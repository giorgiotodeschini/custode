// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';
import { mockSignedInSession } from './support/session';

const wcagTags = ['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa'];
const viewports = [
  { name: 'wide', width: 1280, height: 800 },
  { name: 'narrow', width: 390, height: 800 },
] as const;

for (const scheme of ['light', 'dark'] as const) {
  for (const viewport of viewports) {
    test(`meets WCAG 2.2 AA on ${viewport.name} viewport in ${scheme} theme`, async ({ page }) => {
      await page.emulateMedia({ colorScheme: scheme });
      await page.setViewportSize({ width: viewport.width, height: viewport.height });
      await mockSignedInSession(page);
      await page.goto('/');

      const results = await new AxeBuilder({ page }).withTags(wcagTags).analyze();

      expect(results.violations).toEqual([]);
    });
  }

  test(`meets WCAG 2.2 AA on the sign-in form in ${scheme} theme`, async ({ page }) => {
    await page.emulateMedia({ colorScheme: scheme });
    await page.route('**/api/auth/refresh', (route) => route.fulfill({ status: 401 }));
    await page.goto('/sign-in');

    const results = await new AxeBuilder({ page }).withTags(wcagTags).analyze();

    expect(results.violations).toEqual([]);
  });
}

test('disables sidebar transitions when reduced motion is requested', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.setViewportSize({ width: 390, height: 800 });
  await mockSignedInSession(page);
  await page.goto('/');

  const duration = await page
    .locator('#app-sidebar')
    .evaluate((element) => getComputedStyle(element).transitionDuration);
  expect(parseFloat(duration)).toBeLessThanOrEqual(0.001);
});
