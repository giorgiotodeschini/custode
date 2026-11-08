// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { Routes } from '@angular/router';
import { adminGuard } from './core/guards/admin.guard';
import { authGuard } from './core/guards/auth.guard';
import { unsavedChangesGuard } from './features/secret-editor/unsaved-changes.guard';
import { ScreenPlaceholder } from './shell/placeholder/screen-placeholder';

export const routes: Routes = [
  {
    path: 'sign-in',
    loadComponent: () => import('./features/session/sign-in/sign-in').then((m) => m.SignIn),
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./shell/app-shell').then((m) => m.AppShell),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'folders' },
      {
        path: 'folders',
        loadComponent: () => import('./features/secret-browser/folders-page').then((m) => m.FoldersPage),
      },
      {
        path: 'folders/**',
        loadComponent: () => import('./features/secret-browser/folders-page').then((m) => m.FoldersPage),
      },
      {
        path: 'secrets/new',
        loadComponent: () => import('./features/secret-editor/secret-editor-page').then((m) => m.SecretEditorPage),
        canDeactivate: [unsavedChangesGuard],
      },
      {
        path: 'secrets/:id/edit',
        loadComponent: () => import('./features/secret-editor/secret-editor-page').then((m) => m.SecretEditorPage),
        canDeactivate: [unsavedChangesGuard],
      },
      {
        path: 'settings',
        loadComponent: () => import('./features/settings/settings-page').then((m) => m.SettingsPage),
      },
      {
        path: 'audit',
        canActivate: [adminGuard],
        loadComponent: () => import('./features/audit/audit-page').then((m) => m.AuditPage),
      },
      {
        path: 'administration',
        canActivate: [adminGuard],
        children: [
          {
            path: '',
            loadComponent: () => import('./features/tenant-admin/administration-page').then((m) => m.AdministrationPage),
          },
          {
            path: 'members',
            loadComponent: () => import('./features/tenant-admin/members-page').then((m) => m.MembersPage),
          },
          {
            path: 'groups',
            loadComponent: () => import('./features/tenant-admin/groups-page').then((m) => m.GroupsPage),
          },
          {
            path: 'groups/:id',
            loadComponent: () => import('./features/tenant-admin/group-detail-page').then((m) => m.GroupDetailPage),
          },
          {
            path: 'policies',
            loadComponent: () => import('./features/tenant-admin/policies-page').then((m) => m.PoliciesPage),
          },
          {
            path: 'access',
            loadComponent: () => import('./features/access-control/access-control-page').then((m) => m.AccessControlPage),
          },
          {
            path: 'backup',
            loadComponent: () => import('./features/secret-transfer/backup-page').then((m) => m.BackupPage),
          },
        ],
      },
      {
        path: 'transfer',
        loadComponent: () => import('./features/secret-transfer/transfer-page').then((m) => m.TransferPage),
      },
      {
        path: '**',
        loadComponent: () => import('./features/not-found/not-found-page').then((m) => m.NotFoundPage),
      },
    ],
  },
];
