// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { BreakpointObserver } from '@angular/cdk/layout';
import { CdkTrapFocus } from '@angular/cdk/a11y';
import { Component, ElementRef, Injector, afterNextRender, computed, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';
import { NoticeService } from '../core/notifications/notice.service';
import { isTenantAdministrator } from '../core/session/access-token';
import { SessionService } from '../core/session/session.service';
import { CommandPaletteService } from './command-palette/command-palette.service';
import { ClipboardCopyService } from '../core/clipboard/clipboard-copy.service';
import { IdleTimeoutService } from '../features/session/idle-timeout.service';

export const NARROW_VIEWPORT_QUERY = '(max-width: 767.98px)';

interface NavItem {
  label: string;
  path: string;
}

@Component({
  selector: 'app-shell',
  imports: [CdkTrapFocus, RouterLink, RouterLinkActive, RouterOutlet],
  templateUrl: './app-shell.html',
})
export class AppShell {
  private readonly breakpoints = inject(BreakpointObserver);
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);
  private readonly session = inject(SessionService);
  protected readonly notices = inject(NoticeService);
  protected readonly palette = inject(CommandPaletteService);
  private readonly idleTimeout = inject(IdleTimeoutService);
  private readonly clipboardCopy = inject(ClipboardCopyService);

  protected readonly navItems = computed<NavItem[]>(() => {
    const items: NavItem[] = [
      { label: 'Folders', path: '/folders' },
      { label: 'Settings', path: '/settings' },
    ];
    if (!isTenantAdministrator(this.session.claims())) {
      return items;
    }
    return [
      ...items,
      { label: 'Audit Logs', path: '/audit' },
      { label: 'Tenant Administration', path: '/administration' },
    ];
  });

  private readonly menuButton = viewChild<ElementRef<HTMLButtonElement>>('menuButton');
  private readonly closeButton = viewChild<ElementRef<HTMLButtonElement>>('closeButton');

  protected readonly narrow = toSignal(
    this.breakpoints.observe(NARROW_VIEWPORT_QUERY).pipe(map((state) => state.matches)),
    { initialValue: false },
  );
  protected readonly drawerOpen = signal(false);

  protected readonly rootClasses = computed(() =>
    this.narrow() ? 'flex min-h-dvh flex-col' : 'flex min-h-dvh flex-row',
  );

  protected readonly sidebarClasses = computed(() => {
    const base = 'flex w-64 shrink-0 flex-col border-r border-border bg-surface-muted motion-safe:transition-transform';
    if (!this.narrow()) {
      return `${base} sticky top-0 h-dvh`;
    }
    const drawer = 'fixed inset-y-0 left-0 z-40 w-72 max-w-[85vw]';
    return `${base} ${drawer} ${this.drawerOpen() ? 'translate-x-0' : '-translate-x-full'}`;
  });

  constructor() {
    this.breakpoints
      .observe(NARROW_VIEWPORT_QUERY)
      .pipe(takeUntilDestroyed())
      .subscribe((state) => {
        if (!state.matches) {
          this.drawerOpen.set(false);
        }
      });

    this.router.events
      .pipe(
        filter((event) => event instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe(() => this.drawerOpen.set(false));
  }

  protected signOut(): void {
    this.session.signOut().subscribe();
  }

  protected openDrawer(): void {
    this.drawerOpen.set(true);
    afterNextRender(() => this.closeButton()?.nativeElement.focus(), { injector: this.injector });
  }

  protected closeDrawer(): void {
    if (!this.drawerOpen()) {
      return;
    }
    this.drawerOpen.set(false);
    this.menuButton()?.nativeElement.focus();
  }
}
