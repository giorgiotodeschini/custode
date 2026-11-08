// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { Dialog } from '@angular/cdk/dialog';
import { Component, computed, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink, UrlSegment } from '@angular/router';
import { Subject, catchError, forkJoin, map, merge, of, switchMap } from 'rxjs';
import { SecretPresentation, SecretPresentationService } from '../../core/preferences/secret-presentation.service';
import { isTenantAdministrator } from '../../core/session/access-token';
import { SessionService } from '../../core/session/session.service';
import { SecretMetadata, VaultApi } from '../../core/vault/vault-api.service';
import { confirmAction } from '../../shared/ui/confirm-dialog';
import { CreateFolderDialog, CreateFolderDialogData, serverMessage } from './create-folder-dialog';
import { FolderTree } from './folder-tree';
import { SecretDetail, SecretDetailData } from './secret-detail';
import { SecretList } from './secret-list';

/** Maps `/folders/a/b` to `/a/b`. The folder path is part of the URL so that a folder can be linked. */
function folderPathOf(segments: UrlSegment[]): string {
  const parts = segments.slice(1).map((segment) => segment.path).filter((part) => part.length > 0);
  return `/${parts.join('/')}`;
}

export function folderUrl(path: string): string {
  const parts = path.split('/').filter((part) => part.length > 0).map(encodeURIComponent);
  return ['/folders', ...parts].join('/');
}

@Component({
  selector: 'app-folders-page',
  imports: [FolderTree, SecretList, RouterLink],
  templateUrl: './folders-page.html',
})
export class FoldersPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(VaultApi);
  private readonly dialog = inject(Dialog);
  private readonly presentationStore = inject(SecretPresentationService);
  private readonly session = inject(SessionService);
  private readonly tree = viewChild.required(FolderTree);

  protected readonly folderPath = toSignal(this.route.url.pipe(map(folderPathOf)), { initialValue: '/' });
  protected readonly presentation = this.presentationStore.presentation;
  protected readonly secrets = signal<SecretMetadata[]>([]);
  protected readonly permissions = signal<string[]>([]);
  protected readonly loadError = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);

  protected readonly isAdmin = computed(() => isTenantAdministrator(this.session.claims()));
  protected readonly canCreate = computed(() => this.permissions().includes('create'));
  protected readonly canDelete = computed(() => this.permissions().includes('delete') && this.folderPath() !== '/');

  private readonly refresh = new Subject<void>();

  constructor() {
    merge(toObservable(this.folderPath), this.refresh)
      .pipe(
        switchMap(() => {
          const path = this.folderPath();
          return forkJoin({
            secrets: this.api.listSecrets(path).pipe(
              map((list) => ({ list, error: null as string | null })),
              catchError((error: unknown) =>
                of({ list: [] as SecretMetadata[], error: serverMessage(error, 'You do not have Read permission on this folder.') }),
              ),
            ),
            permissions: this.api.callerPermissions(path).pipe(
              map((result) => result.permissions),
              catchError(() => of([] as string[])),
            ),
          });
        }),
        takeUntilDestroyed(),
      )
      .subscribe(({ secrets, permissions }) => {
        this.secrets.set(secrets.list);
        this.loadError.set(secrets.error);
        this.permissions.set(permissions);
      });
  }

  protected openFolder(path: string): void {
    this.actionError.set(null);
    void this.router.navigateByUrl(folderUrl(path));
  }

  protected choosePresentation(presentation: SecretPresentation): void {
    this.presentationStore.choose(presentation);
  }

  protected openSecret(secret: SecretMetadata): void {
    this.dialog
      .open<'changed' | undefined, SecretDetailData, SecretDetail>(SecretDetail, {
        data: { secret },
        role: 'dialog',
        ariaLabelledBy: 'secret-detail-title',
        backdropClass: ['bg-text/40'],
      })
      .closed.subscribe((result) => {
        if (result === 'changed') {
          this.refresh.next();
        }
      });
  }

  protected newFolder(): void {
    this.dialog
      .open<string, CreateFolderDialogData, CreateFolderDialog>(CreateFolderDialog, {
        data: { parentPath: this.folderPath() },
        role: 'dialog',
        ariaLabelledBy: 'create-folder-title',
        backdropClass: ['bg-text/40'],
      })
      .closed.subscribe((createdPath) => {
        if (createdPath) {
          void this.tree().reload();
          this.openFolder(createdPath);
        }
      });
  }

  protected deleteFolder(): void {
    const path = this.folderPath();
    confirmAction(this.dialog, {
      title: `Delete folder ${path}?`,
      message: 'The folder is removed only when it is empty.',
      confirmLabel: 'Delete folder',
      destructive: true,
    }).subscribe((confirmed) => {
      if (!confirmed) {
        return;
      }
      this.api.deleteFolder(path).subscribe({
        next: () => {
          const parent = path.substring(0, path.lastIndexOf('/')) || '/';
          void this.tree().reload();
          this.openFolder(parent);
        },
        error: (error: unknown) =>
          this.actionError.set(serverMessage(error, 'The folder could not be deleted.')),
      });
    });
  }
}
