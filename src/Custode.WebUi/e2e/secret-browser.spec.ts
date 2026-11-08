// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { expect, test } from '@playwright/test';
import { PASSWORD, mockVault } from './support/vault';
import { mockSignedInSession } from './support/session';

test.beforeEach(async ({ context }) => {
  await context.grantPermissions(['clipboard-read', 'clipboard-write']);
});

test.describe('folder tree and listing', () => {
  test('shows the folders from the server and opens a folder by URL', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await mockSignedInSession(page);
    await mockVault(page);
    await page.goto('/folders');

    const tree = page.getByRole('tree', { name: 'Folders' });
    await expect(tree.getByRole('treeitem', { name: 'production' })).toBeVisible();
    await expect(tree.getByRole('treeitem', { name: 'staging' })).toBeVisible();

    await tree.getByRole('treeitem', { name: 'production' }).getByRole('button', { name: 'production', exact: true }).click();

    await expect(page).toHaveURL(/\/folders\/production$/);
    await expect(page.locator('table')).toContainText('prod-db');
  });

  test('expanding a folder loads its subfolders', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await mockSignedInSession(page);
    await mockVault(page);
    await page.goto('/folders');

    await page.getByRole('button', { name: 'Expand production' }).click();

    await expect(page.getByRole('treeitem', { name: 'api' })).toBeVisible();
  });

  test('listings and the detail panel show no secret value', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await mockSignedInSession(page);
    await mockVault(page);
    await page.goto('/folders/production');
    await expect(page.locator('table')).toContainText('prod-db');

    await expect(page.locator('body')).not.toContainText(PASSWORD);
    await page.getByRole('button', { name: 'Details for prod-db' }).click();
    await expect(page.getByRole('dialog')).toContainText('prod-db');
    await expect(page.getByRole('dialog')).not.toContainText(PASSWORD);
  });
});

test.describe('presentation', () => {
  test('a narrow viewport starts with cards and switching does not reload the list', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 800 });
    await mockSignedInSession(page);
    const vault = await mockVault(page);
    await page.goto('/folders/production');
    await expect(page.getByRole('list', { name: 'Secrets in this folder' })).toBeVisible();
    const listingsBefore = vault.secretListings;

    await page.getByRole('button', { name: 'Table' }).click();

    await expect(page.getByRole('table')).toBeVisible();
    expect(vault.secretListings).toBe(listingsBefore);
  });

  test('the chosen presentation is restored for the same account only', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await mockSignedInSession(page);
    await mockVault(page);
    await page.goto('/folders/production');
    await page.getByRole('button', { name: 'Cards' }).click();
    await expect(page.getByRole('list', { name: 'Secrets in this folder' })).toBeVisible();

    await page.reload();

    await expect(page.getByRole('list', { name: 'Secrets in this folder' })).toBeVisible();
    const keys = await page.evaluate(() => Object.keys(localStorage).filter((key) => key.startsWith('custode.secrets.presentation.')));
    expect(keys).toHaveLength(1);
  });
});

test.describe('copy', () => {
  test('copies the password to the clipboard without showing it and confirms briefly', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await mockSignedInSession(page);
    await mockVault(page);
    await page.goto('/folders/production');
    await page.getByRole('button', { name: 'Details for prod-db' }).click();

    await page.getByRole('button', { name: 'Copy password' }).click();

    await expect(page.getByRole('button', { name: 'Copied' })).toBeVisible();
    await expect(page.getByRole('dialog')).not.toContainText(PASSWORD);
    await expect(page.getByText(/The clipboard clears in \d+ seconds/)).toBeVisible();
    expect(await page.evaluate(() => navigator.clipboard.readText())).toBe(PASSWORD);
  });

  test('a refused read shows the server message and leaves the clipboard unchanged', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await mockSignedInSession(page);
    await mockVault(page, { valueStatus: 403 });
    await page.goto('/folders/production');
    await page.evaluate(() => navigator.clipboard.writeText('unrelated text'));
    await page.getByRole('button', { name: 'Details for prod-db' }).click();

    await page.getByRole('button', { name: 'Copy password' }).click();

    await expect(page.getByRole('alert')).toHaveText('You do not have Read permission on this folder.');
    expect(await page.evaluate(() => navigator.clipboard.readText())).toBe('unrelated text');
  });

  test('clears the clipboard after the tenant delay', async ({ page }) => {
    test.setTimeout(60_000);
    await page.setViewportSize({ width: 1280, height: 800 });
    await mockSignedInSession(page);
    await mockVault(page, { clipboardClearSeconds: 10 });
    await page.goto('/folders/production');
    await page.getByRole('button', { name: 'Details for prod-db' }).click();

    await page.getByRole('button', { name: 'Copy password' }).click();
    await expect(page.getByRole('button', { name: 'Copied' })).toBeVisible();
    await expect.poll(() => page.evaluate(() => navigator.clipboard.readText()), { timeout: 20_000 }).toBe('');
  });
});

test.describe('actions by permission', () => {
  test('a read-only user sees no edit, move, or delete action', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await mockSignedInSession(page);
    await mockVault(page, { permissions: ['read'] });
    await page.goto('/folders/production');
    await page.getByRole('button', { name: 'Details for prod-db' }).click();

    await expect(page.getByRole('dialog').getByRole('button', { name: 'Copy password' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Edit' })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Move' })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Delete' })).toHaveCount(0);
  });

  test('cancelling the delete confirmation deletes nothing', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await mockSignedInSession(page);
    const vault = await mockVault(page);
    await page.goto('/folders/production');
    await page.getByRole('button', { name: 'Details for prod-db' }).click();

    await page.getByRole('dialog').getByRole('button', { name: 'Delete', exact: true }).click();
    await page.getByRole('alertdialog').getByRole('button', { name: 'Cancel' }).click();

    expect(vault.deletes).toHaveLength(0);
    await expect(page.locator('table')).toContainText('prod-db');
  });

  test('confirming the delete sends the request', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await mockSignedInSession(page);
    const vault = await mockVault(page);
    await page.goto('/folders/production');
    await page.getByRole('button', { name: 'Details for prod-db' }).click();

    await page.getByRole('dialog').getByRole('button', { name: 'Delete', exact: true }).click();
    await page.getByRole('alertdialog').getByRole('button', { name: 'Delete secret' }).click();

    await expect.poll(() => vault.deletes.length).toBe(1);
  });

  test('a non-empty folder is not deleted and the server reason is shown', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await mockSignedInSession(page);
    await mockVault(page);
    await page.goto('/folders/production');

    await page.getByRole('button', { name: 'Delete folder' }).click();
    await page.getByRole('alertdialog').getByRole('button', { name: 'Delete folder' }).click();

    await expect(page.getByRole('alert')).toHaveText('The folder is not empty.');
  });
});
