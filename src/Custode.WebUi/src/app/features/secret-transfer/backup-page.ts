// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { Subscription } from 'rxjs';
import { TransferApi } from './transfer-api.service';
import { downloadFile } from './export-folder';

export const MIN_PASSPHRASE_LENGTH = 12;

/** Code points, so that a passphrase with accented or symbol characters counts as the user sees it. */
export function passphraseLength(value: string): number {
  return [...value.normalize('NFC')].length;
}

/** Encrypted export of the whole tenant, for tenant administrators only (webui-secret-transfer). */
@Component({
  selector: 'app-backup-page',
  templateUrl: './backup-page.html',
})
export class BackupPage {
  private readonly transfer = inject(TransferApi);

  protected readonly minLength = MIN_PASSPHRASE_LENGTH;
  protected readonly passphrase = signal('');
  protected readonly confirmation = signal('');
  protected readonly attempted = signal(false);
  protected readonly running = signal(false);
  protected readonly status = signal<string | null>(null);
  protected readonly failure = signal<string | null>(null);
  private subscription: Subscription | null = null;

  protected readonly lengthError = computed(() =>
    passphraseLength(this.passphrase()) < MIN_PASSPHRASE_LENGTH
      ? `Use at least ${MIN_PASSPHRASE_LENGTH} characters.`
      : null,
  );
  protected readonly mismatch = computed(() =>
    this.confirmation().length > 0 && this.passphrase().normalize('NFC') !== this.confirmation().normalize('NFC')
      ? 'The two passphrases do not match.'
      : null,
  );
  protected readonly canStart = computed(
    () => !this.running() && this.lengthError() === null && this.mismatch() === null && this.confirmation().length > 0,
  );

  protected start(): void {
    this.attempted.set(true);
    if (!this.canStart()) {
      return;
    }
    const passphrase = this.passphrase().normalize('NFC');
    this.clearPassphrases();
    this.running.set(true);
    this.failure.set(null);
    this.status.set('Preparing the encrypted backup. This can take a while for a large tenant.');
    this.subscription = this.transfer.createBackup(passphrase).subscribe({
      next: ({ blob, fileName }) => {
        downloadFile(blob, fileName, 'application/octet-stream');
        this.running.set(false);
        this.status.set('The encrypted backup was downloaded. Keep its passphrase: it cannot be recovered.');
      },
      error: (error: unknown) => {
        this.running.set(false);
        this.status.set(null);
        this.failure.set(
          error instanceof HttpErrorResponse && error.status === 403
            ? 'Only a tenant administrator can export a backup.'
            : 'The backup could not be created. Nothing was downloaded.',
        );
      },
    });
  }

  protected cancel(): void {
    this.subscription?.unsubscribe();
    this.subscription = null;
    this.running.set(false);
    this.status.set(null);
    this.failure.set('The backup was cancelled. Nothing was downloaded.');
  }

  private clearPassphrases(): void {
    this.passphrase.set('');
    this.confirmation.set('');
    this.attempted.set(false);
  }
}
