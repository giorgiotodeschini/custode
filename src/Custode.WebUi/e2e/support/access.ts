// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import type { Page } from '@playwright/test';

export interface AccessMockOptions {
  /** Status for every change request, with its reason; the state is then left as it was. */
  refuse?: { status: number; detail: string };
}

export interface AccessRecord {
  puts: Array<{ folderPath: string; principalKind: string; principalId: string; permissions: string[] }>;
  deletes: Array<{ folderPath: string; principalId: string }>;
  breaks: string[];
  restores: string[];
}

const OPERATORS = { id: 'g-operators', name: 'Operators' };
const ALICE = { id: 'u-alice', login: 'alice' };

interface FolderState {
  path: string;
  parentPath: string | null;
  inheritsPermissions: boolean;
}

/**
 * An in-memory tenant ACL. Reads compute effective grants from the stored direct grants and the
 * inheritance flags, the same way the server does, so that a change is visible only after it.
 */
export async function mockAccess(page: Page, options: AccessMockOptions = {}): Promise<AccessRecord> {
  const record: AccessRecord = { puts: [], deletes: [], breaks: [], restores: [] };
  const folders: FolderState[] = [
    { path: '/', parentPath: null, inheritsPermissions: false },
    { path: '/production', parentPath: '/', inheritsPermissions: true },
    { path: '/production/aws', parentPath: '/production', inheritsPermissions: true },
    { path: '/staging', parentPath: '/', inheritsPermissions: false },
  ];
  const direct: Record<string, Record<string, string[]>> = {
    '/production': { [`group:${OPERATORS.id}`]: ['read'] },
    '/staging': {},
  };

  const chainOf = (path: string): string[] => {
    const chain: string[] = [];
    let current: FolderState | undefined = folders.find((folder) => folder.path === path);
    while (current) {
      chain.push(current.path);
      current = current.parentPath ? folders.find((folder) => folder.path === current?.parentPath) : undefined;
    }
    return chain;
  };

  const grantsFor = (path: string) => {
    const folder = folders.find((candidate) => candidate.path === path);
    if (!folder) {
      return null;
    }
    const chain = chainOf(path);
    const principals = new Set<string>([...Object.keys(direct[path] ?? {})]);
    for (const ancestor of chain.slice(1)) {
      if (folder.inheritsPermissions) {
        Object.keys(direct[ancestor] ?? {}).forEach((key) => principals.add(key));
      }
    }
    return [...principals].map((key) => {
      const [kind, id] = key.split(':') as [string, string];
      const name = kind === 'group' ? OPERATORS.name : ALICE.login;
      const sources: Array<{ folderPath: string; permissions: string[] }> = [];
      const own = direct[path]?.[key];
      if (own) {
        sources.push({ folderPath: path, permissions: own });
      }
      if (folder.inheritsPermissions) {
        for (const ancestor of chain.slice(1)) {
          const levels = direct[ancestor]?.[key];
          if (levels) {
            sources.push({ folderPath: ancestor, permissions: levels });
          }
        }
      }
      const effective = [...new Set(sources.flatMap((source) => source.permissions))];
      return { principalKind: kind, principalId: id, principalName: name, sources, effectivePermissions: effective };
    });
  };

  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const method = request.method();
    const path = url.pathname;
    const json = (body: unknown, status = 200) => route.fulfill({ status, json: body });
    const refused = () => (options.refuse ? json({ status: options.refuse.status, detail: options.refuse.detail }, options.refuse.status) : null);

    if (path === '/api/access/grants' && method === 'GET') {
      return json(grantsFor(url.searchParams.get('folderPath') ?? '/') ?? []);
    }
    if (path === '/api/folders' && method === 'GET') {
      const parent = url.searchParams.get('path') ?? '/';
      return json(folders.filter((folder) => folder.parentPath === parent).map((folder) => ({ path: folder.path, inheritsPermissions: folder.inheritsPermissions })));
    }
    if (path === '/api/access/folders' && method === 'GET') {
      return json(folders.map((folder) => ({ path: folder.path, parentPath: folder.parentPath, inheritsPermissions: folder.inheritsPermissions })));
    }
    if (path === '/api/access/groups' && method === 'GET') {
      return json([{ id: OPERATORS.id, name: OPERATORS.name, memberCount: 1, grantCount: 1 }]);
    }
    if (path === '/api/tenant/members' && method === 'GET') {
      return json([{ id: ALICE.id, login: ALICE.login, roles: ['TenantAdministrator'], groupCount: 1, isEnabled: true, mustChangePassword: false }]);
    }
    if (path === '/api/access/grants' && method === 'PUT') {
      const body = JSON.parse(request.postData() ?? '{}') as { folderPath: string; principalKind: string; principalId: string; permissions: string[] };
      record.puts.push(body);
      const failure = refused();
      if (failure) {
        return failure;
      }
      direct[body.folderPath] = { ...(direct[body.folderPath] ?? {}), [`${body.principalKind}:${body.principalId}`]: body.permissions };
      return route.fulfill({ status: 204 });
    }
    if (path === '/api/access/grants' && method === 'DELETE') {
      const folder = url.searchParams.get('folderPath') ?? '/';
      const principalId = url.searchParams.get('principalId') ?? '';
      record.deletes.push({ folderPath: folder, principalId });
      const failure = refused();
      if (failure) {
        return failure;
      }
      const grantsOfFolder = { ...(direct[folder] ?? {}) };
      Object.keys(grantsOfFolder).forEach((key) => {
        if (key.endsWith(principalId)) {
          delete grantsOfFolder[key];
        }
      });
      direct[folder] = grantsOfFolder;
      return route.fulfill({ status: 204 });
    }
    if (path === '/api/access/inheritance/break' && method === 'POST') {
      const folder = url.searchParams.get('folderPath') ?? '/';
      record.breaks.push(folder);
      const failure = refused();
      if (failure) {
        return failure;
      }
      const target = folders.find((candidate) => candidate.path === folder);
      if (target) {
        target.inheritsPermissions = false;
      }
      return route.fulfill({ status: 204 });
    }
    if (path === '/api/access/inheritance/restore' && method === 'POST') {
      const folder = url.searchParams.get('folderPath') ?? '/';
      record.restores.push(folder);
      const failure = refused();
      if (failure) {
        return failure;
      }
      const target = folders.find((candidate) => candidate.path === folder);
      if (target) {
        target.inheritsPermissions = true;
      }
      return route.fulfill({ status: 204 });
    }
    return route.fallback();
  });

  return record;
}
