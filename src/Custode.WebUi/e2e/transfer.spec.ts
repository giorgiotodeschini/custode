// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { readFileSync } from 'node:fs';
import { expect, test } from '@playwright/test';
import { PASSWORD, mockVault } from './support/vault';
import { mockSignedInSession } from './support/session';

test.beforeEach(async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
});

const jsonImport = JSON.stringify([
  { folderPath: '/production', name: 'ci-a', type: 'pat', value: 'token-a' },
  { folderPath: '/production', name: 'ci-b', type: 'api-key', value: 'key-b' },
]);

test.describe('export', () => {
  test('a JSON export without values contains metadata and no secret value', async ({ page }) => {
    await mockSignedInSession(page);
    await mockVault(page);
    await page.goto('/transfer?folderPath=/production');

    const downloadPromise = page.waitForEvent('download');
    await page.getByRole('button', { name: 'Export', exact: true }).click();
    const download = await downloadPromise;
    const content = readFileSync((await download.path()) ?? '', 'utf8');

    const file = JSON.parse(content) as { includesValues: boolean; secrets: Array<{ name: string }> };
    expect(file.includesValues).toBe(false);
    expect(file.secrets.map((secret) => secret.name).sort()).toEqual(['prod-api-token', 'prod-db']);
    expect(content).not.toContain(PASSWORD);
  });

  test('values are included only after the plaintext confirmation', async ({ page }) => {
    await mockSignedInSession(page);
    await mockVault(page);
    await page.goto('/transfer?folderPath=/production');

    await page.getByLabel('Include secret values in plain text. You will be asked to confirm.').check();
    await page.getByRole('button', { name: 'Export', exact: true }).click();
    await expect(page.getByRole('alertdialog')).toContainText('plain text');

    const downloadPromise = page.waitForEvent('download');
    await page.getByRole('alertdialog').getByRole('button', { name: 'Export with values' }).click();
    const content = readFileSync((await (await downloadPromise).path()) ?? '', 'utf8');

    expect(JSON.parse(content).includesValues).toBe(true);
    expect(content).toContain(PASSWORD);
  });

  test('a CSV export without values has no value column', async ({ page }) => {
    await mockSignedInSession(page);
    await mockVault(page);
    await page.goto('/transfer?folderPath=/production');

    await page.getByLabel('CSV').check();
    const downloadPromise = page.waitForEvent('download');
    await page.getByRole('button', { name: 'Export', exact: true }).click();
    const content = readFileSync((await (await downloadPromise).path()) ?? '', 'utf8');

    const header = content.split('\r\n')[0] ?? '';
    expect(header).toContain('folderPath,name,type');
    expect(header).not.toContain('password');
    expect(content).not.toContain(PASSWORD);
  });
});

