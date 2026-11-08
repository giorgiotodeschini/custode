// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import type { Page } from '@playwright/test';

export const ALICE = 'u-alice';
export const BOB = 'u-bob';

export interface TenantRecord {
  created: Array<{ login: string; initialPassword: string }>;
  enabledChanges: Array<{ id: string; enabled: boolean }>;
  administratorChanges: Array<{ id: string; isAdministrator: boolean }>;
  groupsCreated: string[];
  groupsDeleted: string[];
  groupMemberAdds: Array<{ groupId: string; userId: string }>;
  groupMemberRemovals: Array<{ groupId: string; userId: string }>;
  policyPuts: Array<Record<string, number>>;
}

interface Member {
  id: string;
  login: string;
  roles: string[];
  groupCount: number;
  isEnabled: boolean;
  mustChangePassword: boolean;
}

interface Group {
  id: string;
  name: string;
  memberCount: number;
  grantCount: number;
  members: string[];
}

const POLICY_LIMITS = {
  idleTimeoutMinutes: [5, 120],
  refreshTokenLifetimeDays: [1, 30],
  clipboardClearSeconds: [10, 120],
} as const;

/**
 * An in-memory tenant: members, roles, groups, and policies. The rules that the server enforces
 * (duplicate logins and names, the last enabled administrator, policy ranges) are applied here too.
 */
