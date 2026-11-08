// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import type { SecretMetadata, SecretPair, SecretTypeName, SecretValue } from '../../core/vault/vault-api.service';
import { SecretDraft, emptyDraft, validateDraft } from '../secret-editor/secret-model';

export const MAX_TRANSFER_ROWS = 500;
export const EXPORT_FORMAT = 'custode-export';
export const EXPORT_VERSION = 1;

const SECRET_TYPES: readonly SecretTypeName[] = ['username-password', 'pat', 'api-key', 'file', 'structured', 'key-value'];
const FORMULA_START = /^[=+\-@\t\r]/;

/** One secret as it appears in an import file. Values are present only when the file carries them. */
export interface TransferItem {
  name: string;
  type: string;
  folderPath: string;
  username?: string;
  password?: string;
  value?: string;
  fileName?: string;
  contentBase64?: string;
  fields?: Record<string, string>;
  pairs?: SecretPair[];
}

export interface TransferRow {
  index: number;
  item: TransferItem;
  /** Problems found before the file reaches the server. */
  errors: string[];
}

/** Prefixes a cell that a spreadsheet would evaluate as a formula (webui-secret-transfer). */
export function neutralizeCell(value: string): string {
  return FORMULA_START.test(value) ? `'${value}` : value;
}

/** Reverses neutralizeCell for a value written by this application. */
function restoreCell(value: string): string {
  return /^'[=+\-@\t\r]/.test(value) ? value.slice(1) : value;
}

