// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { Dialog } from '@angular/cdk/dialog';
import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { Subject, forkJoin, map, merge, switchMap, of, catchError } from 'rxjs';
import { FolderTree } from '../secret-browser/folder-tree';
import { serverMessage } from '../secret-browser/create-folder-dialog';
import { confirmAction } from '../../shared/ui/confirm-dialog';
import { NoticeService } from '../../core/notifications/notice.service';
import {
  AccessApi,
  FolderInheritance,
  PermissionLevel,
  PrincipalGrant,
  PrincipalKind,
} from './access-api.service';
import {
  LEVELS,
  LEVEL_LABELS,
  PRESETS,
  directLevels,
  inheritedOnly,
  levelStates,
  keyOf,
  presetFor,
  principalKey,
} from './access-model';

interface PrincipalOption {
  kind: PrincipalKind;
  id: string;
  name: string;
}

type PresetChoice = 'viewer' | 'editor' | 'full' | 'custom';

/** Folder ACL view for tenant administrators: grants, inheritance, and their changes (webui-access-control-management). */
@Component({
  selector: 'app-access-control-page',
  imports: [FolderTree],
  templateUrl: './access-control-page.html',
})
export class AccessControlPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(AccessApi);
  private readonly dialog = inject(Dialog);
  private readonly notices = inject(NoticeService);

  protected readonly levels = LEVELS;
  protected readonly principalKey = principalKey;
  protected readonly labels = LEVEL_LABELS;
  protected readonly presets = PRESETS;

  protected readonly folderPath = toSignal(
    this.route.queryParamMap.pipe(map((params) => params.get('folderPath') ?? '/')),
    { initialValue: this.route.snapshot.queryParamMap.get('folderPath') ?? '/' },
  );
  protected readonly grants = signal<PrincipalGrant[]>([]);
  protected readonly folders = signal<FolderInheritance[]>([]);
  protected readonly principals = signal<PrincipalOption[]>([]);
  protected readonly loadError = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);
  protected readonly busy = signal(false);

  protected readonly folderInfo = computed(() => this.folders().find((folder) => folder.path === this.folderPath()) ?? null);
  protected readonly inheritsFromParent = computed(() => this.folderInfo()?.inheritsPermissions ?? false);
  protected readonly hasParent = computed(() => this.folderInfo()?.parentPath != null);
  protected readonly inheritanceMap = computed(
    () => new Map(this.folders().map((folder) => [folder.path, folder.inheritsPermissions] as [string, boolean])),
  );

  // Editor for one principal
  protected readonly selectedKey = signal('');
  protected readonly preset = signal<PresetChoice>('viewer');
  protected readonly customLevels = signal<ReadonlySet<PermissionLevel>>(new Set(['read']));
  protected readonly levelsToSave = computed<PermissionLevel[]>(() => {
    if (this.preset() === 'custom') {
      return LEVELS.filter((level) => this.customLevels().has(level));
    }
    return PRESETS.find((preset) => preset.id === this.preset())?.levels ?? [];
  });
  protected readonly selectedPrincipal = computed(() => this.principals().find((option) => keyOf(option.kind, option.id) === this.selectedKey()) ?? null);

  private readonly reload$ = new Subject<void>();

  constructor() {
    merge(toObservable(this.folderPath), this.reload$)
      .pipe(
        switchMap(() => {
          const path = this.folderPath();
          return forkJoin({
            grants: this.api.grantsOf(path),
            folders: this.api.folders(),
            groups: this.api.groups().pipe(catchError(() => of([]))),
            members: this.api.members().pipe(catchError(() => of([]))),
          }).pipe(
            map((loaded) => ({ path, ...loaded })),
            catchError(() => of(null)),
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe((loaded) => {
        if (!loaded) {
          this.loadError.set('The permissions of this folder could not be loaded.');
          return;
        }
        this.loadError.set(null);
        this.grants.set(loaded.grants);
        this.folders.set(loaded.folders);
        this.principals.set([
          ...loaded.groups.map((group) => ({ kind: 'group' as const, id: group.id, name: group.name })),
          ...loaded.members.map((member) => ({ kind: 'user' as const, id: member.id, name: member.login })),
        ]);
      });
  }

  protected openFolder(path: string): void {
    this.actionError.set(null);
    void this.router.navigate(['/administration/access'], { queryParams: { folderPath: path } });
  }

  protected levelCell(grant: PrincipalGrant, level: PermissionLevel): { text: string; title: string } {
    const state = levelStates(grant, this.folderPath())[level];
    if (state.kind === 'direct') {
      return { text: 'Direct', title: `${this.labels[level]} is set on this folder` };
    }
    if (state.kind === 'inherited') {
      return { text: `Inherited from ${state.from}`, title: `${this.labels[level]} is inherited from ${state.from}` };
    }
    return { text: '—', title: `${this.labels[level]} is not granted` };
  }

  protected hasDirect(grant: PrincipalGrant): boolean {
    return directLevels(grant, this.folderPath()).length > 0;
  }

  protected editGrant(grant: PrincipalGrant): void {
    const levels = directLevels(grant, this.folderPath());
    this.selectedKey.set(principalKey(grant));
    const choice = presetFor(levels);
    this.preset.set(choice);
    this.customLevels.set(new Set(levels.length > 0 ? levels : ['read']));
    this.actionError.set(null);
  }

  protected choosePrincipal(key: string): void {
    this.selectedKey.set(key);
    this.actionError.set(null);
  }

  protected choosePreset(choice: PresetChoice): void {
    this.preset.set(choice);
    if (choice !== 'custom') {
      const preset = PRESETS.find((candidate) => candidate.id === choice);
      this.customLevels.set(new Set(preset?.levels ?? []));
    }
  }

  protected toggleCustomLevel(level: PermissionLevel, checked: boolean): void {
    this.customLevels.update((current) => {
      const next = new Set(current);
      if (checked) {
        next.add(level);
      } else {
        next.delete(level);
      }
      return next;
    });
  }

  /** Sends the grant; the table is reloaded from the server afterwards, so only confirmed state is shown. */
  protected saveGrant(): void {
    const principal = this.selectedPrincipal();
    const levels = this.levelsToSave();
    if (!principal || levels.length === 0) {
      this.actionError.set('Choose a principal and at least one permission.');
      return;
    }
    this.busy.set(true);
    this.actionError.set(null);
    this.api.setGrant(this.folderPath(), principal.kind, principal.id, levels).subscribe({
      next: () => {
        this.busy.set(false);
        this.notices.show(`Access updated for ${principal.name} on ${this.folderPath()}.`);
        this.reload$.next();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.actionError.set(serverMessage(error, 'The change was refused by the server.'));
        this.reload$.next();
      },
    });
  }

  protected revoke(grant: PrincipalGrant): void {
    confirmAction(this.dialog, {
      title: `Revoke ${grant.principalName} on ${this.folderPath()}?`,
      message: 'The direct grants of this principal on this folder are removed. Inherited access from ancestors stays.',
      confirmLabel: 'Revoke grant',
      destructive: true,
    }).subscribe((confirmed) => {
      if (!confirmed) {
        return;
      }
      this.api.revokeGrant(this.folderPath(), grant.principalKind, grant.principalId).subscribe({
        next: () => {
          this.notices.show(`Revoked ${grant.principalName} on ${this.folderPath()}.`);
          this.reload$.next();
        },
        error: (error: unknown) => {
          this.actionError.set(serverMessage(error, 'The revocation was refused by the server.'));
          this.reload$.next();
        },
      });
    });
  }

  /** Shows what would stop applying before the break is sent. Cancelling changes nothing. */
  protected breakInheritance(): void {
    const folder = this.folderPath();
    const lost = inheritedOnly(this.grants(), folder).map(
      (grant) => `${grant.principalName}: ${grant.effectivePermissions.map((level) => this.labels[level]).join(', ')} (from an ancestor)`,
    );
    const descendants = this.folders().filter((candidate) => candidate.path.startsWith(`${folder === '/' ? '' : folder}/`) && candidate.inheritsPermissions).length;
    confirmAction(this.dialog, {
      title: `Break inheritance on ${folder}?`,
      message: `${descendants} subfolder${descendants === 1 ? '' : 's'} inheriting from this folder would also lose these grants. Direct grants stay.`,
      details: lost.length > 0 ? lost : ['No inherited access would be removed.'],
      confirmLabel: 'Break inheritance',
      destructive: true,
    }).subscribe((confirmed) => {
      if (!confirmed) {
        return;
      }
      this.runInheritanceChange(this.api.breakInheritance(folder), 'Inheritance broken.');
    });
  }

  /** Lists the grants of the parent that apply again before the restore is sent. */
  protected restoreInheritance(): void {
    const folder = this.folderPath();
    const parent = this.folderInfo()?.parentPath;
    if (!parent) {
      return;
    }
    this.api.grantsOf(parent).subscribe({
      next: (parentGrants) => {
        const returning = parentGrants
          .filter((grant) => grant.effectivePermissions.length > 0)
          .map((grant) => `${grant.principalName}: ${grant.effectivePermissions.map((level) => this.labels[level]).join(', ')} (from ${parent})`);
        confirmAction(this.dialog, {
          title: `Restore inheritance on ${folder}?`,
          message: 'Access granted on the parent folder applies here again. Direct grants stay.',
          details: returning.length > 0 ? returning : ['No access would return.'],
          confirmLabel: 'Restore inheritance',
        }).subscribe((confirmed) => {
          if (confirmed) {
            this.runInheritanceChange(this.api.restoreInheritance(folder), 'Inheritance restored.');
          }
        });
      },
      error: (error: unknown) => this.actionError.set(serverMessage(error, 'The parent folder could not be read.')),
    });
  }

  private runInheritanceChange(request: ReturnType<AccessApi['breakInheritance']>, message: string): void {
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
