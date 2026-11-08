// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { Dialog } from '@angular/cdk/dialog';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { Observable, map } from 'rxjs';
import { FolderCatalogService } from '../../core/access/folder-catalog.service';
import { NoticeService } from '../../core/notifications/notice.service';
import { SessionService } from '../../core/session/session.service';
import { SecretTypeName, VaultApi } from '../../core/vault/vault-api.service';
import { confirmAction } from '../../shared/ui/confirm-dialog';
import { NotFoundPage } from '../not-found/not-found-page';
import { folderUrl } from '../secret-browser/folders-page';
import { serverMessage } from '../secret-browser/create-folder-dialog';
import { SecretVersionHistory } from './secret-version-history';
import {
  MAX_FILE_BYTES,
  NamedEntry,
  SUPPORTED_FILE_EXTENSIONS,
  SecretDraft,
  buildWriteBody,
  draftFromValue,
  emptyDraft,
  fileExtensionAllowed,
  fileSizeFromBase64,
  hasErrors,
  validateDraft,
} from './secret-model';

export const TYPE_LABELS: Record<SecretTypeName, string> = {
  'username-password': 'Username and password',
  pat: 'Personal access token',
  'api-key': 'API key',
  file: 'Certificate file',
  structured: 'Structured (named fields)',
  'key-value': 'Key-value pairs',
};

type LoadState = 'loading' | 'ready' | 'missing' | 'failed';
type EntryKind = 'fields' | 'pairs';

function toBase64(buffer: ArrayBuffer): string {
  const bytes = new Uint8Array(buffer);
  let binary = '';
  const chunk = 0x8000;
  for (let offset = 0; offset < bytes.length; offset += chunk) {
    binary += String.fromCharCode(...bytes.subarray(offset, offset + chunk));
  }
  return btoa(binary);
}

