// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { Dialog } from '@angular/cdk/dialog';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { NoticeService } from '../../core/notifications/notice.service';
import { VaultApi } from '../../core/vault/vault-api.service';
import { confirmAction } from '../../shared/ui/confirm-dialog';
import { folderUrl } from '../secret-browser/folders-page';
import { collectFolderSecrets, buildExportFile, downloadFile } from './export-folder';
import { ImportResponse, ImportRowResult, ImportRowStatus, TransferApi } from './transfer-api.service';
import {
  MAX_TRANSFER_ROWS,
  TransferItem,
  checkRows,
  csvRowFor,
  duplicateInFile,
  parseCsvItems,
  parseJsonItems,
  toCsv,
} from './transfer-format';

export type PreviewStatus = ImportRowStatus | 'skipped' | 'unchecked';

export interface PreviewRow {
  index: number;
  item: TransferItem;
  status: PreviewStatus;
  message: string | null;
}

const BASE_CSV_COLUMNS = ['folderPath', 'name', 'type', 'fileName', 'createdAtUtc', 'updatedAtUtc'];
const VALUE_CSV_COLUMNS = ['username', 'password', 'value'];
const STATUS_MESSAGES: Partial<Record<ImportRowStatus, string>> = {
  conflict: 'A secret with this name already exists in the folder.',
  forbidden: 'Create permission is required on the target folder.',
  'folder-not-found': 'The target folder does not exist.',
};

function stamp(): string {
  return new Date().toISOString().slice(0, 10);
}

