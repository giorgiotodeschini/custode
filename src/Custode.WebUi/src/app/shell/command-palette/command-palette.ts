// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { CdkTrapFocus } from '@angular/cdk/a11y';
import { Component, computed, inject, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { catchError, debounceTime, map, of, switchMap } from 'rxjs';
import { FolderCatalogService } from '../../core/access/folder-catalog.service';
import { ThemeService } from '../../core/preferences/theme.service';
import { isTenantAdministrator } from '../../core/session/access-token';
import { SessionService } from '../../core/session/session.service';
import { EMPTY_SEARCH_RESULT, SearchResult, SearchService } from '../../core/search/search.service';
import { CommandPaletteService } from './command-palette.service';

interface PaletteItem {
  id: string;
  label: string;
  detail: string | null;
  run: () => void;
}

interface PaletteGroup {
  name: string;
  items: PaletteItem[];
}

const MAX_QUERY_LENGTH = 100;
const SEARCH_DEBOUNCE_MS = 200;

@Component({
  selector: 'app-command-palette',
  imports: [CdkTrapFocus],
  templateUrl: './command-palette.html',
})
export class CommandPalette {
  private readonly palette = inject(CommandPaletteService);
  private readonly search = inject(SearchService);
  private readonly catalog = inject(FolderCatalogService);
  private readonly router = inject(Router);
  private readonly theme = inject(ThemeService);
  private readonly session = inject(SessionService);

  protected readonly query = signal('');
  protected readonly maxQueryLength = MAX_QUERY_LENGTH;
  protected readonly activeIndex = signal(0);
  protected readonly canCreate = toSignal(this.catalog.canCreateSecret(), { initialValue: false });

  private readonly results = toSignal(
    toObservable(this.query).pipe(
      map((value) => value.trim()),
      debounceTime(SEARCH_DEBOUNCE_MS),
      switchMap((value) =>
        value.length === 0
          ? of(EMPTY_SEARCH_RESULT)
          : this.search.search(value).pipe(catchError(() => of(EMPTY_SEARCH_RESULT))),
      ),
    ),
    { initialValue: EMPTY_SEARCH_RESULT as SearchResult },
  );

  protected readonly groups = computed<PaletteGroup[]>(() => {
    const result = this.results();
    const folders: PaletteItem[] = result.folders.map((folder) => ({
      id: `folder:${folder.path}`,
      label: folder.path,
      detail: null,
      run: () => this.openFolder(folder.path),
    }));
    const secrets: PaletteItem[] = result.secrets.map((secret) => ({
      id: `secret:${secret.id}`,
      label: secret.name,
      detail: secret.folderPath,
      run: () => this.openFolder(secret.folderPath),
    }));
    const groups: PaletteGroup[] = [
      { name: 'Folders', items: folders },
      { name: 'Secrets', items: secrets },
      { name: 'Actions', items: this.actions() },
    ];
    return groups.filter((group) => group.items.length > 0);
  });

  protected readonly flatItems = computed(() => this.groups().flatMap((group) => group.items));
  protected readonly activeId = computed(() => this.flatItems()[this.activeIndex()]?.id ?? null);

  protected onInput(value: string): void {
    this.query.set(value.slice(0, MAX_QUERY_LENGTH));
    this.activeIndex.set(0);
  }

  protected move(step: number): void {
    const count = this.flatItems().length;
    if (count === 0) {
      return;
    }
    this.activeIndex.update((index) => Math.min(Math.max(index + step, 0), count - 1));
  }

  protected runActive(): void {
    const item = this.flatItems()[this.activeIndex()];
    if (item) {
      this.close();
      item.run();
    }
  }

  protected close(): void {
    this.palette.close();
  }

  private actions(): PaletteItem[] {
    const text = this.query().trim().toLowerCase();
    const actions: Array<PaletteItem & { visible: boolean }> = [
      {
        id: 'action:new-secret',
        label: 'New secret',
        detail: null,
        visible: this.canCreate(),
        run: () => this.navigate('/secrets/new'),
      },
      {
        id: 'action:audit-log',
        label: 'Audit log',
        detail: null,
        visible: isTenantAdministrator(this.session.claims()),
        run: () => this.navigate('/audit'),
      },
      {
        id: 'action:settings',
        label: 'Settings',
        detail: null,
        visible: true,
        run: () => this.navigate('/settings'),
      },
      {
        id: 'action:toggle-theme',
        label: 'Toggle theme',
        detail: null,
        visible: true,
        run: () => this.theme.toggle(),
      },
    ];
    return actions
      .filter((action) => action.visible)
      .filter((action) => text.length === 0 || action.label.toLowerCase().includes(text))
      .map(({ visible: _visible, ...item }) => item);
  }

  private openFolder(path: string): void {
    const segments = path.split('/').filter((segment) => segment.length > 0).map(encodeURIComponent);
    this.navigate(['/folders', ...segments].join('/'));
  }

  private navigate(url: string): void {
    void this.router.navigateByUrl(url);
  }
}
