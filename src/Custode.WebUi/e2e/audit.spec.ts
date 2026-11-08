// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { expect, test } from '@playwright/test';
import { mockAudit } from './support/audit';
import { mockSignedInSession } from './support/session';

test.beforeEach(async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
});

const sequences = (page: import('@playwright/test').Page) =>
  page.locator('tbody tr td:first-child').allTextContents();

test('a regular user cannot open the audit log', async ({ page }) => {
  await mockSignedInSession(page);
  await mockAudit(page);
  await page.goto('/audit');

  await expect(page).toHaveURL(/\/folders$/);
  await expect(page.getByRole('status')).toContainText('This page is not available.');
});

test('entries are listed in ascending sequence order, page by page without gaps or repeats', async ({ page }) => {
  await mockSignedInSession(page, { admin: true });
  await mockAudit(page);
  await page.goto('/audit');

  await expect(page.locator('tbody tr')).toHaveCount(3);
  expect(await sequences(page)).toEqual(['1', '2', '3']);

  await page.getByRole('button', { name: 'Load more' }).click();
  await expect(page.locator('tbody tr')).toHaveCount(6);
  await page.getByRole('button', { name: 'Load more' }).click();
  await expect(page.locator('tbody tr')).toHaveCount(7);

  const all = await sequences(page);
  expect(all).toEqual(['1', '2', '3', '4', '5', '6', '7']);
  expect(new Set(all).size).toBe(all.length);
});

test('descending order changes only the display order', async ({ page }) => {
  await mockSignedInSession(page, { admin: true });
  await mockAudit(page);
  await page.goto('/audit');
  await page.getByRole('button', { name: 'Load more' }).click();
  await expect(page.locator('tbody tr')).toHaveCount(6);

  await page.getByLabel('Order').selectOption('desc');

  expect((await sequences(page))[0]).toBe('6');
  expect(page).toHaveURL(/order=desc/);
});

test('filters combine and are kept in the address, so a shared link shows the same view', async ({ page }) => {
  await mockSignedInSession(page, { admin: true });
  await mockAudit(page);
  await page.goto('/audit');

  await page.getByLabel('Outcome').selectOption('denied');
  await expect(page).toHaveURL(/outcome=denied/);
  await expect(page.locator('tbody tr')).toHaveCount(1);
  await expect(page.locator('tbody tr').first()).toContainText('Denied');

  await page.goto(page.url());
  await expect(page.getByLabel('Outcome')).toHaveValue('denied');
  await expect(page.locator('tbody tr')).toHaveCount(1);
});

test('a denied outcome has a text label and an icon, and the data layer is named', async ({ page }) => {
  await mockSignedInSession(page, { admin: true });
  await mockAudit(page);
  await page.goto('/audit?outcome=denied');
  await expect(page.locator('tbody tr').first()).toContainText('✕ Denied');

  await page.goto('/audit?user=00000000-0000-0000-0000-000000000000');
  await expect(page.locator('tbody tr').first()).toContainText('Data layer');
});

test('the integrity is not verified until the check runs, then reports the entries verified', async ({ page }) => {
  await mockSignedInSession(page, { admin: true });
  await mockAudit(page);
  await page.goto('/audit');

  await expect(page.getByText('Not verified')).toBeVisible();
  await page.getByRole('button', { name: 'Verify integrity' }).click();

  await expect(page.getByText('Intact')).toBeVisible();
  await expect(page.getByText('(7 entries verified)')).toBeVisible();
});

test('a broken link is marked on its entry and the integrity turns altered', async ({ page }) => {
  await mockSignedInSession(page, { admin: true });
  await mockAudit(page, { breakLinkAt: 2 });
  await page.goto('/audit');

  await expect(page.locator('tbody tr').nth(1)).toContainText('Link broken at 2');
  await expect(page.getByText('Altered')).toBeVisible();
  await expect(page.getByText('from sequence 2')).toBeVisible();
});