/** Export and import of secrets as JSON or CSV. Values are included only after confirmation. */
@Component({
  selector: 'app-transfer-page',
  templateUrl: './transfer-page.html',
})
export class TransferPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly vault = inject(VaultApi);
  private readonly transfer = inject(TransferApi);
  private readonly dialog = inject(Dialog);
  private readonly notices = inject(NoticeService);

  // Export
  protected readonly exportFolder = signal(this.route.snapshot.queryParamMap.get('folderPath') ?? '/');
  protected readonly exportFormat = signal<'json' | 'csv'>('json');
  protected readonly includeValues = signal(false);
  protected readonly exporting = signal(false);
  protected readonly exportProgress = signal<{ done: number; total: number } | null>(null);
  protected readonly exportStatus = signal<string | null>(null);
  private exportCancelled = false;

  // Import
  protected readonly items = signal<TransferItem[]>([]);
  protected readonly fileName = signal<string | null>(null);
  protected readonly parseError = signal<string | null>(null);
  protected readonly skipped = signal<ReadonlySet<number>>(new Set());
  protected readonly createMissingFolders = signal(false);
  protected readonly serverRows = signal<Map<number, ImportRowResult> | null>(null);
  protected readonly checkCurrent = signal(false);
  protected readonly checking = signal(false);
  protected readonly checkError = signal<string | null>(null);
  protected readonly importing = signal(false);
  protected readonly refused = signal<ImportRowResult[]>([]);
  protected readonly importError = signal<string | null>(null);
  private checkSubscription: Subscription | null = null;

  private readonly duplicates = computed(() => duplicateInFile(this.items()));
  private readonly clientRows = computed(() => checkRows(this.items()));

  protected readonly preview = computed<PreviewRow[]>(() => {
    const server = this.serverRows();
    const duplicates = this.duplicates();
    const skipped = this.skipped();
    return this.clientRows().map((row): PreviewRow => {
      if (skipped.has(row.index)) {
        return { index: row.index, item: row.item, status: 'skipped', message: null };
      }
      if (row.errors.length > 0) {
        return { index: row.index, item: row.item, status: 'invalid', message: row.errors.join(' ') };
      }
      if (duplicates.has(row.index)) {
        return {
          index: row.index,
          item: row.item,
          status: 'conflict',
          message: 'The same name appears more than once in this folder in the file.',
        };
      }
      const result = server?.get(row.index);
      if (!result) {
        return { index: row.index, item: row.item, status: 'unchecked', message: null };
      }
      return {
        index: row.index,
        item: row.item,
        status: result.status,
        message: result.message ?? STATUS_MESSAGES[result.status] ?? null,
      };
    });
  });

  protected readonly counts = computed(() => {
    const rows = this.preview();
    const count = (status: PreviewStatus) => rows.filter((row) => row.status === status).length;
    return {
      create: count('valid'),
      skipped: count('skipped'),
      conflicts: count('conflict'),
      failing: count('invalid') + count('forbidden') + count('folder-not-found'),
    };
  });

  protected readonly canImport = computed(
    () =>
      this.items().length > 0 &&
      this.checkCurrent() &&
      !this.checking() &&
      !this.importing() &&
      this.counts().create > 0 &&
      this.counts().conflicts === 0 &&
      this.counts().failing === 0,
  );

  protected readonly maxRows = MAX_TRANSFER_ROWS;

  protected exportCurrentFolder(): void {
    if (!this.includeValues()) {
      this.runExport();
      return;
    }
    confirmAction(this.dialog, {
      title: 'Export plaintext values?',
      message: 'The file will contain the secret values in plain text. Keep it safe and delete it when it is no longer needed.',
      confirmLabel: 'Export with values',
      destructive: true,
    }).subscribe((confirmed) => {
      if (confirmed) {
        this.runExport();
      }
    });
  }

  protected cancelExport(): void {
    this.exportCancelled = true;
  }

  protected async chooseFile(files: FileList | null): Promise<void> {
    this.parseError.set(null);
    this.refused.set([]);
    this.importError.set(null);
    const file = files?.[0];
    if (!file) {
      return;
    }
    const lower = file.name.toLowerCase();
    try {
      const text = await file.text();
      const parsed = lower.endsWith('.json')
        ? parseJsonItems(text)
        : lower.endsWith('.csv')
          ? parseCsvItems(text)
          : null;
      if (!parsed) {
        this.parseError.set('Choose a JSON or CSV file.');
        return;
      }
      if (parsed.length === 0) {
        this.parseError.set('The file contains no secrets.');
        return;
      }
      if (parsed.length > MAX_TRANSFER_ROWS) {
        this.parseError.set(`A file can import at most ${MAX_TRANSFER_ROWS} secrets. This file has ${parsed.length}.`);
        return;
      }
      this.fileName.set(file.name);
      this.items.set(parsed);
      this.skipped.set(new Set());
      this.serverRows.set(null);
      this.check();
    } catch {
      this.parseError.set('The file could not be read. Check that it is a valid JSON or CSV export.');
    }
  }

  protected toggleSkip(index: number): void {
    this.skipped.update((current) => {
      const next = new Set(current);
      if (next.has(index)) {
        next.delete(index);
      } else {
        next.add(index);
      }
      return next;
    });
    this.checkCurrent.set(false);
  }

  protected rename(index: number, name: string): void {
    this.items.update((current) => current.map((item, position) => (position === index ? { ...item, name } : item)));
    this.checkCurrent.set(false);
  }

  protected setCreateMissingFolders(value: boolean): void {
    this.createMissingFolders.set(value);
    this.checkCurrent.set(false);
  }

  /** Sends the rows that are not skipped to the server for a dry run. Nothing is stored. */
  protected check(): void {
    const sent = this.sentItems();
    if (sent.items.length === 0) {
      this.serverRows.set(new Map());
      this.checkCurrent.set(true);
      return;
    }
    this.checking.set(true);
    this.checkError.set(null);
    this.checkSubscription?.unsubscribe();
    this.checkSubscription = this.transfer.importItems(sent.items, true, this.createMissingFolders()).subscribe({
      next: (response) => this.applyDryRun(response, sent.indexes),
      error: (error: unknown) => {
        this.checking.set(false);
        this.checkError.set(error instanceof HttpErrorResponse && error.error?.rows ? 'The check was refused.' : 'The check could not be completed.');
      },
    });
  }

  protected cancelCheck(): void {
    this.checkSubscription?.unsubscribe();
    this.checkSubscription = null;
    this.checking.set(false);
    this.checkError.set('The check was cancelled. Nothing was stored.');
  }

  protected importNow(): void {
    if (!this.canImport()) {
      return;
    }
    const sent = this.sentItems();
    this.importing.set(true);
    this.importError.set(null);
    this.refused.set([]);
    this.transfer.importItems(sent.items, false, this.createMissingFolders()).subscribe({
      next: (response) => {
        this.importing.set(false);
        this.notices.show(`Imported ${response.created} ${response.created === 1 ? 'secret' : 'secrets'}.`);
        void this.router.navigateByUrl(folderUrl(sent.items[0]?.folderPath ?? '/'));
      },
      error: (error: unknown) => {
        this.importing.set(false);
        if (error instanceof HttpErrorResponse && (error.status === 403 || error.status === 422)) {
          const body = error.error as ImportResponse | null;
          this.refused.set((body?.rows ?? []).map((row) => ({ ...row, index: sent.indexes[row.index] ?? row.index })));
          this.importError.set('Nothing was stored. Fix the rows below, then check the file again.');
          this.checkCurrent.set(false);
          return;
        }
        this.importError.set(
          'The import result is unknown because the connection failed. Check the folders before trying again, so that no secret is stored twice.',
        );
      },
    });
  }

  protected rowLabel(row: PreviewRow): string {
    return `${row.item.name || '(no name)'} in ${row.item.folderPath || '(no folder)'}`;
  }

  protected refusedLabel(row: ImportRowResult): string {
    const item = this.items()[row.index];
    return item ? `${item.name || '(no name)'} in ${item.folderPath || '(no folder)'}` : `row ${row.index + 1}`;
  }

  private runExport(): void {
    this.exporting.set(true);
    this.exportCancelled = false;
    this.exportStatus.set(null);
    this.exportProgress.set({ done: 0, total: 0 });
    const folder = this.exportFolder().trim() || '/';
    const includeValues = this.includeValues();
    collectFolderSecrets(this.vault, folder, {
      includeValues,
      onProgress: (done, total) => {
        this.exportProgress.set({ done, total });
        return !this.exportCancelled;
      },
    })
      .then((collected) => {
        this.exporting.set(false);
        this.exportProgress.set(null);
        if (!collected) {
          this.exportStatus.set('The export was cancelled. Nothing was downloaded.');
          return;
        }
        const count = collected.entries.length;
        const suffix = collected.skippedUnreadable > 0 ? ` ${collected.skippedUnreadable} unreadable folders were left out.` : '';
        const base = `custode-${folder.replace(/[^a-z0-9]+/gi, '-').replace(/^-|-$/g, '') || 'root'}-${stamp()}`;
        if (this.exportFormat() === 'json') {
          downloadFile(JSON.stringify(buildExportFile(folder, collected, includeValues), null, 2), `${base}.json`, 'application/json');
        } else {
          const columns = includeValues ? [...BASE_CSV_COLUMNS, ...VALUE_CSV_COLUMNS] : BASE_CSV_COLUMNS;
          const rows = collected.entries.map((entry) => csvRowFor(entry.metadata, entry.value));
          downloadFile(toCsv(rows, columns), `${base}.csv`, 'text/csv');
        }
        this.exportStatus.set(`Exported ${count} ${count === 1 ? 'secret' : 'secrets'}.${suffix}`);
      })
      .catch(() => {
        this.exporting.set(false);
        this.exportProgress.set(null);
        this.exportStatus.set('The export could not be completed. Nothing was downloaded.');
      });
  }

  private sentItems(): { items: TransferItem[]; indexes: number[] } {
    const skipped = this.skipped();
    const indexes: number[] = [];
    const items: TransferItem[] = [];
    this.items().forEach((item, index) => {
      if (!skipped.has(index)) {
        indexes.push(index);
        items.push(item);
      }
    });
    return { items, indexes };
  }

  private applyDryRun(response: ImportResponse, indexes: number[]): void {
    const map = new Map<number, ImportRowResult>();
    for (const row of response.rows) {
      const original = indexes[row.index];
      if (original !== undefined) {
        map.set(original, { ...row, index: original });
      }
    }
    this.serverRows.set(map);
    this.checkCurrent.set(true);
    this.checking.set(false);
  }
}
