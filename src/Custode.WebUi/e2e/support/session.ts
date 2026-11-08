// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import type { Page } from '@playwright/test';

function encodeSegment(value: object): string {
  return Buffer.from(JSON.stringify(value)).toString('base64url');
}

/**
 * Answers the session endpoints so that the shell can be tested without the server.
 * The token is not verified by the web UI; it only supplies claims for the screen.
 */
export async function mockSignedInSession(page: Page, options: { admin?: boolean } = {}): Promise<void> {
  const token = `${encodeSegment({ alg: 'HS256', typ: 'JWT' })}.${encodeSegment({
    sub: 'user-1',
    tenant_id: 'tenant-1',
    sid: 'session-1',
    role: options.admin ? ['Member', 'TenantAdministrator'] : ['Member'],
  })}.signature`;
  const expiresAt = new Date(Date.now() + 15 * 60_000).toISOString();

  await page.route('**/api/auth/refresh', (route) => route.fulfill({ json: { accessToken: token, expiresAt } }));
  await page.route('**/api/auth/activity', (route) => route.fulfill({ status: 204 }));
}
