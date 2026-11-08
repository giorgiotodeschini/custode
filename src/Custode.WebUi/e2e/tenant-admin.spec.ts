// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { expect, test } from '@playwright/test';
import { mockSignedInSession } from './support/session';
import { mockTenant } from './support/tenant';

test.beforeEach(async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
});

test('a regular user cannot open the members page', async ({ page }) => {
  await mockSignedInSession(page);
  await mockTenant(page);
  await page.goto('/administration/members');

  await expect(page).toHaveURL(/\/folders$/);
  await expect(page.getByRole('status')).toContainText('This page is not available.');
});

test.describe('members', () => {
  test('lists the members with roles and status', async ({ page }) => {
    await mockSignedInSession(page, { admin: true });
    await mockTenant(page);
    await page.goto('/administration/members');

    const alice = page.getByRole('row', { name: /alice/ });
    await expect(alice).toContainText('Tenant administrator');
    await expect(page.getByRole('row', { name: /bob/ })).toContainText('Member');
    await expect(page.getByRole('row', { name: /bob/ })).toContainText('Active');
  });

  test('creates a member with an initial password that must be changed', async ({ page }) => {
    await mockSignedInSession(page, { admin: true });
    const tenant = await mockTenant(page);
    await page.goto('/administration/members');

    await page.getByLabel('Login').fill('carol');
    await page.getByLabel('Initial password').fill('start-here-123');
    await page.getByRole('button', { name: 'Create member' }).click();

    await expect(page.getByRole('row', { name: /carol/ })).toContainText('Must change password at first sign-in');
    await expect(page.getByRole('status')).toContainText('must be changed at first sign-in');
    expect(tenant.created).toEqual([{ login: 'carol', initialPassword: 'start-here-123' }]);
  });

  test('a duplicate login shows the server reason and clears the password field', async ({ page }) => {
    await mockSignedInSession(page, { admin: true });
    await mockTenant(page);
    await page.goto('/administration/members');

    await page.getByLabel('Login').fill('bob');
    await page.getByLabel('Initial password').fill('another-password');
    await page.getByRole('button', { name: 'Create member' }).click();

    await expect(page.getByRole('alert')).toHaveText('A member with this login already exists.');
    await expect(page.getByLabel('Initial password')).toHaveValue('');
  });

  test('disabling a member asks first and shows the new status from the server', async ({ page }) => {
    await mockSignedInSession(page, { admin: true });
    const tenant = await mockTenant(page);
    await page.goto('/administration/members');

    await page.getByRole('button', { name: 'Disable bob' }).click();
    await page.getByRole('alertdialog').getByRole('button', { name: 'Cancel' }).click();
    expect(tenant.enabledChanges).toHaveLength(0);

    await page.getByRole('button', { name: 'Disable bob' }).click();
    await page.getByRole('alertdialog').getByRole('button', { name: 'Disable member' }).click();
    await expect(page.getByRole('row', { name: /bob/ })).toContainText('Disabled');
    expect(tenant.enabledChanges).toEqual([{ id: 'u-bob', enabled: false }]);
  });

  test('the last enabled administrator cannot lose the role', async ({ page }) => {
    await mockSignedInSession(page, { admin: true });
    await mockTenant(page);
    await page.goto('/administration/members');

    await expect(page.getByRole('button', { name: 'Withdraw administrator role from alice' })).toBeDisabled();
    await expect(page.getByText('At least one administrator is required.')).toBeVisible();
  });

  test('granting the administrator role shows the new role after the server confirms', async ({ page }) => {
    await mockSignedInSession(page, { admin: true });
    const tenant = await mockTenant(page);
    await page.goto('/administration/members');

    await page.getByRole('button', { name: 'Grant administrator role to bob' }).click();

    await expect(page.getByRole('row', { name: /bob/ })).toContainText('Tenant administrator');
    expect(tenant.administratorChanges).toEqual([{ id: 'u-bob', isAdministrator: true }]);
  });
});

