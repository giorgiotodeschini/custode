// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { expect, test } from '@playwright/test';
import { mockAccess } from './support/access';
import { mockSignedInSession } from './support/session';

test.beforeEach(async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
});

test('a regular user cannot open the folder permissions', async ({ page }) => {
  await mockSignedInSession(page);
  await mockAccess(page);
  await page.goto('/administration/access?folderPath=/production');

  await expect(page).toHaveURL(/\/folders$/);
  await expect(page.getByRole('status')).toContainText('This page is not available.');
});

test('an inherited grant shows the ancestor it comes from', async ({ page }) => {
  await mockSignedInSession(page, { admin: true });
  await mockAccess(page);
  await page.goto('/administration/access?folderPath=/production/aws');

  await expect(page.getByRole('heading', { name: '/production/aws' })).toBeVisible();
  await expect(page.getByText('Inheriting from parent')).toBeVisible();
  const row = page.getByRole('row', { name: /Operators/ });
  await expect(row.getByText('Inherited from /production')).toBeVisible();
});

test('the editor preset saves the grant and the table shows it only after the server confirms', async ({ page }) => {
  await mockSignedInSession(page, { admin: true });
  const access = await mockAccess(page);
  await page.goto('/administration/access?folderPath=/production/aws');

  await page.getByLabel('User or group of this tenant').selectOption({ label: 'Group: Operators' });
  await page.getByLabel('Editor (Read, Write, Create)').check();
  await page.getByRole('button', { name: 'Save access' }).click();

  await expect.poll(() => access.puts.length).toBe(1);
  expect(access.puts[0]).toMatchObject({ folderPath: '/production/aws', principalKind: 'group', permissions: ['read', 'write', 'create'] });
  await expect(page.getByRole('row', { name: /Operators/ }).getByText('Direct').first()).toBeVisible();
});

test('a custom combination sends exactly the chosen levels', async ({ page }) => {
  await mockSignedInSession(page, { admin: true });
  const access = await mockAccess(page);
  await page.goto('/administration/access?folderPath=/staging');

  await page.getByLabel('User or group of this tenant').selectOption({ label: 'User: alice' });
  await page.getByLabel('Custom').check();
  await page.getByRole('checkbox', { name: 'Read' }).uncheck();
  await page.getByRole('checkbox', { name: 'Delete' }).check();
  await page.getByRole('button', { name: 'Save access' }).click();

  await expect.poll(() => access.puts.length).toBe(1);
  expect(access.puts[0]?.permissions).toEqual(['delete']);
});

test('a refused change shows the server reason and leaves the table as it was', async ({ page }) => {
  await mockSignedInSession(page, { admin: true });
  const access = await mockAccess(page, { refuse: { status: 403, detail: 'Only tenant administrators can change access.' } });
  await page.goto('/administration/access?folderPath=/production');

  await page.getByLabel('User or group of this tenant').selectOption({ label: 'User: alice' });
  await page.getByLabel('Viewer (Read)').check();
  await page.getByRole('button', { name: 'Save access' }).click();

  await expect(page.getByRole('alert')).toHaveText('Only tenant administrators can change access.');
  await expect(page.getByRole('row', { name: /alice/ })).toHaveCount(0);
  expect(access.puts).toHaveLength(1);
});

test('revoking a direct grant asks for confirmation first', async ({ page }) => {
  await mockSignedInSession(page, { admin: true });
  const access = await mockAccess(page);
  await page.goto('/administration/access?folderPath=/production');

  await page.getByRole('button', { name: 'Revoke Operators' }).click();
  await expect(page.getByRole('alertdialog')).toContainText('Revoke Operators on /production?');
  await page.getByRole('alertdialog').getByRole('button', { name: 'Cancel' }).click();
  expect(access.deletes).toHaveLength(0);

  await page.getByRole('button', { name: 'Revoke Operators' }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Revoke grant' }).click();
  await expect.poll(() => access.deletes.length).toBe(1);
});

test('breaking inheritance lists what would be lost, and cancelling changes nothing', async ({ page }) => {
  await mockSignedInSession(page, { admin: true });
  const access = await mockAccess(page);
  await page.goto('/administration/access?folderPath=/production/aws');

  await page.getByRole('button', { name: 'Break inheritance' }).click();
  await expect(page.getByRole('alertdialog').getByRole('listitem')).toContainText('Operators: Read (from an ancestor)');
  await page.getByRole('alertdialog').getByRole('button', { name: 'Cancel' }).click();
  expect(access.breaks).toHaveLength(0);
  await expect(page.getByText('Inheriting from parent')).toBeVisible();

  await page.getByRole('button', { name: 'Break inheritance' }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Break inheritance' }).click();
  await expect(page.getByText('Inheritance broken', { exact: true })).toBeVisible();
  expect(access.breaks).toEqual(['/production/aws']);
});

test('restoring inheritance lists the grants that return', async ({ page }) => {
  await mockSignedInSession(page, { admin: true });
  const access = await mockAccess(page);
  await page.goto('/administration/access?folderPath=/production/aws');

  await page.getByRole('button', { name: 'Break inheritance' }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Break inheritance' }).click();
  await expect(page.getByText('Inheritance broken', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Restore inheritance' }).click();
  await expect(page.getByRole('alertdialog').getByRole('listitem')).toContainText('Operators: Read (from /production)');
  await page.getByRole('alertdialog').getByRole('button', { name: 'Restore inheritance' }).click();

  await expect.poll(() => access.restores.length).toBe(1);
});

test('the tree marks folders whose inheritance is broken', async ({ page }) => {
  await mockSignedInSession(page, { admin: true });
  await mockAccess(page);
  await page.goto('/administration/access?folderPath=/production');

  await expect(page.getByRole('treeitem', { name: 'staging' }).getByText('Broken')).toBeVisible();
});
