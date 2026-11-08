// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { expect, test } from '@playwright/test';
import { mockSignedInSession } from './support/session';
import { mockVault } from './support/vault';

test.beforeEach(async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
});

test('a session that ends in another tab closes the open detail panel', async ({ context }) => {
  const first = await context.newPage();
  const second = await context.newPage();
  for (const page of [first, second]) {
    await mockSignedInSession(page);
    await mockVault(page);
  }
  await second.route('**/api/auth/sign-out', (route) => route.fulfill({ status: 204 }));
  await first.goto('/folders/production');
  await second.goto('/folders');
  await first.getByRole('button', { name: 'Details for prod-db' }).click();
  await expect(first.getByRole('dialog')).toBeVisible();

  await second.getByRole('button', { name: 'Sign out' }).click();

  await expect(first).toHaveURL(/\/sign-in/);
  await expect(first.getByRole('dialog')).toHaveCount(0);
});

test('a forced sign-out does not ask to discard unsaved edits', async ({ context }) => {
  const first = await context.newPage();
  const second = await context.newPage();
  for (const page of [first, second]) {
    await mockSignedInSession(page);
    await mockVault(page);
  }
  await second.route('**/api/auth/sign-out', (route) => route.fulfill({ status: 204 }));
  await first.goto('/secrets/new?folderPath=/production');
  await first.getByLabel('Name', { exact: true }).fill('unsaved draft');
  await second.goto('/folders');

  await second.getByRole('button', { name: 'Sign out' }).click();

  await expect(first).toHaveURL(/\/sign-in/);
  await expect(first.getByRole('alertdialog')).toHaveCount(0);
});
