// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { VaultApi } from '../../core/vault/vault-api.service';

export interface CreateFolderDialogData {
  parentPath: string;
}

/** Creates a folder under the current folder. The server states why a path is refused. */
@Component({
  selector: 'app-create-folder-dialog',
  templateUrl: './create-folder-dialog.html',
})
export class CreateFolderDialog {
  private readonly data = inject<CreateFolderDialogData>(DIALOG_DATA);
  private readonly api = inject(VaultApi);
  private readonly ref = inject<DialogRef<string>>(DialogRef);

  protected readonly parentPath = this.data.parentPath;
  /** Plain signals instead of a FormControl: a control set up after the first keystroke would overwrite the text. */
  protected readonly name = signal('');
  protected readonly touched = signal(false);
  protected readonly nameInvalid = computed(() => {
    const value = this.name().trim();
    return value.length === 0 || value.includes('/');
  });
  protected readonly submitting = signal(false);
  protected readonly failure = signal<string | null>(null);

  protected create(): void {
    if (this.nameInvalid() || this.submitting()) {
      this.touched.set(true);
      return;
    }
    const name = this.name().trim();
    const path = this.parentPath === '/' ? `/${name}` : `${this.parentPath}/${name}`;
    this.submitting.set(true);
    this.failure.set(null);
    this.api.createFolder(path).subscribe({
      next: () => this.ref.close(path),
      error: (error: unknown) => {
        this.submitting.set(false);
        this.failure.set(serverMessage(error, 'The folder could not be created.'));
      },
    });
  }

  protected cancel(): void {
    this.ref.close();
  }
}

export function serverMessage(error: unknown, fallback: string): string {
  if (error instanceof HttpErrorResponse) {
    const detail = (error.error as { detail?: unknown } | null)?.detail;
    if (typeof detail === 'string' && detail.length > 0) {
      return detail;
    }
  }
  return fallback;
}
