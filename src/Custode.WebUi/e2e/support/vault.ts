// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import type { Page } from '@playwright/test';

export const PASSWORD = 'correct-horse-7';
export const PROD_DB = '11111111-1111-4111-8111-111111111111';
export const PROD_TOKEN = '22222222-2222-4222-8222-222222222222';

export interface VaultMockOptions {
  permissions?: string[];
  valueStatus?: number;
  clipboardClearSeconds?: number;
  /** Status returned by PUT /api/secrets/{id}, for example 412 for a stale version token. */
  saveStatus?: number;
  /** Status returned by POST /api/secrets. */
  createStatus?: number;
  /** Stored version token of the secret; its value is returned in the ETag header. */
  currentVersion?: number;
  /** Status per import row index, for example conflict or invalid. */
  importStatuses?: Record<number, string>;
  /** Status returned for the backup request. */
  backupStatus?: number;
}

export interface VaultRecord {
  secretListings: number;
  deletes: string[];
  createdFolders: string[];
  valueReads: number;
  moves: Array<{ id: string; folderPath: string }>;
  creates: Array<Record<string, unknown>>;
  updates: Array<{ id: string; ifMatch: string | null; body: Record<string, unknown> }>;
  rollbacks: Array<{ id: string; number: number; ifMatch: string | null }>;
  imports: Array<{ dryRun: boolean; items: Array<Record<string, unknown>>; createMissingFolders: boolean }>;
  backups: string[];
}

const FOLDERS: Record<string, Array<{ path: string; inheritsPermissions: boolean }>> = {
  '/': [
    { path: '/production', inheritsPermissions: true },
    { path: '/staging', inheritsPermissions: true },
  ],
  '/production': [{ path: '/production/api', inheritsPermissions: false }],
};

const SECRETS: Record<string, unknown[]> = {
  '/production': [
    {
      id: PROD_DB,
      name: 'prod-db',
      type: 'username-password',
      folderPath: '/production',
      fileName: null,
      createdAtUtc: '2026-11-01T10:00:00Z',
      updatedAtUtc: '2026-11-02T10:00:00Z',
      version: 2,
    },
    {
      id: PROD_TOKEN,
      name: 'prod-api-token',
      type: 'pat',
      folderPath: '/production',
      fileName: null,
      createdAtUtc: '2026-11-01T10:00:00Z',
      updatedAtUtc: '2026-11-03T10:00:00Z',
      version: 1,
    },
  ],
};

const VERSIONS = [
  { number: 1, name: 'prod-db', fileName: null, changeKind: 'created', createdAtUtc: '2026-11-01T10:00:00Z', createdByUserId: '00000000-0000-4000-8000-000000000001', sourceVersion: null },
  { number: 2, name: 'prod-db', fileName: null, changeKind: 'updated', createdAtUtc: '2026-11-02T10:00:00Z', createdByUserId: '00000000-0000-4000-8000-000000000001', sourceVersion: null },
];

function valueOfVersion(number: number): Record<string, unknown> {
  return {
    id: PROD_DB,
    name: 'prod-db',
    type: 'username-password',
    folderPath: '/production',
    fileName: null,
    username: 'deploy',
    password: number === 1 ? 'old-password-1' : PASSWORD,
    value: null,
    contentBase64: null,
    version: number,
  };
}

/**
 * Answers the vault endpoints. Listings carry metadata only; a value is returned only by the
 * value endpoints, which the copy action and the editor call.
 */