test.describe('groups', () => {
  test('lists the groups with their member and grant counts', async ({ page }) => {
    await mockSignedInSession(page, { admin: true });
    await mockTenant(page);
    await page.goto('/administration/groups');

    await expect(page.getByRole('row', { name: /Operators/ })).toContainText('2');
    await expect(page.getByRole('row', { name: /Operators/ })).toContainText('3');
  });

  test('a duplicate name is refused before any request, ignoring case', async ({ page }) => {
    await mockSignedInSession(page, { admin: true });
    const tenant = await mockTenant(page);
    await page.goto('/administration/groups');

    await page.getByLabel('Group name').fill('operators');
    await page.getByRole('button', { name: 'Create group' }).click();

    await expect(page.getByRole('alert')).toHaveText('A group with this name already exists in the tenant.');
    expect(tenant.groupsCreated).toHaveLength(0);
  });

  test('deleting a group states how many members and grants will be removed', async ({ page }) => {
    await mockSignedInSession(page, { admin: true });
    const tenant = await mockTenant(page);
    await page.goto('/administration/groups');

    await page.getByRole('button', { name: 'Delete Operators' }).click();
    await expect(page.getByRole('alertdialog')).toContainText('2 members will be removed from the group and 3 folder grants will be deleted.');
    await page.getByRole('alertdialog').getByRole('button', { name: 'Delete group' }).click();

    await expect.poll(() => tenant.groupsDeleted).toEqual(['g-ops']);
  });

  test('adding and removing a member states that the change applies on the next request', async ({ page }) => {
    await mockSignedInSession(page, { admin: true });
    const tenant = await mockTenant(page);
    await page.goto('/administration/groups/g-dev');

    await page.getByLabel('User of this tenant').selectOption({ label: 'bob' });
    await page.getByRole('button', { name: 'Add to group' }).click();
    await expect(page.getByRole('status')).toContainText('The change applies on their next request.');
    expect(tenant.groupMemberAdds).toEqual([{ groupId: 'g-dev', userId: 'u-bob' }]);

    await page.getByRole('button', { name: 'Remove bob' }).click();
    await page.getByRole('alertdialog').getByRole('button', { name: 'Remove member' }).click();
    await expect(page.getByRole('status')).toContainText("They lose the group's access on their next request.");
  });
});

test.describe('security policies', () => {
  test('an out-of-range value is rejected in the form and nothing is sent', async ({ page }) => {
    await mockSignedInSession(page, { admin: true });
    const tenant = await mockTenant(page);
    await page.goto('/administration/policies');

    await page.getByLabel('Idle timeout').fill('2');
    await page.getByRole('button', { name: 'Save policy' }).click();

    await expect(page.getByText('Idle timeout must be between 5 and 120 minutes.')).toBeVisible();
    expect(tenant.policyPuts).toHaveLength(0);
  });

  test('the effect of a shorter idle timeout is shown before saving, and the saved value is then shown', async ({ page }) => {
    await mockSignedInSession(page, { admin: true });
    const tenant = await mockTenant(page);
    await page.goto('/administration/policies');

    await page.getByLabel('Idle timeout').fill('10');
    await expect(page.getByText('Users idle for more than 10 minutes will be signed out, down from 30 minutes.')).toBeVisible();
    await page.getByRole('button', { name: 'Save policy' }).click();

    await expect(page.getByRole('status')).toContainText('Security policy saved.');
    expect(tenant.policyPuts[0]).toMatchObject({ idleTimeoutMinutes: 10 });
    expect(tenant.policyPuts[0]).toMatchObject({ accessTokenLifetimeMinutes: 15 });
  });

  test('a refused policy shows the reasons and the saved values stay as they were', async ({ page }) => {
    await mockSignedInSession(page, { admin: true });
    await mockTenant(page, { policyRefusal: ['clipboard clear delay is not allowed by the tenant.'] });
    await page.goto('/administration/policies');

    await page.getByLabel('Clipboard clear delay').fill('20');
    await page.getByRole('button', { name: 'Save policy' }).click();

    await expect(page.getByRole('alert')).toContainText('clipboard clear delay is not allowed by the tenant.');
    await expect(page.getByLabel('Clipboard clear delay')).toHaveValue('20');
    await expect(page.getByText('Saved: 30 seconds.')).toBeVisible();
  });
});
