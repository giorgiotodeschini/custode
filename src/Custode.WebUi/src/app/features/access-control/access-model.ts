// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import type { PermissionLevel, PrincipalGrant, PrincipalKind } from './access-api.service';

export const LEVELS: readonly PermissionLevel[] = ['read', 'write', 'create', 'delete'];

export const LEVEL_LABELS: Record<PermissionLevel, string> = {
  read: 'Read',
  write: 'Write',
  create: 'Create',
  delete: 'Delete',
};

export interface PermissionPreset {
  id: 'viewer' | 'editor' | 'full';
  label: string;
  levels: PermissionLevel[];
}

/** The presets of webui-access-control-management. Any other combination is a custom one. */
export const PRESETS: readonly PermissionPreset[] = [
  { id: 'viewer', label: 'Viewer (Read)', levels: ['read'] },
  { id: 'editor', label: 'Editor (Read, Write, Create)', levels: ['read', 'write', 'create'] },
  { id: 'full', label: 'Full (Read, Write, Create, Delete)', levels: ['read', 'write', 'create', 'delete'] },
];

export type LevelState =
  | { kind: 'none' }
  | { kind: 'direct' }
  | { kind: 'inherited'; from: string };

/** Whether a level is set on this folder, inherited from an ancestor, or not granted at all. */
export function levelStates(grant: PrincipalGrant, folderPath: string): Record<PermissionLevel, LevelState> {
  const states = Object.fromEntries(LEVELS.map((level) => [level, { kind: 'none' } as LevelState])) as Record<
    PermissionLevel,
    LevelState
  >;
  for (const source of grant.sources) {
    for (const level of source.permissions) {
      const current = states[level];
      if (source.folderPath === folderPath) {
        states[level] = { kind: 'direct' };
      } else if (current.kind === 'none') {
        states[level] = { kind: 'inherited', from: source.folderPath };
      }
    }
  }
  return states;
}

export function directLevels(grant: PrincipalGrant, folderPath: string): PermissionLevel[] {
  return grant.sources
    .filter((source) => source.folderPath === folderPath)
    .flatMap((source) => source.permissions)
    .filter((level, index, all) => all.indexOf(level) === index);
}

/** Levels that a folder would lose when its inheritance is broken: those granted only by an ancestor. */
export function inheritedOnly(grants: readonly PrincipalGrant[], folderPath: string): PrincipalGrant[] {
  return grants
    .map((grant) => {
      const states = levelStates(grant, folderPath);
      const lost = LEVELS.filter((level) => states[level].kind === 'inherited');
      return lost.length > 0 ? { ...grant, effectivePermissions: lost } : null;
    })
    .filter((grant): grant is PrincipalGrant => grant !== null);
}

export function presetFor(levels: readonly PermissionLevel[]): PermissionPreset['id'] | 'custom' {
  const sorted = [...levels].sort().join(',');
  const preset = PRESETS.find((candidate) => [...candidate.levels].sort().join(',') === sorted);
  return preset?.id ?? 'custom';
}

export function keyOf(kind: PrincipalKind, id: string): string {
  return `${kind}:${id}`;
}

export function principalKey(grant: Pick<PrincipalGrant, 'principalKind' | 'principalId'>): string {
  return keyOf(grant.principalKind, grant.principalId);
}
