// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { Dialog, DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { Observable, catchError, map, of } from 'rxjs';
import { ClipboardCopyService } from '../../core/clipboard/clipboard-copy.service';
import { SecretMetadata, SecretTypeName, VaultApi } from '../../core/vault/vault-api.service';
import { confirmAction } from '../../shared/ui/confirm-dialog';
import { serverMessage } from './create-folder-dialog';
import { MoveSecretDialog, MoveSecretDialogData } from './move-secret-dialog';

export interface SecretDetailData {
  secret: SecretMetadata;
}

interface CopyTarget {
  field: 'username' | 'password' | 'value';
  label: string;
}

const COPY_CONFIRMATION_MS = 2_000;

function copyTargetsFor(type: SecretTypeName): CopyTarget[] {
  switch (type) {
    case 'username-password':
      return [
        { field: 'username', label: 'Copy username' },
        { field: 'password', label: 'Copy password' },
      ];
    case 'pat':
      return [{ field: 'value', label: 'Copy token' }];
    case 'api-key':
      return [{ field: 'value', label: 'Copy API key' }];
    default:
      return [];
  }
}

/**
 * Metadata and the actions the server allows for one secret. A value is read only when the user
 * copies it, and it is handed to the clipboard without being kept in component state.
 */
@Component({
  selector: 'app-secret-detail',
  imports: [DatePipe],
  templateUrl: './secret-detail.html',
})
export class SecretDetail {
  private readonly data = inject<SecretDetailData>(DIALOG_DATA);
  private readonly ref = inject<DialogRef<'changed' | undefined>>(DialogRef);
  private readonly api = inject(VaultApi);
  private readonly clipboard = inject(ClipboardCopyService);
  private readonly dialog = inject(Dialog);
  private readonly router = inject(Router);

  protected readonly secret = this.data.secret;
  protected readonly copyTargets = copyTargetsFor(this.secret.type);
  protected readonly secondsUntilClear = this.clipboard.secondsUntilClear;
  protected readonly copiedField = signal<string | null>(null);
  protected readonly failure = signal<string | null>(null);
  protected readonly busy = signal(false);

  private readonly permissions = toSignal(
    this.api.callerPermissions(this.secret.folderPath).pipe(
      map((result) => result.permissions),
      catchError(() => of([] as string[])),
    ),
    { initialValue: [] as string[] },
  );
  protected readonly canWrite = computed(() => this.permissions().includes('write'));
  protected readonly canDelete = computed(() => this.permissions().includes('delete'));

  protected copy(target: CopyTarget): void {
    this.failure.set(null);
    this.api.readValue(this.secret.id).subscribe({
      next: (value) => {
        const text = value[target.field];
        if (!text) {
          this.failure.set('This secret has no value to copy.');
          return;
        }
        if (!this.clipboard.copy(text)) {
          this.failure.set('Could not copy to the clipboard. The clipboard was not changed.');
          return;
        }
        this.copiedField.set(target.field);
        setTimeout(() => {
          if (this.copiedField() === target.field) {
            this.copiedField.set(null);
          }
        }, COPY_CONFIRMATION_MS);
      },
      error: (error: unknown) =>
        this.failure.set(serverMessage(error, 'You do not have Read permission on this folder.')),
    });
  }

  protected edit(): void {
    this.ref.close();
    void this.router.navigate(['/secrets', this.secret.id, 'edit']);
  }

  protected openHistory(): void {
    this.ref.close();
    void this.router.navigate(['/secrets', this.secret.id, 'edit']);
  }

  protected move(): void {
    this.dialog
      .open<string, MoveSecretDialogData, MoveSecretDialog>(MoveSecretDialog, {
        data: { secret: this.secret },
        role: 'dialog',
        ariaLabelledBy: 'move-secret-title',
        backdropClass: ['bg-text/40'],
      })
      .closed.subscribe((destination) => {
        if (!destination || destination === this.secret.folderPath) {
          return;
        }
        this.run(this.api.moveSecret(this.secret.id, destination));
      });
  }

  protected delete(): void {
    confirmAction(this.dialog, {
      title: `Delete ${this.secret.name}?`,
      message: 'The secret is deleted from this folder. This cannot be undone.',
      confirmLabel: 'Delete secret',
      destructive: true,
    }).subscribe((confirmed) => {
      if (confirmed) {
        this.run(this.api.deleteSecret(this.secret.id));
      }
    });
  }

  private run(request: Observable<unknown>): void {
    this.busy.set(true);
    this.failure.set(null);
    request.subscribe({
      next: () => this.ref.close('changed'),
      error: (error: unknown) => {
        this.busy.set(false);
        this.failure.set(serverMessage(error, 'The change was refused by the server.'));
      },
    });
  }

  protected close(): void {
    this.ref.close();
  }
}
