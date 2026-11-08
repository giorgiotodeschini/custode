// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { Dialog, DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { Component, inject } from '@angular/core';
import { Observable, map } from 'rxjs';

export interface ConfirmDialogData {
  title: string;
  message: string;
  confirmLabel: string;
  destructive?: boolean;
  /** Optional lines shown as a list, for example the access that would change. */
  details?: string[];
}

@Component({
  selector: 'app-confirm-dialog',
  templateUrl: './confirm-dialog.html',
})
export class ConfirmDialog {
  protected readonly data = inject<ConfirmDialogData>(DIALOG_DATA);
  private readonly ref = inject<DialogRef<boolean>>(DialogRef);

  protected confirm(): void {
    this.ref.close(true);
  }

  protected cancel(): void {
    this.ref.close(false);
  }
}

/** Asks the user to confirm an action. Cancelling resolves to false and changes nothing. */
export function confirmAction(dialog: Dialog, data: ConfirmDialogData): Observable<boolean> {
  return dialog
    .open<boolean, ConfirmDialogData, ConfirmDialog>(ConfirmDialog, {
      data,
      role: 'alertdialog',
      ariaLabelledBy: 'confirm-dialog-title',
      backdropClass: ['bg-text/40'],
    })
    .closed.pipe(map((result) => result === true));
}
