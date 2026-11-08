// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { Dialog } from '@angular/cdk/dialog';
import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Subject, catchError, forkJoin, map, of, switchMap } from 'rxjs';
import { NoticeService } from '../../core/notifications/notice.service';
import { AccessApi, GroupMember, GroupSummary, TenantMember } from '../access-control/access-api.service';
import { serverMessage } from '../secret-browser/create-folder-dialog';
import { confirmAction } from '../../shared/ui/confirm-dialog';

interface GroupData {
  group: GroupSummary | null;
  members: GroupMember[];
  tenantMembers: TenantMember[];
}

/** One group: its members, and adding or removing them (webui-tenant-administration). */
@Component({
  selector: 'app-group-detail-page',
  imports: [RouterLink],
  templateUrl: './group-detail-page.html',
})
export class GroupDetailPage {
  private readonly route = inject(ActivatedRoute);
  private readonly access = inject(AccessApi);
  private readonly dialog = inject(Dialog);
  private readonly notices = inject(NoticeService);

  private readonly groupId = this.route.snapshot.paramMap.get('id') ?? '';
  protected readonly group = signal<GroupSummary | null>(null);
  protected readonly members = signal<GroupMember[]>([]);
  protected readonly candidates = signal<TenantMember[]>([]);
  protected readonly selectedUser = signal('');
  protected readonly loadError = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly available = computed(() => {
    const inGroup = new Set(this.members().map((member) => member.id));
    return this.candidates().filter((member) => member.isEnabled && !inGroup.has(member.id));
  });

  private readonly reload$ = new Subject<void>();

  constructor() {
    this.reload$
      .pipe(
        switchMap(() =>
          forkJoin({
            groups: this.access.groups(),
            members: this.access.groupMembers(this.groupId),
            tenantMembers: this.access.members(),
          }).pipe(
            map((data): GroupData => ({
              group: data.groups.find((group) => group.id === this.groupId) ?? null,
              members: data.members,
              tenantMembers: data.tenantMembers,
            })),
            catchError(() => of(null)),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((data) => {
        if (!data) {
          this.loadError.set('The group could not be loaded.');
          return;
        }
        this.loadError.set(data.group ? null : 'This group does not exist in your tenant.');
        this.group.set(data.group);
        this.members.set(data.members);
        this.candidates.set(data.tenantMembers);
      });
    this.reload$.next();
  }

  protected add(): void {
    const userId = this.selectedUser();
    if (!userId) {
      return;
    }
    const user = this.available().find((member) => member.id === userId);
    this.busy.set(true);
    this.actionError.set(null);
    this.access.addGroupMember(this.groupId, userId).subscribe({
      next: () => {
        this.busy.set(false);
        this.selectedUser.set('');
        this.notices.show(`${user?.login ?? 'The member'} added. The change applies on their next request.`);
        this.reload$.next();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.actionError.set(serverMessage(error, 'The member was not added.'));
        this.reload$.next();
      },
    });
  }

  protected remove(member: GroupMember): void {
    confirmAction(this.dialog, {
      title: `Remove ${member.login} from ${this.group()?.name ?? 'this group'}?`,
      message: 'The member loses the access that this group gives, on their next request.',
      confirmLabel: 'Remove member',
      destructive: true,
    }).subscribe((confirmed) => {
      if (!confirmed) {
        return;
      }
      this.busy.set(true);
      this.actionError.set(null);
      this.access.removeGroupMember(this.groupId, member.id).subscribe({
        next: () => {
          this.busy.set(false);
          this.notices.show(`${member.login} removed. They lose the group's access on their next request.`);
          this.reload$.next();
        },
        error: (error: unknown) => {
          this.busy.set(false);
          this.actionError.set(serverMessage(error, 'The member was not removed.'));
          this.reload$.next();
        },
      });
    });
  }
}
