// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

export const DATA_LAYER_ACTOR = '00000000-0000-0000-0000-000000000000';

/** The audit actions recorded by the server (AuditActions). Used to offer the action filter. */
export const AUDIT_ACTIONS: readonly string[] = [
  'secret created', 'secret updated', 'secret deleted', 'secret moved', 'secret value read',
  'folder created', 'folder deleted', 'group created', 'group renamed', 'group deleted',
  'membership added', 'membership removed', 'grant set', 'grant revoked', 'inheritance broken',
  'inheritance restored', 'vault initialized', 'audit tampering attempt', 'session revoked',
  'refresh token reuse detected', 'password changed', 'member created', 'member disabled',
  'member enabled', 'administrator role granted', 'administrator role withdrawn',
  'administrator role changed', 'tenant policy changed', 'tenant backup exported',
  'secret rolled back', 'secrets imported',
];

export type TimeframePreset = 'last-24-hours' | 'last-7-days' | 'last-30-days' | 'custom';

const PRESET_HOURS: Record<Exclude<TimeframePreset, 'custom'>, number> = {
  'last-24-hours': 24,
  'last-7-days': 24 * 7,
  'last-30-days': 24 * 30,
};

export function presetStart(preset: Exclude<TimeframePreset, 'custom'>, now: Date): Date {
  return new Date(now.getTime() - PRESET_HOURS[preset] * 3_600_000);
}

/** Converts a value of a datetime-local input (local time) to an instant. */
export function localInputToInstant(value: string): string | null {
  if (!value) {
    return null;
  }
  const instant = new Date(value);
  return Number.isNaN(instant.getTime()) ? null : instant.toISOString();
}

export interface AuditEntryView {
  sequence: number;
  occurredAtUtc: string;
  actorUserId: string;
  action: string;
  outcome: string;
  targetType: string | null;
  targetId: string | null;
  secondaryTargetType: string | null;
  secondaryTargetId: string | null;
  hash: string;
  previousHash: string;
}

export type LinkStatus = 'first' | 'verified' | 'broken' | 'predecessor-not-loaded';

/**
 * Compares each entry's previous hash with the hash of the entry before it in the loaded list
 * (webui-audit-log-viewer, design D10). The browser does not recompute the hashes.
 * The list must be in ascending sequence order.
 */
export function linkStatuses(entries: readonly AuditEntryView[]): LinkStatus[] {
  return entries.map((entry, index): LinkStatus => {
    if (entry.previousHash === '') {
      return 'first';
    }
    const before = entries[index - 1];
    if (!before) {
      return 'predecessor-not-loaded';
    }
    return before.hash === entry.previousHash ? 'verified' : 'broken';
  });
}

export function actorLabel(actorUserId: string, loginById: ReadonlyMap<string, string>): string {
  if (actorUserId === DATA_LAYER_ACTOR) {
    return 'Data layer';
  }
  return loginById.get(actorUserId) ?? actorUserId;
}
