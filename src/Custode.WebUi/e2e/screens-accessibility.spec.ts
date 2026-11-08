// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { mockAccess } from './support/access';
import { mockAudit } from './support/audit';
import { mockSignedInSession } from './support/session';
import { PROD_DB, mockVault } from './support/vault';
import { mockTenant } from './support/tenant';

const wcagTags = ['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa'];

/** Every screen of the application, as an administrator sees it. */
const screens = [
  '/folders',
  '/folders/production',
  '/secrets/new?folderPath=/production',
  `/secrets/${PROD_DB}/edit`,
  '/settings',
  '/transfer?folderPath=/production',
  '/audit',
  '/administration',
  '/administration/members',
  '/administration/groups',
  '/administration/groups/g-ops',
  '/administration/policies',
  '/administration/access?folderPath=/production',
  '/administration/backup',
  '/sign-in',
  '/no-such-screen',
];

async function mockEverything(page: Page): Promise<void> {
  await mockSignedInSession(page, { admin: true });
  await mockVault(page);
  await mockAccess(page);
  await mockAudit(page);
  await mockTenant(page);
}

for (const scheme of ['light', 'dark'] as const) {
  test(`every screen meets WCAG 2.2 AA in the ${scheme} theme`, async ({ page }) => {
    test.setTimeout(180_000);
    await page.setViewportSize({ width: 1280, height: 900 });
    await page.emulateMedia({ colorScheme: scheme });
    await mockEverything(page);

    const failures: string[] = [];
    for (const screen of screens) {
      await page.goto(screen);
      await page.waitForLoadState('networkidle');
      const results = await new AxeBuilder({ page }).withTags(wcagTags).analyze();
      for (const violation of results.violations) {
        const targets = violation.nodes.slice(0, 3).map((node) => node.target.join(' ')).join(', ');
        failures.push(`${screen}: ${violation.id} (${violation.help}) at ${targets}`);
      }
    }

    expect(failures).toEqual([]);
  });
}
