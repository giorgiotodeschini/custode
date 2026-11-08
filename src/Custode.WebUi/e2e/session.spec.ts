// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { expect, test } from '@playwright/test';
import { mockSignedInSession } from './support/session';

test('shows the same generic message for failed credentials', async ({ page }) => {
  await page.route('**/api/auth/refresh', (route) => route.fulfill({ status: 401 }));
  await page.route('**/api/auth/sign-in', (route) => route.fulfill({ status: 401 }));
  await page.goto('/sign-in');

  await page.getByLabel('Login').fill('nobody');
  await page.getByLabel('Password').fill('wrong password');
  await page.getByRole('button', { name: 'Sign in' }).click();

  await expect(page.getByRole('alert')).toHaveText('Invalid login or password');
  await expect(page.getByLabel('Password')).toHaveValue('');
});

test('does not keep the access token or refresh token in browser storage', async ({ page }) => {
  await page.route('**/api/auth/refresh', (route) => route.fulfill({ status: 401 }));
  await page.route('**/api/auth/sign-in', (route) =>
    route.fulfill({
      json: {
        accessToken: 'header.payload.signature',
        expiresAt: new Date(Date.now() + 15 * 60_000).toISOString(),
      },
    }),
  );
  await page.route('**/api/tenant/policies', (route) =>
    route.fulfill({
      json: { idleTimeoutMinutes: 15, accessTokenLifetimeMinutes: 15, refreshTokenLifetimeDays: 7, clipboardClearSeconds: 30 },
    }),
  );
  await page.goto('/sign-in');

  await page.getByLabel('Login').fill('alice');
  await page.getByLabel('Password').fill('correct horse battery');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.locator('#app-sidebar')).toBeVisible();

  const stored = await page.evaluate(() => ({
    local: JSON.stringify({ ...localStorage }),
    session: JSON.stringify({ ...sessionStorage }),
    cookie: document.cookie,
  }));
  expect(stored.local).not.toContain('header.payload.signature');
  expect(stored.session).not.toContain('header.payload.signature');
  expect(stored.cookie).not.toContain('custode_refresh');
});

test('signing out in one tab shows the sign-in form in another tab', async ({ context }) => {
  const first = await context.newPage();
  const second = await context.newPage();
  for (const page of [first, second]) {
    await mockSignedInSession(page);
  }
  await first.route('**/api/auth/sign-out', (route) => route.fulfill({ status: 204 }));
  await first.goto('/');
  await second.goto('/');
  await expect(second.locator('#app-sidebar')).toBeVisible();

  await first.getByRole('button', { name: 'Sign out' }).click();

  await expect(second).toHaveURL(/\/sign-in$/);
});
