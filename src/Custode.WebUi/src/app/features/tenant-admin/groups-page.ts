// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { Dialog } from '@angular/cdk/dialog';
import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { Subject, catchError, map, of, switchMap } from 'rxjs';
import { AccessApi, GroupSummary } from '../access-control/access-api.service';
import { NoticeService } from '../../core/notifications/notice.service';
import { serverMessage } from '../secret-browser/create-folder-dialog';
import { confirmAction } from '../../shared/ui/confirm-dialog';

export const DUPLICATE_GROUP_MESSAGE = 'A group with this name already exists in the tenant.';

/** Whether another group already uses the name (case-insensitively). */
export function nameTaken(name: string, groups: readonly GroupSummary[], exceptId: string | null = null): boolean {
  const wanted = name.trim().toLowerCase();
  return groups.some((group) => group.id !== exceptId && group.name.trim().toLowerCase() === wanted);
}

@Component({
  selector: 'app-groups-page',
  imports: [RouterLink],
  templateUrl: './groups-page.html',
})
export class GroupsPage {
  private readonly access = inject(AccessApi);
  private readonly dialog = inject(Dialog);
  private readonly notices = inject(NoticeService);

  protected readonly groups = signal<GroupSummary[]>([]);
  protected readonly loadError = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);
  protected readonly busy = signal(false);

  protected readonly newName = signal('');
  protected readonly createError = signal<string | null>(null);
  protected readonly renamingId = signal<string | null>(null);
  protected readonly renameValue = signal('');
  protected readonly renameError = signal<string | null>(null);
  protected readonly sortedGroups = computed(() => [...this.groups()].sort((a, b) => a.name.localeCompare(b.name)));

  private readonly reload$ = new Subject<void>();

  constructor() {
    this.reload$
      .pipe(
        switchMap(() =>
          this.access.groups().pipe(
            map((list) => ({ list, error: null as string | null })),
            catchError(() => of({ list: null, error: 'The groups could not be loaded.' })),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe(({ list, error }) => {
        this.loadError.set(error);
        if (list) {
          this.groups.set(list);
        }
      });
    this.reload$.next();
  }

  protected create(): void {
    const name = this.newName().trim();
    if (name.length === 0) {
      this.createError.set('Enter a name for the group.');
      return;
    }
    if (nameTaken(name, this.groups())) {
      this.createError.set(DUPLICATE_GROUP_MESSAGE);
      return;
    }
    this.busy.set(true);
    this.createError.set(null);
    this.access.createGroup(name).subscribe({
      next: () => {
        this.busy.set(false);
        this.newName.set('');
        this.notices.show(`Group ${name} created.`);
        this.reload$.next();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.createError.set(serverMessage(error, DUPLICATE_GROUP_MESSAGE));
      },
    });
  }

  protected startRename(group: GroupSummary): void {
    this.renamingId.set(group.id);
    this.renameValue.set(group.name);
    this.renameError.set(null);
  }

  protected rename(group: GroupSummary): void {
    const name = this.renameValue().trim();
    if (name.length === 0) {
      this.renameError.set('Enter a name for the group.');
      return;
    }
    if (nameTaken(name, this.groups(), group.id)) {
      this.renameError.set(DUPLICATE_GROUP_MESSAGE);
      return;
    }
    this.busy.set(true);
    this.access.renameGroup(group.id, name).subscribe({
      next: () => {
        this.busy.set(false);
        this.renamingId.set(null);
        this.notices.show(`Group renamed to ${name}.`);
        this.reload$.next();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.renameError.set(serverMessage(error, DUPLICATE_GROUP_MESSAGE));
      },
    });
  }

  /** The confirmation states how many members and grants the deletion removes. */
  protected remove(group: GroupSummary): void {
    confirmAction(this.dialog, {
      title: `Delete group ${group.name}?`,
      message: `${group.memberCount} ${group.memberCount === 1 ? 'member' : 'members'} will be removed from the group and ${group.grantCount} folder ${group.grantCount === 1 ? 'grant' : 'grants'} will be deleted. This cannot be undone.`,
      confirmLabel: 'Delete group',
      destructive: true,
    }).subscribe((confirmed) => {
      if (!confirmed) {
        return;
      }
      this.busy.set(true);
      this.actionError.set(null);
      this.access.deleteGroup(group.id).subscribe({
        next: () => {
          this.busy.set(false);
          this.notices.show(`Group ${group.name} deleted.`);
          this.reload$.next();
        },
        error: (error: unknown) => {
          this.busy.set(false);
          this.actionError.set(serverMessage(error, 'The group was not deleted.'));
          this.reload$.next();
        },
      });
    });
  }
}
