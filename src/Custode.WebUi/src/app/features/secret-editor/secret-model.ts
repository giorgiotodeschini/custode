// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import type { SecretTypeName, SecretValue, SecretWriteBody } from '../../core/vault/vault-api.service';

export const SUPPORTED_FILE_EXTENSIONS: readonly string[] = ['.crt', '.cer', '.pem', '.pfx', '.p12', '.key'];

/** Matches the server default. The server is authoritative; this check only avoids a useless upload. */
export const MAX_FILE_BYTES = 1024 * 1024;
export const MAX_FIELD_NAME_LENGTH = 128;
export const MAX_FIELD_VALUE_LENGTH = 4096;
export const MAX_STRUCTURED_FIELDS = 100;
export const MAX_KEY_VALUE_PAIRS = 200;

export interface NamedEntry {
  name: string;
  value: string;
}

/** Everything the form holds, except a file's content, which is kept outside the form. */
export interface SecretDraft {
  name: string;
  folderPath: string;
  type: SecretTypeName;
  username: string;
  password: string;
  value: string;
  fields: NamedEntry[];
  pairs: NamedEntry[];
  fileName: string | null;
  fileSelected: boolean;
}

export type DraftErrors = Partial<Record<string, string>>;

export function emptyDraft(type: SecretTypeName = 'username-password', folderPath = '/'): SecretDraft {
  return {
    name: '',
    folderPath,
    type,
    username: '',
    password: '',
    value: '',
    fields: [],
    pairs: [],
    fileName: null,
    fileSelected: false,
  };
}

export function fileExtensionAllowed(fileName: string): boolean {
  const dot = fileName.lastIndexOf('.');
  const extension = dot < 0 ? '' : fileName.slice(dot).toLowerCase();
  return SUPPORTED_FILE_EXTENSIONS.includes(extension);
}

/** Checks the draft before any request is sent. Keys are field identifiers used by the form. */
export function validateDraft(draft: SecretDraft, mode: 'create' | 'edit', hasNewFile: boolean): DraftErrors {
  const errors: DraftErrors = {};
  if (mode === 'create' && draft.name.trim().length === 0) {
    errors['name'] = 'Enter a name for the secret.';
  }
  if (mode === 'create' && !draft.folderPath) {
    errors['folder'] = 'Choose a folder you can create secrets in.';
  }

  switch (draft.type) {
    case 'username-password':
      if (draft.username.length === 0) {
        errors['username'] = 'Enter a username.';
      }
      if (draft.password.length === 0) {
        errors['password'] = 'Enter a password.';
      }
      break;
    case 'pat':
    case 'api-key':
      if (draft.value.length === 0) {
        errors['value'] = 'Enter a value.';
      }
      break;
    case 'file':
      if (!hasNewFile && !draft.fileSelected) {
        errors['file'] = 'Select a certificate file.';
      }
      break;
    case 'structured':
      validateNamedEntries(draft.fields, 'field', MAX_STRUCTURED_FIELDS, errors, 'Add at least one field.');
      break;
    case 'key-value':
      validateNamedEntries(draft.pairs, 'pair', MAX_KEY_VALUE_PAIRS, errors, 'Add at least one pair.');
      break;
  }
  return errors;
}

function validateNamedEntries(
  entries: NamedEntry[],
  prefix: string,
  maximum: number,
  errors: DraftErrors,
  emptyMessage: string,
): void {
  if (entries.length === 0) {
    errors[`${prefix}-list`] = emptyMessage;
  }
  if (entries.length > maximum) {
    errors[`${prefix}-list`] = `Use at most ${maximum} ${prefix === 'field' ? 'fields' : 'pairs'}.`;
  }
  const seen = new Map<string, number>();
  entries.forEach((entry, index) => {
    const name = entry.name.trim();
    if (name.length === 0) {
      errors[`${prefix}-${index}-name`] = prefix === 'field' ? 'Enter a field name.' : 'Enter a key.';
    } else if (name.length > MAX_FIELD_NAME_LENGTH) {
      errors[`${prefix}-${index}-name`] = `Use at most ${MAX_FIELD_NAME_LENGTH} characters.`;
    } else if (seen.has(name)) {
      errors[`${prefix}-${index}-name`] = prefix === 'field' ? 'This field name already exists.' : 'This key already exists.';
      const first = seen.get(name);
      if (first !== undefined && errors[`${prefix}-${first}-name`] === undefined) {
        errors[`${prefix}-${first}-name`] = prefix === 'field' ? 'This field name already exists.' : 'This key already exists.';
      }
    } else {
      seen.set(name, index);
    }
    if (entry.value.length > MAX_FIELD_VALUE_LENGTH) {
      errors[`${prefix}-${index}-value`] = `Use at most ${MAX_FIELD_VALUE_LENGTH} characters.`;
    }
  });
}

