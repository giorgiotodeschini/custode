// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { expect, test } from '@playwright/test';
import { mockSignedInSession } from './support/session';

test.describe('sidebar entries', () => {
  test('a regular user sees Folders and Settings only', async ({ page }) => {
    await mockSignedInSession(page);
    await page.goto('/folders');

    const nav = page.getByRole('navigation', { name: 'Main' });
    await expect(nav.getByRole('link', { name: 'Folders' })).toBeVisible();
    await expect(nav.getByRole('link', { name: 'Settings' })).toBeVisible();
    await expect(nav.getByRole('link', { name: 'Audit Logs' })).toHaveCount(0);
    await expect(nav.getByRole('link', { name: 'Tenant Administration' })).toHaveCount(0);
  });

  test('a tenant administrator sees every entry and the current one is marked', async ({ page }) => {
    await mockSignedInSession(page, { admin: true });
    await page.goto('/folders');

    const nav = page.getByRole('navigation', { name: 'Main' });
    await expect(nav.getByRole('link', { name: 'Audit Logs' })).toBeVisible();
    await expect(nav.getByRole('link', { name: 'Tenant Administration' })).toBeVisible();
    await expect(nav.getByRole('link', { name: 'Folders' })).toHaveAttribute('aria-current', 'page');
  });
});

test.describe('administration routes', () => {
  test('a regular user opening the audit log is sent to folders with a notice', async ({ page }) => {
    await mockSignedInSession(page);
    await page.goto('/audit');

    await expect(page).toHaveURL(/\/folders$/);
    await expect(page.getByRole('status')).toHaveText(/This page is not available/);
  });

  test('an unknown address shows the not-found page with a link to folders', async ({ page }) => {
    await mockSignedInSession(page);
    await page.goto('/no-such-screen');

    await expect(page.getByRole('heading', { name: 'Page not found' })).toBeVisible();
    await page.getByRole('link', { name: 'Back to folders' }).click();
    await expect(page).toHaveURL(/\/folders$/);
  });
});

test.describe('command palette', () => {
  test('Ctrl+K opens it with focus in the search field, and Escape returns focus', async ({ page }) => {
    await mockSignedInSession(page);
    await page.route('**/api/folders**', (route) => route.fulfill({ json: [] }));
    await page.goto('/settings');

    const trigger = page.getByRole('button', { name: 'Search' });
    await trigger.focus();
    await expect(page.getByRole('button', { name: 'Search' })).toBeVisible();
    await page.keyboard.press('Control+K');

    const search = page.getByRole('combobox', { name: 'Search folders, secrets, and actions' });
    await expect(search).toBeFocused();

    await page.keyboard.press('Escape');
    await expect(page.getByRole('dialog', { name: 'Command palette' })).toHaveCount(0);
    await expect(trigger).toBeFocused();
  });

  test('the shortcut works inside a text field without changing its text', async ({ page }) => {
    await mockSignedInSession(page);
    await page.route('**/api/folders**', (route) => route.fulfill({ json: [] }));
    await page.goto('/settings');

    await expect(page.getByRole('button', { name: 'Search' })).toBeVisible();
    await page.evaluate(() => {
      const input = document.createElement('input');
      input.id = 'probe';
      document.querySelector('main')?.prepend(input);
      input.focus();
      input.value = 'draft text';
    });
    await expect(page.getByRole('button', { name: 'Search' })).toBeVisible();
    await page.keyboard.press('Control+K');

    await expect(page.getByRole('combobox', { name: 'Search folders, secrets, and actions' })).toBeFocused();
    await page.keyboard.press('Escape');
    await expect(page.locator('#probe')).toHaveValue('draft text');
  });

  test('search results are grouped and Enter opens the highlighted secret folder', async ({ page }) => {
    await mockSignedInSession(page);
    await page.route('**/api/folders**', (route) => route.fulfill({ json: [] }));
    await page.route('**/api/search**', (route) =>
      route.fulfill({
        json: {
          folders: [{ path: '/production' }],
          secrets: [{ id: '3f1d8c2e-0000-4000-8000-000000000001', name: 'prod-db', type: 'UsernamePassword', folderPath: '/production' }],
          truncated: false,
        },
      }),
    );
    await page.goto('/settings');

    await expect(page.getByRole('button', { name: 'Search' })).toBeVisible();
    await page.keyboard.press('Control+K');
    const search = page.getByRole('combobox', { name: 'Search folders, secrets, and actions' });
    await search.fill('prod');

    await expect(page.getByRole('heading', { name: 'Folders' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Secrets' })).toBeVisible();
    await page.keyboard.press('ArrowDown');
    await page.keyboard.press('Enter');
    await expect(page).toHaveURL(/\/folders\/production$/);
  });

  test('quick actions show Settings and Toggle theme but not New secret without Create', async ({ page }) => {
    await mockSignedInSession(page);
    await page.route('**/api/folders**', (route) => route.fulfill({ json: [] }));
    await page.goto('/folders');

    await expect(page.getByRole('button', { name: 'Search' })).toBeVisible();
    await page.keyboard.press('Control+K');
    await expect(page.getByRole('option', { name: 'Settings' })).toBeVisible();
    await expect(page.getByRole('option', { name: 'Toggle theme' })).toBeVisible();
    await expect(page.getByRole('option', { name: 'New secret' })).toHaveCount(0);
    await expect(page.getByRole('option', { name: 'Audit log' })).toHaveCount(0);
  });

  test('New secret appears when the caller can create in a folder', async ({ page }) => {
    await mockSignedInSession(page);
    await page.route('**/api/folders**', (route) =>
      route.fulfill({ json: [{ path: '/production', inheritsPermissions: true }] }),
    );
    await page.route('**/api/access/me/permissions**', (route) =>
      route.fulfill({ json: { folderPath: '/production', permissions: ['read', 'create'] } }),
    );
    await page.goto('/folders');

    await expect(page.getByRole('button', { name: 'Search' })).toBeVisible();
    await page.keyboard.press('Control+K');
    await expect(page.getByRole('option', { name: 'New secret' })).toBeVisible();
  });
});