export async function mockTenant(page: Page, options: { policyRefusal?: string[] } = {}): Promise<TenantRecord> {
  const record: TenantRecord = {
    created: [],
    enabledChanges: [],
    administratorChanges: [],
    groupsCreated: [],
    groupsDeleted: [],
    groupMemberAdds: [],
    groupMemberRemovals: [],
    policyPuts: [],
  };
  const members: Member[] = [
    { id: ALICE, login: 'alice', roles: ['TenantAdministrator'], groupCount: 1, isEnabled: true, mustChangePassword: false },
    { id: BOB, login: 'bob', roles: [], groupCount: 1, isEnabled: true, mustChangePassword: false },
  ];
  const groups: Group[] = [
    { id: 'g-ops', name: 'Operators', memberCount: 2, grantCount: 3, members: [ALICE, BOB] },
    { id: 'g-dev', name: 'Developers', memberCount: 0, grantCount: 0, members: [] },
  ];
  const policy = { idleTimeoutMinutes: 30, accessTokenLifetimeMinutes: 15, refreshTokenLifetimeDays: 7, clipboardClearSeconds: 30 };

  const problem = (status: number, detail: string) => ({ status, body: { status, detail } });
  const adminsEnabled = () => members.filter((member) => member.isEnabled && member.roles.includes('TenantAdministrator')).length;
  const refreshGroupCounts = () => groups.forEach((group) => (group.memberCount = group.members.length));
  const memberByLogin = (login: string) => members.find((member) => member.login.toLowerCase() === login.toLowerCase());

  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const method = request.method();
    const path = url.pathname;
    const send = (body: unknown, status = 200) => route.fulfill({ status, json: body });
    const fail = (status: number, detail: string) => send({ status, detail }, status);

    if (path === '/api/tenant/members' && method === 'GET') {
      return send(members);
    }
    if (path === '/api/tenant/members' && method === 'POST') {
      const body = JSON.parse(request.postData() ?? '{}') as { login: string; initialPassword: string };
      record.created.push(body);
      if (memberByLogin(body.login)) {
        const failure = problem(409, 'A member with this login already exists.');
        return fail(failure.status, failure.body.detail);
      }
      const created: Member = { id: `u-${body.login}`, login: body.login, roles: [], groupCount: 0, isEnabled: true, mustChangePassword: true };
      members.push(created);
      return send({ id: created.id, login: created.login }, 201);
    }
    const toggle = /^\/api\/tenant\/members\/([^/]+)\/(enable|disable)$/.exec(path);
    if (toggle && method === 'POST') {
      const member = members.find((candidate) => candidate.id === toggle[1]);
      if (member) {
        member.isEnabled = toggle[2] === 'enable';
        record.enabledChanges.push({ id: member.id, enabled: member.isEnabled });
      }
      return route.fulfill({ status: 204 });
    }
    const admin = /^\/api\/tenant\/members\/([^/]+)\/administrator$/.exec(path);
    if (admin && method === 'PUT') {
      const body = JSON.parse(request.postData() ?? '{}') as { isAdministrator: boolean };
      const member = members.find((candidate) => candidate.id === admin[1]);
      if (member) {
        const isAdmin = member.roles.includes('TenantAdministrator');
        if (!body.isAdministrator && isAdmin && member.isEnabled && adminsEnabled() <= 1) {
          return fail(409, 'At least one enabled tenant administrator is required.');
        }
        member.roles = body.isAdministrator ? [...member.roles, 'TenantAdministrator'] : member.roles.filter((role) => role !== 'TenantAdministrator');
        record.administratorChanges.push({ id: member.id, isAdministrator: body.isAdministrator });
      }
      return route.fulfill({ status: 204 });
    }

    if (path === '/api/access/groups' && method === 'GET') {
      refreshGroupCounts();
      return send(groups.map(({ members: _members, ...group }) => group));
    }
    if (path === '/api/access/groups' && method === 'POST') {
      const body = JSON.parse(request.postData() ?? '{}') as { name: string };
      if (groups.some((group) => group.name.toLowerCase() === body.name.toLowerCase())) {
        return fail(409, 'A group with this name already exists.');
      }
      record.groupsCreated.push(body.name);
      const created: Group = { id: `g-${body.name}`, name: body.name, memberCount: 0, grantCount: 0, members: [] };
      groups.push(created);
      return send({ id: created.id, name: created.name, memberCount: 0, grantCount: 0 }, 201);
    }
    const group = /^\/api\/access\/groups\/([^/]+)$/.exec(path);
    if (group && method === 'DELETE') {
      record.groupsDeleted.push(group[1] ?? '');
      const index = groups.findIndex((candidate) => candidate.id === group[1]);
      if (index >= 0) groups.splice(index, 1);
      return route.fulfill({ status: 204 });
    }
    const members$ = /^\/api\/access\/groups\/([^/]+)\/members$/.exec(path);
    if (members$ && method === 'GET') {
      const target = groups.find((candidate) => candidate.id === members$[1]);
      return send((target?.members ?? []).map((id) => members.find((member) => member.id === id)).filter(Boolean).map((member) => ({ id: member!.id, login: member!.login })));
    }
    if (members$ && method === 'POST') {
      const body = JSON.parse(request.postData() ?? '{}') as { userId: string };
      record.groupMemberAdds.push({ groupId: members$[1] ?? '', userId: body.userId });
      groups.find((candidate) => candidate.id === members$[1])?.members.push(body.userId);
      return route.fulfill({ status: 204 });
    }
    const memberRemove = /^\/api\/access\/groups\/([^/]+)\/members\/([^/]+)$/.exec(path);
    if (memberRemove && method === 'DELETE') {
      record.groupMemberRemovals.push({ groupId: memberRemove[1] ?? '', userId: memberRemove[2] ?? '' });
      const target = groups.find((candidate) => candidate.id === memberRemove[1]);
      if (target) target.members = target.members.filter((id) => id !== memberRemove[2]);
      return route.fulfill({ status: 204 });
    }

    if (path === '/api/tenant/policies' && method === 'GET') {
      return send(policy);
    }
    if (path === '/api/tenant/policies' && method === 'PUT') {
      const body = JSON.parse(request.postData() ?? '{}') as Record<string, number>;
      record.policyPuts.push(body);
      if (options.policyRefusal) {
        return send({ errors: options.policyRefusal }, 400);
      }
      const missing = ['idleTimeoutMinutes', 'accessTokenLifetimeMinutes', 'refreshTokenLifetimeDays', 'clipboardClearSeconds'].filter((field) => body[field] === undefined);
      if (missing.length > 0) {
        return send({ errors: missing.map((field) => `${field} is required.`) }, 400);
      }
      const errors = Object.entries(POLICY_LIMITS)
        .filter(([field, [min, max]]) => body[field] !== undefined && (body[field] < min || body[field] > max))
        .map(([field, [min, max]]) => `${field} must be between ${min} and ${max}.`);
      if (errors.length > 0) {
        return send({ errors }, 400);
      }
      Object.assign(policy, body);
      return send(policy);
    }
    return route.fallback();
  });

  return record;
}