export function hasErrors(errors: DraftErrors): boolean {
  return Object.values(errors).some((message) => message !== undefined);
}

/** Builds the request body for a create or an update. Content is passed in, never read from the draft. */
export function buildWriteBody(draft: SecretDraft, fileContentBase64: string | null): SecretWriteBody {
  const body: SecretWriteBody = { name: draft.name.trim(), type: draft.type };
  switch (draft.type) {
    case 'username-password':
      body.username = draft.username;
      body.password = draft.password;
      break;
    case 'pat':
    case 'api-key':
      body.value = draft.value;
      break;
    case 'file':
      body.fileName = draft.fileName ?? undefined;
      body.contentBase64 = fileContentBase64 ?? undefined;
      break;
    case 'structured':
      body.fields = Object.fromEntries(draft.fields.map((entry) => [entry.name.trim(), entry.value]));
      break;
    case 'key-value':
      body.pairs = draft.pairs.map((entry) => ({ key: entry.name.trim(), value: entry.value }));
      break;
  }
  return body;
}

/** Turns a loaded value into the draft, so that edits start from the stored content. */
export function draftFromValue(value: SecretValue): SecretDraft {
  return {
    name: value.name,
    folderPath: value.folderPath,
    type: value.type,
    username: value.username ?? '',
    password: value.password ?? '',
    value: value.value ?? '',
    fields: (value.fields ?? []).map((pair) => ({ name: pair.key, value: pair.value })),
    pairs: (value.pairs ?? []).map((pair) => ({ name: pair.key, value: pair.value })),
    fileName: value.fileName,
    fileSelected: false,
  };
}

/** Size of the stored file, derived from its base64 content without showing the content. */
export function fileSizeFromBase64(contentBase64: string | null): number | null {
  if (!contentBase64) {
    return null;
  }
  const padding = contentBase64.endsWith('==') ? 2 : contentBase64.endsWith('=') ? 1 : 0;
  return Math.floor((contentBase64.length * 3) / 4) - padding;
}

export type FieldStatus = 'unchanged' | 'changed' | 'added' | 'removed';

export interface FieldDiff {
  field: string;
  status: FieldStatus;
  before: string | null;
  after: string | null;
  /** File content is compared but never revealed. */
  revealable: boolean;
}

/** Compares the fields of two versions. Values are returned for the view to hide or reveal. */
export function diffVersions(before: SecretValue, after: SecretValue): FieldDiff[] {
  const left = fieldsOf(before);
  const right = fieldsOf(after);
  const names = [...new Set([...left.keys(), ...right.keys()])];
  return names.map((field) => {
    const previous = left.get(field) ?? null;
    const next = right.get(field) ?? null;
    const status: FieldStatus =
      previous === null ? 'added' : next === null ? 'removed' : previous === next ? 'unchanged' : 'changed';
    return { field, status, before: previous, after: next, revealable: field !== 'file content' };
  });
}

function fieldsOf(value: SecretValue): Map<string, string> {
  const fields = new Map<string, string>();
  switch (value.type) {
    case 'username-password':
      fields.set('username', value.username ?? '');
      fields.set('password', value.password ?? '');
      break;
    case 'pat':
    case 'api-key':
      fields.set('value', value.value ?? '');
      break;
    case 'file':
      fields.set('file name', value.fileName ?? '');
      fields.set('file content', value.contentBase64 ?? '');
      break;
    case 'structured':
      for (const pair of value.fields ?? []) {
        fields.set(pair.key, pair.value);
      }
      break;
    case 'key-value':
      for (const pair of value.pairs ?? []) {
        fields.set(pair.key, pair.value);
      }
      break;
  }
  return fields;
}