export async function mockVault(page: Page, options: VaultMockOptions = {}): Promise<VaultRecord> {
  const record: VaultRecord = {
    secretListings: 0,
    deletes: [],
    createdFolders: [],
    valueReads: 0,
    moves: [],
    creates: [],
    updates: [],
    rollbacks: [],
    imports: [],
    backups: [],
  };
  const permissions = options.permissions ?? ['read', 'write', 'create', 'delete'];
  const etag = `"${options.currentVersion ?? 2}"`;

  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const method = request.method();
    const path = url.pathname;
    const json = (body: unknown, status = 200, headers: Record<string, string> = {}) =>
      route.fulfill({ status, json: body, headers });
    const problem = (status: number, detail: string) => json({ status, detail }, status);

    if (path === '/api/folders' && method === 'GET') {
      return json(FOLDERS[url.searchParams.get('path') ?? '/'] ?? []);
    }
    if (path === '/api/folders' && method === 'POST') {
      record.createdFolders.push((JSON.parse(request.postData() ?? '{}') as { path: string }).path);
      return route.fulfill({ status: 204 });
    }
    if (path === '/api/folders' && method === 'DELETE') {
      return problem(409, 'The folder is not empty.');
    }
    if (path === '/api/secrets' && method === 'GET') {
      record.secretListings += 1;
      return json(SECRETS[url.searchParams.get('folderPath') ?? '/'] ?? []);
    }
    if (path === '/api/secrets' && method === 'POST') {
      const body = JSON.parse(request.postData() ?? '{}') as Record<string, unknown>;
      record.creates.push(body);
      if (options.createStatus) {
        return problem(options.createStatus, 'Create permission is required on this folder.');
      }
      return json({ id: PROD_TOKEN, name: body['name'], type: body['type'], folderPath: body['folderPath'], fileName: null, createdAtUtc: '2026-11-09T10:00:00Z', updatedAtUtc: '2026-11-09T10:00:00Z', version: 1 }, 200, { ETag: '"1"' });
    }

    const value = /^\/api\/secrets\/([^/]+)\/value$/.exec(path);
    if (value && method === 'GET') {
      record.valueReads += 1;
      if (options.valueStatus) {
        return problem(options.valueStatus, 'You do not have Read permission on this folder.');
      }
      return json(valueOfVersion(options.currentVersion ?? 2), 200, { ETag: etag });
    }

    const versions = /^\/api\/secrets\/([^/]+)\/versions$/.exec(path);
    if (versions && method === 'GET') {
      return json(VERSIONS);
    }
    const versionValue = /^\/api\/secrets\/([^/]+)\/versions\/(\d+)\/value$/.exec(path);
    if (versionValue && method === 'GET') {
      return json(valueOfVersion(Number(versionValue[2])));
    }
    const rollback = /^\/api\/secrets\/([^/]+)\/versions\/(\d+)\/rollback$/.exec(path);
    if (rollback && method === 'POST') {
      record.rollbacks.push({ id: rollback[1], number: Number(rollback[2]), ifMatch: request.headers()['if-match'] ?? null });
      return json({ id: rollback[1], name: 'prod-db', type: 'username-password', folderPath: '/production', fileName: null, createdAtUtc: '2026-11-01T10:00:00Z', updatedAtUtc: '2026-11-09T10:00:00Z', version: 3 }, 200, { ETag: '"3"' });
    }

    if (path === '/api/secrets/imports' && method === 'POST') {
      const body = JSON.parse(request.postData() ?? '{}') as { createMissingFolders: boolean; items: Array<Record<string, unknown>> };
      const dryRun = url.searchParams.get('dryRun') === 'true';
      record.imports.push({ dryRun, items: body.items, createMissingFolders: body.createMissingFolders });
      const rows = body.items.map((_, index) => {
        const status = options.importStatuses?.[index] ?? 'valid';
        return { index, status, message: status === 'valid' ? null : 'Rejected by the server.' };
      });
      const refused = rows.some((row) => row.status !== 'valid');
      if (!dryRun && refused) {
        return json({ batchId: null, dryRun: false, committed: false, created: 0, rows }, 422);
      }
      return json({
        batchId: dryRun ? null : '33333333-3333-4333-8333-333333333333',
        dryRun,
        committed: !dryRun,
        created: dryRun ? 0 : body.items.length,
        rows,
      });
    }
    if (path === '/api/backups' && method === 'POST') {
      const body = JSON.parse(request.postData() ?? '{}') as { passphrase: string };
      record.backups.push(body.passphrase);
      if (options.backupStatus) {
        return problem(options.backupStatus, 'Only a tenant administrator can export a backup.');
      }
      return route.fulfill({
        status: 200,
        body: Buffer.from('encrypted-backup-bytes'),
        headers: {
          'Content-Type': 'application/octet-stream',
          'Content-Disposition': 'attachment; filename="custode-tenant-20261108.cbak"',
          'Cache-Control': 'no-store',
        },
      });
    }

    const update = /^\/api\/secrets\/([^/]+)$/.exec(path);
    if (update && method === 'PUT') {
      const ifMatch = request.headers()['if-match'] ?? null;
      record.updates.push({ id: update[1], ifMatch, body: JSON.parse(request.postData() ?? '{}') as Record<string, unknown> });
      if (ifMatch === null) {
        return problem(428, 'An If-Match header carrying the secret\'s version token is required.');
      }
      if (options.saveStatus) {
        return problem(options.saveStatus, 'The secret changed since it was read. Reload it and try again.');
      }
      return json({ id: update[1], name: 'prod-db', type: 'username-password', folderPath: '/production', fileName: null, createdAtUtc: '2026-11-01T10:00:00Z', updatedAtUtc: '2026-11-09T10:00:00Z', version: 3 }, 200, { ETag: '"3"' });
    }

    const move = /^\/api\/secrets\/([^/]+)\/move$/.exec(path);
    if (move && method === 'POST') {
      const body = JSON.parse(request.postData() ?? '{}') as { folderPath: string };
      record.moves.push({ id: move[1], folderPath: body.folderPath });
      return json({ id: move[1], name: 'prod-db', type: 'username-password', folderPath: body.folderPath, fileName: null, createdAtUtc: '2026-11-01T10:00:00Z', updatedAtUtc: '2026-11-09T10:00:00Z', version: 2 });
    }
    const secret = /^\/api\/secrets\/([^/]+)$/.exec(path);
    if (secret && method === 'DELETE') {
      record.deletes.push(secret[1]);
      return route.fulfill({ status: 204 });
    }
    if (path === '/api/access/me/permissions') {
      return json({ folderPath: url.searchParams.get('folderPath'), permissions });
    }
    if (path === '/api/tenant/policies') {
      return json({
        idleTimeoutMinutes: 15,
        accessTokenLifetimeMinutes: 15,
        refreshTokenLifetimeDays: 7,
        clipboardClearSeconds: options.clipboardClearSeconds ?? 30,
      });
    }
    return route.fallback();
  });

  return record;
}