export function toCsv(rows: ReadonlyArray<Record<string, string>>, columns: readonly string[]): string {
  const escape = (cell: string): string => {
    const safe = neutralizeCell(cell);
    return /[",\r\n]/.test(safe) ? `"${safe.replace(/"/g, '""')}"` : safe;
  };
  const lines = [columns.join(',')];
  for (const row of rows) {
    lines.push(columns.map((column) => escape(row[column] ?? '')).join(','));
  }
  return `${lines.join('\r\n')}\r\n`;
}

/** Splits CSV text into rows of cells, honouring quoted cells and doubled quotes. */
export function parseCsv(text: string): string[][] {
  const rows: string[][] = [];
  let row: string[] = [];
  let cell = '';
  let quoted = false;
  for (let index = 0; index < text.length; index++) {
    const char = text[index];
    if (quoted) {
      if (char === '"' && text[index + 1] === '"') {
        cell += '"';
        index++;
      } else if (char === '"') {
        quoted = false;
      } else {
        cell += char;
      }
    } else if (char === '"') {
      quoted = true;
    } else if (char === ',') {
      row.push(cell);
      cell = '';
    } else if (char === '\n' || char === '\r') {
      if (char === '\r' && text[index + 1] === '\n') {
        index++;
      }
      row.push(cell);
      rows.push(row);
      row = [];
      cell = '';
    } else {
      cell += char;
    }
  }
  if (cell.length > 0 || row.length > 0) {
    row.push(cell);
    rows.push(row);
  }
  return rows.filter((line) => line.some((value) => value.length > 0));
}

export function parseCsvItems(text: string): TransferItem[] {
  const [header, ...body] = parseCsv(text);
  if (!header) {
    return [];
  }
  const columns = header.map((name) => name.trim());
  return body.map((cells) => {
    const get = (column: string): string | undefined => {
      const position = columns.indexOf(column);
      return position < 0 ? undefined : restoreCell(cells[position] ?? '');
    };
    const item: TransferItem = {
      name: get('name') ?? '',
      type: get('type') ?? '',
      folderPath: get('folderPath') ?? '',
    };
    for (const key of ['username', 'password', 'value', 'fileName'] as const) {
      const cell = get(key);
      if (cell !== undefined && cell.length > 0) {
        item[key] = cell;
      }
    }
    return item;
  });
}

export function parseJsonItems(text: string): TransferItem[] {
  const parsed: unknown = JSON.parse(text);
  const list = Array.isArray(parsed) ? parsed : (parsed as { secrets?: unknown }).secrets;
  if (!Array.isArray(list)) {
    throw new Error('The file has no list of secrets.');
  }
  return list.map(normalizeJsonItem);
}

/** Accepts both the object form of fields (import) and the pair list an export writes. */
function normalizeJsonItem(raw: unknown): TransferItem {
  const source = (typeof raw === 'object' && raw !== null ? raw : {}) as Record<string, unknown>;
  const text = (key: string): string => (typeof source[key] === 'string' ? (source[key] as string) : '');
  const item: TransferItem = { name: text('name'), type: text('type'), folderPath: text('folderPath') };
  for (const key of ['username', 'password', 'value', 'fileName', 'contentBase64'] as const) {
    const value = text(key);
    if (value.length > 0) {
      item[key] = value;
    }
  }
  const fields = source['fields'];
  if (Array.isArray(fields)) {
    item.fields = Object.fromEntries(fields.map((pair: { key: string; value: string }) => [pair.key, pair.value]));
  } else if (typeof fields === 'object' && fields !== null) {
    item.fields = fields as Record<string, string>;
  }
  const pairs = source['pairs'];
  if (Array.isArray(pairs)) {
    item.pairs = pairs.map((pair: { key: string; value: string }) => ({ key: pair.key, value: pair.value }));
  }
  return item;
}

/** Checks each item with the same rules as the create form, before the server is asked. */
export function checkRows(items: readonly TransferItem[]): TransferRow[] {
  return items.map((item, index) => ({ index, item, errors: checkItem(item) }));
}

function checkItem(item: TransferItem): string[] {
  const errors: string[] = [];
  if (!SECRET_TYPES.includes(item.type as SecretTypeName)) {
    errors.push(`Unsupported secret type "${item.type}".`);
    return errors;
  }
  if (!item.folderPath || !item.folderPath.startsWith('/')) {
    errors.push('The folder path must start with /.');
  }
  if (item.type === 'file' && item.contentBase64 === undefined) {
    errors.push('A certificate file needs the JSON format, because its content is not in CSV.');
    return errors;
  }
  const draft = draftOf(item);
  for (const message of Object.values(validateDraft(draft, 'create', item.contentBase64 !== undefined))) {
    if (message) {
      errors.push(message);
    }
  }
  return errors;
}

export function draftOf(item: TransferItem): SecretDraft {
  const type = SECRET_TYPES.includes(item.type as SecretTypeName) ? (item.type as SecretTypeName) : 'username-password';
  return {
    ...emptyDraft(type, item.folderPath),
    name: item.name,
    username: item.username ?? '',
    password: item.password ?? '',
    value: item.value ?? '',
    fileName: item.fileName ?? null,
    fileSelected: item.contentBase64 !== undefined,
    fields: Object.entries(item.fields ?? {}).map(([name, value]) => ({ name, value })),
    pairs: (item.pairs ?? []).map((pair) => ({ name: pair.key, value: pair.value })),
  };
}

/** Finds rows whose name already appears earlier in the same folder. */
export function duplicateInFile(items: readonly TransferItem[]): Set<number> {
  const seen = new Map<string, number>();
  const duplicates = new Set<number>();
  items.forEach((item, index) => {
    const key = `${item.folderPath}\u0000${item.name.trim()}`;
    if (seen.has(key)) {
      duplicates.add(index);
    } else {
      seen.set(key, index);
    }
  });
  return duplicates;
}

export interface ExportFile {
  format: typeof EXPORT_FORMAT;
  version: typeof EXPORT_VERSION;
  exportedAtUtc: string;
  folderPath: string;
  includesValues: boolean;
  secrets: Array<Record<string, unknown>>;
}

/** Metadata of one exported secret; the value fields are added only when values are included. */
export function exportEntry(metadata: SecretMetadata, value: SecretValue | null): Record<string, unknown> {
  const entry: Record<string, unknown> = {
    folderPath: metadata.folderPath,
    name: metadata.name,
    type: metadata.type,
    fileName: metadata.fileName,
    createdAtUtc: metadata.createdAtUtc,
    updatedAtUtc: metadata.updatedAtUtc,
    version: metadata.version,
  };
  if (value) {
    entry['username'] = value.username;
    entry['password'] = value.password;
    entry['value'] = value.value;
    entry['contentBase64'] = value.contentBase64;
    entry['fields'] = value.fields;
    entry['pairs'] = value.pairs;
  }
  return entry;
}

export function csvRowFor(metadata: SecretMetadata, value: SecretValue | null): Record<string, string> {
  const row: Record<string, string> = {
    folderPath: metadata.folderPath,
    name: metadata.name,
    type: metadata.type,
    fileName: metadata.fileName ?? '',
    createdAtUtc: metadata.createdAtUtc,
    updatedAtUtc: metadata.updatedAtUtc,
  };
  if (value) {
    row['username'] = value.username ?? '';
    row['password'] = value.password ?? '';
    row['value'] = value.value ?? '';
  }
  return row;
}
