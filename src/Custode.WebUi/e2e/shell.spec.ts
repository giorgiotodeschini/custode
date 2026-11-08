// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { expect, test, type Page } from '@playwright/test';
import { mockSignedInSession } from './support/session';

async function openShell(page: Page): Promise<void> {
  await mockSignedInSession(page);
  await page.goto('/');
}

test.describe('responsive shell', () => {
  test('does not scroll horizontally at 360 pixels', async ({ page }) => {
    await page.setViewportSize({ width: 360, height: 740 });
    await openShell(page);

    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    );
    expect(overflow).toBeLessThanOrEqual(0);
  });

  test('keeps the sidebar beside the content on a wide viewport', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await openShell(page);

    await expect(page.locator('#app-sidebar')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Menu', exact: true })).toHaveCount(0);
  });

  test('opens the sidebar as a drawer and returns focus to the menu on Escape', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 800 });
    await openShell(page);

    const menu = page.getByRole('button', { name: 'Menu', exact: true });
    await expect(menu).toHaveAttribute('aria-expanded', 'false');
    await menu.click();

    await expect(menu).toHaveAttribute('aria-expanded', 'true');
    await expect(page.getByRole('button', { name: 'Close menu' })).toBeFocused();

    await page.keyboard.press('Escape');
    await expect(menu).toHaveAttribute('aria-expanded', 'false');
    await expect(menu).toBeFocused();
  });

  test('signs out to the sign-in form', async ({ page }) => {
    await openShell(page);
    await page.route('**/api/auth/sign-out', (route) => route.fulfill({ status: 204 }));

    await page.getByRole('button', { name: 'Sign out' }).click();

    await expect(page).toHaveURL(/\/sign-in$/);
    await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible();
  });
});

test.describe('theme preference', () => {
  test('follows the operating color scheme when no choice is stored', async ({ page }) => {
    await page.emulateMedia({ colorScheme: 'dark' });
    await openShell(page);

    await expect(page.locator('html')).toHaveClass(/\bdark\b/);
  });

  test('keeps an explicit light choice across reloads', async ({ page }) => {
    await page.emulateMedia({ colorScheme: 'dark' });
    await openShell(page);
    await page.evaluate(() => localStorage.setItem('custode.theme', 'light'));

    await page.reload();

    await expect(page.locator('html')).not.toHaveClass(/\bdark\b/);
  });
});
