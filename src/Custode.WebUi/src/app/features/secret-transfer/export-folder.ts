// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { SecretMetadata, SecretValue, VaultApi } from '../../core/vault/vault-api.service';
import { EXPORT_FORMAT, EXPORT_VERSION, ExportFile, exportEntry } from './transfer-format';

export interface ExportCollected {
  entries: Array<{ metadata: SecretMetadata; value: SecretValue | null }>;
  skippedUnreadable: number;
}

export interface ExportOptions {
  includeValues: boolean;
  /** Called after each secret is read; returns false to stop the export. */
  onProgress: (done: number, total: number) => boolean;
}

/**
 * Lists the readable secrets of a folder and of its readable subfolders. Folders or secrets the
 * server refuses are left out and counted, never shown with their names.
 */
export async function collectFolderSecrets(
  api: VaultApi,
  folderPath: string,
  options: ExportOptions,
): Promise<ExportCollected | null> {
  const folders = await readableFolderTree(api, folderPath);
  const metadata: SecretMetadata[] = [];
  let skippedUnreadable = 0;
  for (const folder of folders) {
    try {
      metadata.push(...(await firstValueFrom(api.listSecrets(folder))));
    } catch (error: unknown) {
      if (error instanceof HttpErrorResponse && (error.status === 403 || error.status === 404)) {
        skippedUnreadable += 1;
        continue;
      }
      throw error;
    }
  }

  const entries: ExportCollected['entries'] = [];
  for (const [position, item] of metadata.entries()) {
    const value = options.includeValues ? await firstValueFrom(api.readValue(item.id)) : null;
    entries.push({ metadata: item, value });
    if (!options.onProgress(position + 1, metadata.length)) {
      return null;
    }
  }
  return { entries, skippedUnreadable };
}

async function readableFolderTree(api: VaultApi, root: string): Promise<string[]> {
  const found = [root];
  const pending = [root];
  while (pending.length > 0) {
    const parent = pending.shift() ?? root;
    let children: Array<{ path: string }> = [];
    try {
      children = await firstValueFrom(api.listFolders(parent));
    } catch {
      children = [];
    }
    for (const child of children) {
      found.push(child.path);
      pending.push(child.path);
    }
  }
  return found;
}

export function buildExportFile(folderPath: string, collected: ExportCollected, includesValues: boolean): ExportFile {
  return {
    format: EXPORT_FORMAT,
    version: EXPORT_VERSION,
    exportedAtUtc: new Date().toISOString(),
    folderPath,
    includesValues,
    secrets: collected.entries.map((entry) => exportEntry(entry.metadata, entry.value)),
  };
}

/** Starts a browser download of the given content. */
export function downloadFile(content: BlobPart, fileName: string, type: string): void {
  const url = URL.createObjectURL(new Blob([content], { type }));
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = fileName;
  anchor.click();
  URL.revokeObjectURL(url);
}
