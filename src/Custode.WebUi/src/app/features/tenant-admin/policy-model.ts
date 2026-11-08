// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

export interface PolicyValues {
  idleTimeoutMinutes: number;
  refreshTokenLifetimeDays: number;
  clipboardClearSeconds: number;
}

export type PolicyField = keyof PolicyValues;

/** The ranges of webui-tenant-administration. The server enforces the same ranges. */
export const POLICY_RANGES: Record<PolicyField, { min: number; max: number; label: string; unit: string }> = {
  idleTimeoutMinutes: { min: 5, max: 120, label: 'Idle timeout', unit: 'minutes' },
  refreshTokenLifetimeDays: { min: 1, max: 30, label: 'Refresh token lifetime', unit: 'days' },
  clipboardClearSeconds: { min: 10, max: 120, label: 'Clipboard clear delay', unit: 'seconds' },
};

export const POLICY_FIELDS: readonly PolicyField[] = ['idleTimeoutMinutes', 'refreshTokenLifetimeDays', 'clipboardClearSeconds'];

/** Checks each value against its range. Non-integers and empty values are rejected too. */
export function validatePolicy(values: Partial<Record<PolicyField, number | null>>): Partial<Record<PolicyField, string>> {
  const errors: Partial<Record<PolicyField, string>> = {};
  for (const field of POLICY_FIELDS) {
    const range = POLICY_RANGES[field];
    const value = values[field];
    if (value === null || value === undefined || Number.isNaN(value)) {
      errors[field] = `Enter a whole number of ${range.unit}.`;
    } else if (!Number.isInteger(value) || value < range.min || value > range.max) {
      errors[field] = `${range.label} must be between ${range.min} and ${range.max} ${range.unit}.`;
    }
  }
  return errors;
}

/** Describes what the change does to the tenant, before it is saved. */
export function policyEffects(current: PolicyValues, next: PolicyValues): string[] {
  const effects: string[] = [];
  if (next.idleTimeoutMinutes < current.idleTimeoutMinutes) {
    effects.push(`Users idle for more than ${next.idleTimeoutMinutes} minutes will be signed out, down from ${current.idleTimeoutMinutes} minutes.`);
  } else if (next.idleTimeoutMinutes > current.idleTimeoutMinutes) {
    effects.push(`Idle sessions will last ${next.idleTimeoutMinutes} minutes, up from ${current.idleTimeoutMinutes} minutes.`);
  }
  if (next.refreshTokenLifetimeDays < current.refreshTokenLifetimeDays) {
    effects.push(`Sessions older than ${next.refreshTokenLifetimeDays} days will end at their next refresh. Sessions that are still within the limit are not affected.`);
  } else if (next.refreshTokenLifetimeDays > current.refreshTokenLifetimeDays) {
    effects.push(`Sessions can last ${next.refreshTokenLifetimeDays} days, up from ${current.refreshTokenLifetimeDays} days.`);
  }
  if (next.clipboardClearSeconds !== current.clipboardClearSeconds) {
    effects.push(`Copied credentials are cleared from the clipboard after ${next.clipboardClearSeconds} seconds, instead of ${current.clipboardClearSeconds}.`);
  }
  return effects;
}

export function changedFields(current: PolicyValues, next: PolicyValues): PolicyField[] {
  return POLICY_FIELDS.filter((field) => current[field] !== next[field]);
}
