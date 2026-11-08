// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { DatePipe } from '@angular/common';
import { Component, input, output } from '@angular/core';
import { SecretMetadata, SecretTypeName } from '../../core/vault/vault-api.service';
import type { SecretPresentation } from '../../core/preferences/secret-presentation.service';

const TYPE_LABELS: Record<SecretTypeName, string> = {
  'username-password': 'Username and password',
  pat: 'Personal access token',
  'api-key': 'API key',
  file: 'Certificate file',
  structured: 'Structured',
  'key-value': 'Key-value',
};

/** Renders metadata as a dense table or as cards. Values never reach this component. */
@Component({
  selector: 'app-secret-list',
  imports: [DatePipe],
  templateUrl: './secret-list.html',
})
export class SecretList {
  readonly secrets = input.required<readonly SecretMetadata[]>();
  readonly presentation = input.required<SecretPresentation>();
  readonly openSecret = output<SecretMetadata>();

  protected typeLabel(type: SecretTypeName): string {
    return TYPE_LABELS[type];
  }
}