test.describe('import', () => {
  test('a valid JSON file is previewed and imported as one batch', async ({ page }) => {
    await mockSignedInSession(page);
    const vault = await mockVault(page);
    await page.goto('/transfer');

    await page.setInputFiles('#import-file', { name: 'secrets.json', mimeType: 'application/json', buffer: Buffer.from(jsonImport) });
    await expect(page.getByText('2 to create, 0 skipped, 0 conflicts, 0 failing.')).toBeVisible();

    await page.getByRole('button', { name: 'Import 2 secrets' }).click();

    await expect.poll(() => vault.imports.filter((request) => !request.dryRun).length).toBe(1);
    await expect(page).toHaveURL(/\/folders\/production$/);
    await expect(page.getByRole('status')).toContainText('Imported 2 secrets.');
  });

  test('a name conflict blocks the import until the row is skipped', async ({ page }) => {
    await mockSignedInSession(page);
    const vault = await mockVault(page, { importStatuses: { 1: 'conflict' } });
    await page.goto('/transfer');
    await page.setInputFiles('#import-file', { name: 'secrets.json', mimeType: 'application/json', buffer: Buffer.from(jsonImport) });

    await expect(page.getByText('1 conflicts')).toBeVisible();
    await expect(page.getByRole('button', { name: /Import/ })).toBeDisabled();

    await page.getByRole('checkbox', { name: /Skip ci-b/ }).check();
    await page.getByRole('button', { name: 'Check again' }).click();

    await expect(page.getByRole('button', { name: 'Import 1 secret' })).toBeEnabled();
    await page.getByRole('button', { name: 'Import 1 secret' }).click();
    await expect.poll(() => vault.imports.filter((request) => !request.dryRun).length).toBe(1);
    const commit = vault.imports.find((request) => !request.dryRun);
    expect(commit?.items.map((item) => item['name'])).toEqual(['ci-a']);
  });

  test('a row with an unsupported type is shown with its reason and cannot be imported', async ({ page }) => {
    await mockSignedInSession(page);
    await mockVault(page);
    await page.goto('/transfer');

    const csv = 'folderPath,name,type,value\n/production,ci-a,pat,token-a\n/production,ci-b,pgp-key,x\n';
    await page.setInputFiles('#import-file', { name: 'secrets.csv', mimeType: 'text/csv', buffer: Buffer.from(csv) });

    await expect(page.getByText('Unsupported secret type "pgp-key".')).toBeVisible();
    await expect(page.getByRole('button', { name: /Import/ })).toBeDisabled();
  });
});

test.describe('encrypted backup', () => {
  test('a regular user opening the backup is sent to folders', async ({ page }) => {
    await mockSignedInSession(page);
    await mockVault(page);
    await page.goto('/administration/backup');

    await expect(page).toHaveURL(/\/folders$/);
    await expect(page.getByRole('status')).toContainText('This page is not available.');
  });

  test('the warning about the passphrase is shown before the passphrase fields', async ({ page }) => {
    await mockSignedInSession(page, { admin: true });
    await mockVault(page);
    await page.goto('/administration/backup');

    const warning = page.getByRole('note');
    await expect(warning).toContainText('cannot be recovered');
    const warningBox = await warning.boundingBox();
    const passphraseBox = await page.getByLabel('Passphrase', { exact: true }).boundingBox();
    expect((warningBox?.y ?? 0) < (passphraseBox?.y ?? 0)).toBe(true);
  });

  test('a short passphrase or a mismatch does not start the export', async ({ page }) => {
    await mockSignedInSession(page, { admin: true });
    const vault = await mockVault(page);
    await page.goto('/administration/backup');

    await page.getByLabel('Passphrase', { exact: true }).fill('too-short');
    await page.getByLabel('Enter the passphrase again').fill('too-short');
    await page.getByRole('button', { name: 'Export encrypted backup' }).click();
    await expect(page.getByText('Use at least 12 characters.')).toBeVisible();

    await page.getByLabel('Passphrase', { exact: true }).fill('a-long-passphrase-1');
    await page.getByLabel('Enter the passphrase again').fill('a-long-passphrase-2');
    await page.getByRole('button', { name: 'Export encrypted backup' }).click();
    await expect(page.getByText('The two passphrases do not match.')).toBeVisible();
    expect(vault.backups).toHaveLength(0);
  });

  test('a matching passphrase downloads the encrypted file and clears the fields', async ({ page }) => {
    await mockSignedInSession(page, { admin: true });
    const vault = await mockVault(page);
    await page.goto('/administration/backup');

    await page.getByLabel('Passphrase', { exact: true }).fill('a-long-passphrase-1');
    await page.getByLabel('Enter the passphrase again').fill('a-long-passphrase-1');
    const downloadPromise = page.waitForEvent('download');
    await page.getByRole('button', { name: 'Export encrypted backup' }).click();
    const download = await downloadPromise;

    expect(download.suggestedFilename()).toBe('custode-tenant-20261108.cbak');
    expect(vault.backups).toEqual(['a-long-passphrase-1']);
    await expect(page.getByLabel('Passphrase', { exact: true })).toHaveValue('');
  });
});
