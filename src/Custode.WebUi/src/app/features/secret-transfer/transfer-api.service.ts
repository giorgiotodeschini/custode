// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { HttpClient, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import type { TransferItem } from './transfer-format';

export type ImportRowStatus = 'valid' | 'invalid' | 'conflict' | 'forbidden' | 'folder-not-found';

export interface ImportRowResult {
  index: number;
  status: ImportRowStatus;
  message: string | null;
}

export interface ImportResponse {
  batchId: string | null;
  dryRun: boolean;
  committed: boolean;
  created: number;
  rows: ImportRowResult[];
}

export interface BackupFile {
  blob: Blob;
  fileName: string;
}

@Injectable({ providedIn: 'root' })
export class TransferApi {
  private readonly http = inject(HttpClient);

  /**
   * Checks the rows (dryRun) or stores them all (commit). The server answers 403 or 422 when a
   * commit is refused; the rows in that answer say why, and nothing is stored.
   */
  importItems(items: readonly TransferItem[], dryRun: boolean, createMissingFolders: boolean): Observable<ImportResponse> {
    return this.http.post<ImportResponse>(
      '/api/secrets/imports',
      { createMissingFolders, items },
      { params: { dryRun: String(dryRun) } },
    );
  }

  /** Requests the encrypted tenant backup. The passphrase is sent once and never kept by the page. */
  createBackup(passphrase: string): Observable<BackupFile> {
    return this.http
      .post('/api/backups', { passphrase }, { responseType: 'blob', observe: 'response' })
      .pipe(map((response: HttpResponse<Blob>) => ({
        blob: response.body as Blob,
        fileName: fileNameFrom(response.headers.get('Content-Disposition')) ?? 'custode-backup.bin',
      })));
  }
}

function fileNameFrom(disposition: string | null): string | null {
  const match = disposition ? /filename="?([^";]+)"?/i.exec(disposition) : null;
  return match?.[1] ?? null;
}
