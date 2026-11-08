// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { Dialog } from '@angular/cdk/dialog';
import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subject, catchError, map, of, switchMap } from 'rxjs';
import { AccessApi, TenantMember } from '../access-control/access-api.service';
import { NoticeService } from '../../core/notifications/notice.service';
import { serverMessage } from '../secret-browser/create-folder-dialog';
import { confirmAction } from '../../shared/ui/confirm-dialog';
import { TenantApi } from './tenant-api.service';

export function isAdministrator(member: Pick<TenantMember, 'roles'>): boolean {
  return member.roles.includes('TenantAdministrator');
}

/**
 * Members of the tenant: create with an initial password, disable and enable, and grant or withdraw the
 * administrator role. Every displayed state comes from the server after the change (webui-tenant-administration).
 */
@Component({
  selector: 'app-members-page',
  templateUrl: './members-page.html',
})
export class MembersPage {
  private readonly access = inject(AccessApi);
  private readonly tenant = inject(TenantApi);
  private readonly dialog = inject(Dialog);
  private readonly notices = inject(NoticeService);

  protected readonly members = signal<TenantMember[]>([]);
  protected readonly loadError = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);
  protected readonly busy = signal(false);

  protected readonly login = signal('');
  protected readonly password = signal('');
  protected readonly createError = signal<string | null>(null);
  protected readonly creating = signal(false);

  protected readonly enabledAdministrators = computed(
    () => this.members().filter((member) => member.isEnabled && isAdministrator(member)).length,
  );

  private readonly reload$ = new Subject<void>();

  constructor() {
    this.reload$
      .pipe(
        switchMap(() =>
          this.access.members().pipe(
            map((list) => ({ list, error: null as string | null })),
            catchError(() => of({ list: null, error: 'The members could not be loaded.' })),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe(({ list, error }) => {
        this.loadError.set(error);
        if (list) {
          this.members.set(list);
        }
      });
    this.reload$.next();
  }

  protected isAdmin(member: TenantMember): boolean {
    return isAdministrator(member);
  }

  /** The last enabled administrator cannot lose the role, and the page says why. */
  protected cannotWithdraw(member: TenantMember): boolean {
    return isAdministrator(member) && member.isEnabled && this.enabledAdministrators() <= 1;
  }

  protected create(): void {
    const login = this.login().trim();
    if (login.length === 0 || this.password().length === 0) {
      this.createError.set('Enter a login and an initial password.');
      return;
    }
    this.creating.set(true);
    this.createError.set(null);
    this.tenant.createMember(login, this.password()).subscribe({
      next: (created) => {
        this.creating.set(false);
        this.login.set('');
        this.password.set('');
        this.notices.show(`Member ${created.login} created. The initial password must be changed at first sign-in.`);
        this.reload$.next();
      },
      error: (error: unknown) => {
        this.creating.set(false);
        this.password.set('');
        this.createError.set(serverMessage(error, 'The member could not be created.'));
      },
    });
  }

  protected toggleEnabled(member: TenantMember): void {
    const enable = !member.isEnabled;
    const run = () => this.run(this.tenant.setMemberEnabled(member.id, enable), enable ? `${member.login} is enabled.` : `${member.login} is disabled.`);
    if (!enable) {
      confirmAction(this.dialog, {
        title: `Disable ${member.login}?`,
        message: 'Their sessions end and they cannot sign in until the member is enabled again.',
        confirmLabel: 'Disable member',
        destructive: true,
      }).subscribe((confirmed) => {
        if (confirmed) {
          run();
        }
      });
      return;
    }
    run();
  }

  protected toggleAdministrator(member: TenantMember): void {
    const grant = !isAdministrator(member);
    const run = () =>
      this.run(
        this.tenant.setAdministrator(member.id, grant),
        grant ? `${member.login} is now a tenant administrator.` : `${member.login} is no longer a tenant administrator.`,
      );
    if (grant) {
      run();
      return;
    }
    confirmAction(this.dialog, {
      title: `Withdraw the administrator role from ${member.login}?`,
      message: 'The member keeps the other roles and loses access to the administration pages.',
      confirmLabel: 'Withdraw role',
      destructive: true,
    }).subscribe((confirmed) => {
      if (confirmed) {
        run();
      }
    });
  }

  private run(request: ReturnType<TenantApi['setMemberEnabled']>, message: string): void {
    this.busy.set(true);
    this.actionError.set(null);
    request.subscribe({
      next: () => {
        this.busy.set(false);
        this.notices.show(message);
        this.reload$.next();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.actionError.set(serverMessage(error, 'The change was refused by the server.'));
        this.reload$.next();
      },
    });
  }
}
