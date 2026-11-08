// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { Component, computed, inject, signal } from '@angular/core';
import type { SecretMetadata } from '../../core/vault/vault-api.service';

export interface MoveSecretDialogData {
  secret: SecretMetadata;
}

/** Asks for the destination folder. Moving is confirmed by the Move button. */
@Component({
  selector: 'app-move-secret-dialog',
  templateUrl: './move-secret-dialog.html',
})
export class MoveSecretDialog {
  protected readonly secret = inject<MoveSecretDialogData>(DIALOG_DATA).secret;
  private readonly ref = inject<DialogRef<string>>(DialogRef);

  /** Plain signals instead of a FormControl: a control set up after the first keystroke would overwrite the text. */
  protected readonly destination = signal(this.secret.folderPath);
  protected readonly touched = signal(false);
  protected readonly destinationInvalid = computed(() => !/^\/.*/.test(this.destination().trim()));

  protected move(): void {
    if (this.destinationInvalid()) {
      this.touched.set(true);
      return;
    }
    this.ref.close(this.destination().trim());
  }

  protected cancel(): void {
    this.ref.close();
  }
}
