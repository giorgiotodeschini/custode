// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import type { Page } from '@playwright/test';

export const ALICE_ID = '00000000-0000-4000-8000-00000000a11c';
export const DATA_LAYER = '00000000-0000-0000-0000-000000000000';
export const PAGE_SIZE = 3;

export interface AuditMockOptions {
  /** Makes the stored hash of this sequence disagree with the next entry's previous hash. */
  breakLinkAt?: number;
  verify?: { isIntact: boolean; entriesVerified: number; firstAlteredSequence: number | null };
}

export interface AuditRecord {
  requests: URL[];
}

function entriesFor(options: AuditMockOptions) {
  const now = Date.now();
  const hashes = (sequence: number) => `hash-${sequence}`;
  return Array.from({ length: 7 }, (_, index) => {
    const sequence = index + 1;
    const previous = sequence === 1 ? '' : hashes(sequence - 1);
    return {
      sequence,
      occurredAtUtc: new Date(now - (8 - sequence) * 60_000).toISOString(),
      actorUserId: sequence === 2 ? DATA_LAYER : ALICE_ID,
      action: sequence % 2 === 0 ? 'secret value read' : 'secret created',
      outcome: sequence === 5 ? 'denied' : 'succeeded',
      targetType: 'secret',
      targetId: `s-${sequence}`,
      secondaryTargetType: null,
      secondaryTargetId: null,
      hash: hashes(sequence),
      previousHash: sequence === options.breakLinkAt ? 'tampered' : previous,
    };
  });
}

/** Answers the audit endpoints: sequence cursor paging, the four filters, and the verification. */
export async function mockAudit(page: Page, options: AuditMockOptions = {}): Promise<AuditRecord> {
  const record: AuditRecord = { requests: [] };
  const all = entriesFor(options);

  await page.route('**/api/tenant/members', (route) =>
    route.fulfill({
      json: [{ id: ALICE_ID, login: 'alice', roles: ['TenantAdministrator'], groupCount: 0, isEnabled: true, mustChangePassword: false }],
    }),
  );
  await page.route('**/api/audit/verify', (route) =>
    route.fulfill({
      json: options.verify ?? { isIntact: true, entriesVerified: all.length, firstAlteredSequence: null },
    }),
  );
  await page.route('**/api/audit?*', (route) => {
    const url = new URL(route.request().url());
    record.requests.push(url);
    let entries = all;
    const outcome = url.searchParams.get('outcome');
    const action = url.searchParams.get('action');
    const user = url.searchParams.get('userId');
    if (outcome) entries = entries.filter((entry) => entry.outcome === outcome);
    if (action) entries = entries.filter((entry) => entry.action === action);
    if (user) entries = entries.filter((entry) => entry.actorUserId === user);
    const after = url.searchParams.get('afterSequence');
    if (after) entries = entries.filter((entry) => entry.sequence > Number(after));
    const page = entries.slice(0, PAGE_SIZE);
    const next = entries.length > PAGE_SIZE ? page[page.length - 1]?.sequence ?? null : null;
    return route.fulfill({ json: { entries: page, nextAfterSequence: next } });
  });

  return record;
}
