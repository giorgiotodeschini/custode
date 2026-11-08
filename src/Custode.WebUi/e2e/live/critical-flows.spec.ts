// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { expect, test, type Page } from '@playwright/test';

/**
 * Critical flows against a running Custode server (task 10.2). They need the dedicated end-to-end
 * tenant created by the bootstrap; the tests are skipped when its credentials are not provided.
 *
 *   CUSTODE_E2E_USER_PASSWORD   password of the users alice (tenant administrator) and bob (member)
 *
 * Every run uses fresh folder and secret names, so the tests can be repeated on the same database.
 */
const password = process.env['CUSTODE_E2E_USER_PASSWORD'];
const runId = Date.now().toString(36);
const folder = `ui-${runId}`;
const secretName = `ci-db-${runId}`;
const secretPassword = `S3cret!-${runId}`;

test.skip(!password, 'CUSTODE_E2E_USER_PASSWORD is not set; the live server tests are skipped.');
test.describe.configure({ mode: 'serial' });

async function signIn(page: Page, login: string): Promise<void> {
  await page.goto('/sign-in');
  await page.getByLabel('Login').fill(login);
  await page.getByLabel('Password', { exact: true }).fill(password ?? '');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.locator('#app-sidebar')).toBeVisible();
}

test.beforeEach(async ({ page, context }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await context.grantPermissions(['clipboard-read', 'clipboard-write']);
});

test('sign-in sets an HTTP-only refresh cookie that script cannot read, and a reload keeps the session', async ({ page, context }) => {
  await signIn(page, 'alice');

  const cookies = await context.cookies();
  const refresh = cookies.find((cookie) => cookie.name === 'custode_refresh');
  expect(refresh?.httpOnly).toBe(true);
  expect(refresh?.secure).toBe(true);
  expect(refresh?.sameSite).toBe('Strict');
  expect(refresh?.path).toBe('/api/auth');
  expect(await page.evaluate(() => document.cookie)).not.toContain('custode_refresh');

  await page.reload();
  await expect(page.locator('#app-sidebar')).toBeVisible();
  await expect(page).not.toHaveURL(/\/sign-in/);
});

test('a folder is created, a secret is saved in it, and its password is copied to the clipboard', async ({ page }) => {
  await signIn(page, 'alice');

  await page.getByRole('link', { name: 'Folders', exact: true }).click();
  await page.getByRole('button', { name: 'New folder' }).click();
  await page.getByLabel('Folder name').fill(folder);
  await page.getByRole('button', { name: 'Create folder' }).click();
  await expect(page).toHaveURL(new RegExp(`/folders/${folder}$`));

  await page.goto(`/secrets/new?folderPath=/${folder}`);
  await page.getByLabel('Name', { exact: true }).fill(secretName);
  await page.getByLabel('Username', { exact: true }).fill('deploy');
  await page.getByLabel('Password', { exact: true }).fill(secretPassword);
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`/folders/${folder}$`));
  await expect(page.locator('table')).toContainText(secretName);

  await page.getByRole('button', { name: `Details for ${secretName}` }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Copy password' }).click();
  await expect(page.getByRole('dialog').getByRole('button', { name: 'Copied' })).toBeVisible();
  expect(await page.evaluate(() => navigator.clipboard.readText())).toBe(secretPassword);
});

test('a changed secret is rolled back to an earlier version with a new version created', async ({ page }) => {
  await signIn(page, 'alice');
  await page.goto(`/folders/${folder}`);
  await page.getByRole('button', { name: `Details for ${secretName}` }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Version history' }).click();
  await expect(page).toHaveURL(/\/secrets\/[^/]+\/edit$/);

  await page.getByLabel('Username', { exact: true }).fill('deploy-2');
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`/folders/${folder}$`));

  await page.getByRole('button', { name: `Details for ${secretName}` }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Version history' }).click();
  await page.getByRole('button', { name: 'Roll back to version 1' }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Roll back' }).click();

  await expect(page.getByRole('status')).toContainText('Rolled back to version 1.');
  await expect(page.getByLabel('Username', { exact: true })).toHaveValue('deploy');
  await expect(page.getByRole('checkbox', { name: 'Select version 3 to compare' })).toBeVisible();
});

test('a grant is set for a member of the tenant and the folder permissions show it as direct', async ({ page }) => {
  await signIn(page, 'alice');
  await page.goto(`/administration/access?folderPath=/${folder}`);

  await page.getByLabel('User or group of this tenant').selectOption({ label: 'User: bob' });
  await page.getByLabel('Editor (Read, Write, Create)').check();
  await page.getByRole('button', { name: 'Save access' }).click();

  await expect(page.getByRole('row', { name: /bob/ }).getByText('Direct').first()).toBeVisible();
});

test('the audit log lists the entries and its hash chain verifies as intact', async ({ page }) => {
  await signIn(page, 'alice');
  await page.goto('/audit?outcome=succeeded');

  await expect(page.locator('tbody tr').first()).toBeVisible();
  await page.getByRole('button', { name: 'Verify integrity' }).click();
  await expect(page.getByText('Intact')).toBeVisible();
});

test('signing out revokes the refresh token on the server', async ({ page, context }) => {
  await signIn(page, 'alice');
  const before = (await context.cookies()).find((cookie) => cookie.name === 'custode_refresh');
  expect(before).toBeDefined();

  await page.getByRole('button', { name: 'Sign out' }).click();
  await expect(page).toHaveURL(/\/sign-in$/);

  const replay = await context.request.post('/api/auth/refresh', {
    headers: { 'X-Custode-Request': 'true' },
    failOnStatusCode: false,
  });
  expect(replay.status()).toBe(401);
});

test('an editing session of several minutes survives the idle timeout while the user keeps interacting', async ({ page }) => {
  test.setTimeout(14 * 60_000);
  await signIn(page, 'alice');

  await page.goto('/administration/policies');
  const idle = page.getByLabel('Idle timeout');
  if ((await idle.inputValue()) !== '15') {
    await idle.fill('15');
    await page.getByRole('button', { name: 'Save policy' }).click();
    await expect(page.getByRole('status')).toContainText('Security policy saved.');
  }
  await idle.fill('5');
  await page.getByRole('button', { name: 'Save policy' }).click();
  await expect(page.getByRole('status')).toContainText('Security policy saved.');

  try {
    await page.goto(`/folders/${folder}`);
    await page.getByRole('button', { name: `Details for ${secretName}` }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Version history' }).click();
    await expect(page).toHaveURL(/\/edit$/);

    const username = page.getByLabel('Username', { exact: true });
    const startedAt = Date.now();
    let counter = 0;
    while (Date.now() - startedAt < 6.5 * 60_000) {
      counter += 1;
      await username.press('End');
      await username.type(`-${counter}`, { delay: 50 });
      await page.waitForTimeout(30_000);
    }

    await page.getByRole('button', { name: 'Save', exact: true }).click();
    await expect(page).toHaveURL(new RegExp(`/folders/${folder}$`));
    await expect(page.getByRole('status')).toContainText('Saved');
  } finally {
    await page.goto('/administration/policies');
    await page.getByLabel('Idle timeout').fill('15');
    await page.getByRole('button', { name: 'Save policy' }).click();
    await expect(page.getByRole('status')).toContainText('Security policy saved.');
  }
});
