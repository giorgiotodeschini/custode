// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { expect, test } from '@playwright/test';
import { mockSignedInSession } from './support/session';
import { mockVault } from './support/vault';

test.beforeEach(async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 800 });
});

test('moving a secret asks for the destination and sends it to the server', async ({ page }) => {
  await mockSignedInSession(page);
  const vault = await mockVault(page);
  await page.goto('/folders/production');
  await page.getByRole('button', { name: 'Details for prod-db' }).click();

  await page.getByRole('dialog').getByRole('button', { name: 'Move', exact: true }).click();
  await page.getByLabel('Destination folder').fill('/staging');
  await page.getByRole('button', { name: 'Move secret' }).click();

  await expect.poll(() => vault.moves.length).toBe(1);
  expect(vault.moves[0]).toEqual({ id: '11111111-1111-4111-8111-111111111111', folderPath: '/staging' });
});

test('a new folder is created under the current folder', async ({ page }) => {
  await mockSignedInSession(page);
  const vault = await mockVault(page);
  await page.goto('/folders/production');

  await page.getByRole('button', { name: 'New folder' }).click();
  await page.getByLabel('Folder name').fill('api-keys');
  await page.getByRole('button', { name: 'Create folder' }).click();

  await expect.poll(() => vault.createdFolders).toEqual(['/production/api-keys']);
});

test('arrow keys move between the visible folders', async ({ page }) => {
  await mockSignedInSession(page);
  await mockVault(page);
  await page.goto('/folders');

  await page.getByRole('treeitem', { name: 'production' }).focus();
  await page.keyboard.press('ArrowDown');

  await expect(page.getByRole('treeitem', { name: 'staging' })).toBeFocused();
});
