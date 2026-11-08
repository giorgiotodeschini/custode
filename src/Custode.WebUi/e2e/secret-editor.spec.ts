// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { expect, test } from '@playwright/test';
import { PASSWORD, PROD_DB, mockVault } from './support/vault';
import { mockSignedInSession } from './support/session';

test.beforeEach(async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await mockSignedInSession(page);
});

test.describe('creating a secret', () => {
  test('saves a username/password secret in the chosen folder', async ({ page }) => {
    const vault = await mockVault(page);
    await page.goto('/secrets/new?folderPath=/production');
    await expect(page.getByLabel('Folder')).toHaveValue('/production');

    await page.getByLabel('Name', { exact: true }).fill('ci-db');
    await page.getByLabel('Username').fill('deploy');
    await page.getByLabel('Password', { exact: true }).fill('s3cret-value');
    await page.getByRole('button', { name: 'Save', exact: true }).click();

    await expect.poll(() => vault.creates.length).toBe(1);
    expect(vault.creates[0]).toMatchObject({
      name: 'ci-db',
      type: 'username-password',
      folderPath: '/production',
      username: 'deploy',
      password: 's3cret-value',
    });
    await expect(page).toHaveURL(/\/folders\/production$/);
    await expect(page.getByRole('status')).toContainText('Saved ci-db.');
  });

  test('a missing password is reported inline and nothing is sent', async ({ page }) => {
    const vault = await mockVault(page);
    await page.goto('/secrets/new?folderPath=/production');

    await page.getByLabel('Name', { exact: true }).fill('ci-db');
    await page.getByLabel('Username').fill('deploy');
    await page.getByRole('button', { name: 'Save', exact: true }).click();

    await expect(page.getByText('Enter a password.')).toBeVisible();
    expect(vault.creates).toHaveLength(0);
  });

  test('an API key needs one value', async ({ page }) => {
    const vault = await mockVault(page);
    await page.goto('/secrets/new?folderPath=/production');

    await page.getByLabel('Type').selectOption('api-key');
    await page.getByLabel('Name', { exact: true }).fill('ci-key');
    await page.getByRole('button', { name: 'Save', exact: true }).click();
    await expect(page.getByText('Enter a value.')).toBeVisible();
    expect(vault.creates).toHaveLength(0);

    await page.getByLabel('API key').fill('k-123');
    await page.getByRole('button', { name: 'Save', exact: true }).click();
    await expect.poll(() => vault.creates.length).toBe(1);
    expect(vault.creates[0]).toMatchObject({ type: 'api-key', value: 'k-123' });
  });

  test('a structured secret rejects a duplicate field name', async ({ page }) => {
    const vault = await mockVault(page);
    await page.goto('/secrets/new?folderPath=/production');

    await page.getByLabel('Type').selectOption('structured');
    await page.getByLabel('Name', { exact: true }).fill('smtp');
    await page.getByRole('button', { name: 'Add field' }).click();
    await page.getByRole('button', { name: 'Add field' }).click();
    await page.locator('#field-name-0').fill('host');
    await page.locator('#field-name-1').fill('host');
    await page.locator('#field-value-0').fill('mail');
    await page.locator('#field-value-1').fill('other');
    await page.getByRole('button', { name: 'Save', exact: true }).click();

    await expect(page.getByText('This field name already exists.').first()).toBeVisible();
    expect(vault.creates).toHaveLength(0);
  });

  test('a key-value secret keeps the order of its pairs', async ({ page }) => {
    const vault = await mockVault(page);
    await page.goto('/secrets/new?folderPath=/production');

    await page.getByLabel('Type').selectOption('key-value');
    await page.getByLabel('Name', { exact: true }).fill('env');
    await page.getByRole('button', { name: 'Add pair' }).click();
    await page.locator('#pair-key-0').fill('B');
    await page.locator('#pair-value-0').fill('2');
    await page.locator('#pair-key-1').fill('A');
    await page.locator('#pair-value-1').fill('1');
    await page.getByRole('button', { name: 'Save', exact: true }).click();

    await expect.poll(() => vault.creates.length).toBe(1);
    expect(vault.creates[0]).toMatchObject({
      type: 'key-value',
      pairs: [
        { key: 'B', value: '2' },
        { key: 'A', value: '1' },
      ],
    });
  });

  test('a user who may create nowhere cannot save', async ({ page }) => {
    await mockVault(page, { permissions: ['read'] });
    await page.goto('/secrets/new');

    await expect(page.getByText('You cannot create a secret in any folder.')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Save', exact: true })).toBeDisabled();
  });

  test('a refused create shows that Create permission is required', async ({ page }) => {
    const vault = await mockVault(page, { createStatus: 403 });
    await page.goto('/secrets/new?folderPath=/production');

    await page.getByLabel('Name', { exact: true }).fill('ci-db');
    await page.getByLabel('Username').fill('deploy');
    await page.getByLabel('Password', { exact: true }).fill('x');
    await page.getByRole('button', { name: 'Save', exact: true }).click();

    await expect(page.getByRole('alert')).toHaveText('Create permission is required on this folder. Nothing was saved.');
    expect(vault.creates).toHaveLength(1);
  });

  test('a certificate with an unsupported extension is rejected before upload', async ({ page }) => {
    await mockVault(page);
    await page.goto('/secrets/new?folderPath=/production');

    await page.getByLabel('Type').selectOption('file');
    await page.setInputFiles('#secret-file', { name: 'notes.txt', mimeType: 'text/plain', buffer: Buffer.from('x') });

    await expect(page.getByRole('alert')).toContainText('Unsupported file type');
  });

  test('two files are refused and a single certificate is accepted', async ({ page }) => {
    await mockVault(page);
    await page.goto('/secrets/new?folderPath=/production');
    await page.getByLabel('Type').selectOption('file');

    await page.setInputFiles('#secret-file', [
      { name: 'a.pem', mimeType: 'application/x-pem-file', buffer: Buffer.from('a') },
      { name: 'b.pem', mimeType: 'application/x-pem-file', buffer: Buffer.from('b') },
    ]);
    await expect(page.getByRole('alert')).toHaveText('Select exactly one certificate file.');

    await page.setInputFiles('#secret-file', { name: 'server.pfx', mimeType: 'application/x-pkcs12', buffer: Buffer.from('pfx') });
    await expect(page.getByText('Selected: server.pfx, 3 bytes')).toBeVisible();
  });
});

test.describe('leaving with unsaved changes', () => {
  test('cancelling the confirmation keeps the form; confirming leaves it', async ({ page }) => {
    await mockVault(page);
    await page.goto('/secrets/new?folderPath=/production');
    await page.getByLabel('Name', { exact: true }).fill('draft');

    await page.getByRole('button', { name: 'Cancel', exact: true }).click();
    await page.getByRole('alertdialog').getByRole('button', { name: 'Cancel' }).click();
    await expect(page.getByLabel('Name', { exact: true })).toHaveValue('draft');

    await page.getByRole('button', { name: 'Cancel', exact: true }).click();
    await page.getByRole('alertdialog').getByRole('button', { name: 'Discard changes' }).click();
    await expect(page).toHaveURL(/\/folders\/production$/);
  });
});

test.describe('editing a secret', () => {
  test('loads the stored fields, shows the type read-only and saves with the version token', async ({ page }) => {
    const vault = await mockVault(page, { currentVersion: 2 });
    await page.goto(`/secrets/${PROD_DB}/edit`);

    await expect(page.getByLabel('Username')).toHaveValue('deploy');
    await expect(page.getByLabel('Type')).toHaveCount(0);
    await expect(page.getByText('Username and password')).toBeVisible();

    await page.getByLabel('Username').fill('deploy-2');
    await page.getByRole('button', { name: 'Save', exact: true }).click();

    await expect.poll(() => vault.updates.length).toBe(1);
    expect(vault.updates[0]?.ifMatch).toBe('"2"');
    expect(vault.updates[0]?.body).toMatchObject({ username: 'deploy-2', type: 'username-password' });
  });

  test('a stale version token shows the conflict and the two ways forward', async ({ page }) => {
    await mockVault(page, { saveStatus: 412 });
    await page.goto(`/secrets/${PROD_DB}/edit`);
    await page.getByLabel('Username').fill('deploy-2');
    await page.getByRole('button', { name: 'Save', exact: true }).click();

    await expect(page.getByRole('alert').first()).toContainText('This secret changed after you opened it.');
    await page.getByRole('button', { name: 'Keep my edits' }).click();
    await expect(page.getByRole('status')).toContainText('Your edits are kept.');
    await expect(page.getByLabel('Username')).toHaveValue('deploy-2');
  });

  test('a read-only user sees the form without a save control', async ({ page }) => {
    await mockVault(page, { permissions: ['read'] });
    await page.goto(`/secrets/${PROD_DB}/edit`);

    await expect(page.getByLabel('Username')).toBeDisabled();
    await expect(page.getByRole('button', { name: 'Save', exact: true })).toHaveCount(0);
    await expect(page.getByText('You can read this secret but not change it.')).toBeVisible();
  });

  test('an unreadable secret shows the not-found page', async ({ page }) => {
    await mockVault(page, { valueStatus: 403 });
    await page.goto(`/secrets/${PROD_DB}/edit`);

    await expect(page.getByRole('heading', { name: 'Page not found' })).toBeVisible();
  });

  test('the password stays masked until the reveal control is used', async ({ page }) => {
    await mockVault(page);
    await page.goto(`/secrets/${PROD_DB}/edit`);

    await expect(page.getByLabel('Password', { exact: true })).toHaveAttribute('type', 'password');
    await page.getByRole('button', { name: 'Show' }).first().click();
    await expect(page.getByLabel('Password', { exact: true })).toHaveValue(PASSWORD);
  });
});

test.describe('version history', () => {
  test('compares two versions with the values hidden until revealed', async ({ page }) => {
    await mockVault(page);
    await page.goto(`/secrets/${PROD_DB}/edit`);

    await page.getByRole('checkbox', { name: 'Select version 1 to compare' }).check();
    await page.getByRole('checkbox', { name: 'Select version 2 to compare' }).check();
    await page.getByRole('button', { name: 'Compare selected' }).click();

    const diff = page.getByRole('region', { name: 'Comparison' });
    await expect(diff.getByRole('row', { name: /password changed/ })).toBeVisible();
    await expect(diff).not.toContainText('old-password-1');

    await page.getByRole('button', { name: 'Reveal values' }).click();
    await expect(diff).toContainText('old-password-1');
  });

  test('rolls back after confirmation, sending the loaded version token', async ({ page }) => {
    const vault = await mockVault(page, { currentVersion: 2 });
    await page.goto(`/secrets/${PROD_DB}/edit`);

    await page.getByRole('button', { name: 'Roll back to version 1' }).click();
    await page.getByRole('alertdialog').getByRole('button', { name: 'Roll back' }).click();

    await expect.poll(() => vault.rollbacks.length).toBe(1);
    expect(vault.rollbacks[0]).toMatchObject({ number: 1, ifMatch: '"2"' });
  });

  test('a read-only user sees no rollback action', async ({ page }) => {
    await mockVault(page, { permissions: ['read'] });
    await page.goto(`/secrets/${PROD_DB}/edit`);

    await expect(page.getByRole('row', { name: /1/ }).first()).toBeVisible();
    await expect(page.getByRole('button', { name: /Roll back/ })).toHaveCount(0);
  });
});

test('the detail panel opens the version history of a secret', async ({ page }) => {
  await mockVault(page);
  await page.goto('/folders/production');
  await page.getByRole('button', { name: 'Details for prod-db' }).click();

  await page.getByRole('dialog').getByRole('button', { name: 'Version history' }).click();

  await expect(page).toHaveURL(new RegExp(`/secrets/${PROD_DB}/edit$`));
  await expect(page.getByRole('heading', { name: 'Version history' })).toBeVisible();
});