/** Full-page form for creating and editing a secret of any supported type. */
@Component({
  selector: 'app-secret-editor-page',
  imports: [NotFoundPage, SecretVersionHistory],
  templateUrl: './secret-editor-page.html',
})
export class SecretEditorPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(VaultApi);
  private readonly catalog = inject(FolderCatalogService);
  private readonly notices = inject(NoticeService);
  private readonly dialog = inject(Dialog);
  private readonly session = inject(SessionService);

  protected readonly typeLabels = TYPE_LABELS;
  protected readonly typeOptions = Object.keys(TYPE_LABELS) as SecretTypeName[];
  protected readonly extensions = SUPPORTED_FILE_EXTENSIONS.join(', ');
  protected readonly maxFileKiB = MAX_FILE_BYTES / 1024;

  private readonly secretId = toSignal(this.route.paramMap.pipe(map((params) => params.get('id'))), {
    initialValue: this.route.snapshot.paramMap.get('id'),
  });
  protected readonly mode = computed<'create' | 'edit'>(() => (this.secretId() ? 'edit' : 'create'));

  protected readonly loadState = signal<LoadState>('loading');
  protected readonly draft = signal<SecretDraft>(emptyDraft());
  protected readonly baseline = signal('');
  protected readonly etag = signal<string | null>(null);
  protected readonly canWrite = signal(false);
  protected readonly readOnly = computed(() => this.mode() === 'edit' && (this.loadState() !== 'ready' || !this.canWrite()));
  protected readonly folderChoices = signal<string[]>([]);

  protected readonly attempted = signal(false);
  protected readonly saving = signal(false);
  protected readonly saveError = signal<string | null>(null);
  protected readonly keptEditsNotice = signal<string | null>(null);
  protected readonly conflict = signal(false);
  protected readonly revealed = signal<ReadonlySet<string>>(new Set());

  protected readonly pendingFile = signal<{ name: string; size: number } | null>(null);
  protected readonly fileError = signal<string | null>(null);
  protected readonly storedFile = signal<{ name: string; size: number | null } | null>(null);
  private newFileContent: string | null = null;
  private storedFileContent: string | null = null;

  protected readonly dirty = computed(
    () => JSON.stringify(this.draft()) !== this.baseline() || this.pendingFile() !== null,
  );
  protected readonly errors = computed(() =>
    validateDraft(this.draft(), this.mode(), this.pendingFile() !== null),
  );
  protected readonly visibleErrors = computed(() => (this.attempted() ? this.errors() : {}));
  protected readonly saveBlocked = computed(() => {
    if (this.readOnly() || this.saving() || this.conflict()) {
      return true;
    }
    if (this.mode() === 'create') {
      return this.loadState() !== 'ready' || this.folderChoices().length === 0;
    }
    return this.loadState() !== 'ready' || this.etag() === null;
  });

  constructor() {
    if (this.mode() === 'edit') {
      this.loadSecret();
    } else {
      this.loadCreateChoices();
    }
  }

  /** Called by the unsaved-changes guard before the user leaves the form. */
  confirmLeave(): Observable<boolean> | boolean {
    if (!this.session.signedIn() || !this.dirty() || this.saving()) {
      return true;
    }
    return confirmAction(this.dialog, {
      title: 'Discard unsaved changes?',
      message: 'Leaving this form discards the changes you have made.',
      confirmLabel: 'Discard changes',
      destructive: true,
    });
  }

  protected valueOf(event: Event): string {
    return (event.target as HTMLInputElement | HTMLSelectElement).value;
  }

  protected setDraft(patch: Partial<SecretDraft>): void {
    this.draft.update((current) => ({ ...current, ...patch }));
  }

  protected setType(type: SecretTypeName): void {
    this.draft.update((current) => ({
      ...current,
      type,
      fields: type === 'structured' && current.fields.length === 0 ? [{ name: '', value: '' }] : current.fields,
      pairs: type === 'key-value' && current.pairs.length === 0 ? [{ name: '', value: '' }] : current.pairs,
    }));
  }

  protected addEntry(kind: EntryKind): void {
    this.draft.update((current) => ({ ...current, [kind]: [...current[kind], { name: '', value: '' }] }));
  }

  protected removeEntry(kind: EntryKind, index: number): void {
    this.draft.update((current) => ({ ...current, [kind]: current[kind].filter((_, position) => position !== index) }));
  }

  protected setEntry(kind: EntryKind, index: number, part: keyof NamedEntry, value: string): void {
    this.draft.update((current) => ({
      ...current,
      [kind]: current[kind].map((entry, position) => (position === index ? { ...entry, [part]: value } : entry)),
    }));
  }

  protected entries(kind: EntryKind): NamedEntry[] {
    return this.draft()[kind];
  }

  protected isRevealed(key: string): boolean {
    return this.revealed().has(key);
  }

  protected toggleReveal(key: string): void {
    this.revealed.update((current) => {
      const next = new Set(current);
      if (next.has(key)) {
        next.delete(key);
      } else {
        next.add(key);
      }
      return next;
    });
  }

  protected async onFiles(files: FileList | null): Promise<void> {
    this.fileError.set(null);
    if (!files || files.length !== 1) {
      this.fileError.set('Select exactly one certificate file.');
      return;
    }
    const file = files[0];
    if (!fileExtensionAllowed(file.name)) {
      this.fileError.set(`Unsupported file type. Use one of: ${this.extensions}.`);
      return;
    }
    if (file.size > MAX_FILE_BYTES) {
      this.fileError.set(`The file is larger than the ${this.maxFileKiB} KiB limit.`);
      return;
    }
    this.newFileContent = toBase64(await file.arrayBuffer());
    this.pendingFile.set({ name: file.name, size: file.size });
    this.setDraft({ fileName: file.name, fileSelected: true });
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    void this.onFiles(event.dataTransfer?.files ?? null);
  }

  protected save(): void {
    this.attempted.set(true);
    if (this.saveBlocked() || hasErrors(this.errors())) {
      return;
    }
    const draft = this.draft();
    const content = this.newFileContent ?? this.storedFileContent;
    const body = buildWriteBody(draft, content);
    this.saving.set(true);
    this.saveError.set(null);
    this.keptEditsNotice.set(null);

    if (this.mode() === 'create') {
      this.api.createSecret({ ...body, folderPath: draft.folderPath }).subscribe({
        next: () => this.finish(draft, 'Saved ' + draft.name.trim() + '.'),
        error: (error: unknown) => this.fail(error, 'Create permission is required on this folder. Nothing was saved.'),
      });
      return;
    }

    const id = this.secretId() ?? '';
    this.api.updateSecret(id, body, this.etag() ?? '').subscribe({
      next: (response) => {
        this.etag.set(response.headers.get('ETag') ?? this.etag());
        this.finish(draft, 'Saved ' + draft.name.trim() + '.');
      },
      error: (error: unknown) => {
        if (error instanceof HttpErrorResponse && error.status === 412) {
          this.saving.set(false);
          this.conflict.set(true);
          return;
        }
        this.fail(error, 'Write permission is required on this folder. Nothing was saved.');
      },
    });
  }

  /** Discards the form and loads the latest version from the server. */
  protected reloadLatest(): void {
    this.conflict.set(false);
    this.saveError.set(null);
    this.loadSecret();
  }

  /** Keeps the form as it is and attaches it to the newer version, so the next save replaces it. */
  protected keepEdits(): void {
    const id = this.secretId();
    if (!id) {
      return;
    }
    this.api.readVersionedValue(id).subscribe({
      next: ({ etag }) => {
        this.etag.set(etag);
        this.conflict.set(false);
        this.keptEditsNotice.set('Your edits are kept. Saving now replaces the newer version.');
      },
      error: (error: unknown) => this.fail(error, 'The latest version could not be loaded.'),
    });
  }

  protected cancel(): void {
    void this.router.navigateByUrl(folderUrl(this.draft().folderPath || '/'));
  }

  protected reloadAfterRestore(): void {
    this.loadSecret();
  }

  protected storedFileNotice(): string | null {
    const stored = this.storedFile();
    if (!stored || this.pendingFile()) {
      return null;
    }
    return stored.size === null ? stored.name : `${stored.name}, ${stored.size} bytes`;
  }

  private loadSecret(): void {
    const id = this.secretId();
    if (!id) {
      return;
    }
    this.loadState.set('loading');
    this.conflict.set(false);
    this.api.readVersionedValue(id).subscribe({
      next: ({ value, etag }) => {
        const loaded = draftFromValue(value);
        this.draft.set(loaded);
        this.baseline.set(JSON.stringify(loaded));
        this.etag.set(etag || null);
        this.storedFileContent = value.contentBase64;
        this.newFileContent = null;
        this.pendingFile.set(null);
        this.storedFile.set(
          value.fileName ? { name: value.fileName, size: fileSizeFromBase64(value.contentBase64) } : null,
        );
        this.loadState.set('ready');
        this.loadPermissions(value.folderPath);
      },
      error: (error: unknown) => {
        const denied = error instanceof HttpErrorResponse && (error.status === 403 || error.status === 404);
        this.loadState.set(denied ? 'missing' : 'failed');
      },
    });
  }

  private loadPermissions(folderPath: string): void {
    this.api.callerPermissions(folderPath).subscribe({
      next: (result) => this.canWrite.set(result.permissions.includes('write')),
      error: () => this.canWrite.set(false),
    });
  }

  private loadCreateChoices(): void {
    this.catalog.createableFolders().subscribe((paths) => {
      this.folderChoices.set(paths);
      const requested = this.route.snapshot.queryParamMap.get('folderPath');
      const folderPath = requested && paths.includes(requested) ? requested : (paths[0] ?? '');
      this.draft.set(emptyDraft('username-password', folderPath));
      this.baseline.set(JSON.stringify(this.draft()));
      this.loadState.set('ready');
      this.canWrite.set(true);
    });
  }

  private finish(draft: SecretDraft, message: string): void {
    this.baseline.set(JSON.stringify(draft));
    this.pendingFile.set(null);
    this.saving.set(false);
    this.notices.show(message);
    void this.router.navigateByUrl(folderUrl(draft.folderPath));
  }

  private fail(error: unknown, forbiddenMessage: string): void {
    this.saving.set(false);
    if (error instanceof HttpErrorResponse && error.status === 403) {
      this.saveError.set(forbiddenMessage);
      return;
    }
    this.saveError.set(serverMessage(error, 'The secret could not be saved.'));
  }
}
