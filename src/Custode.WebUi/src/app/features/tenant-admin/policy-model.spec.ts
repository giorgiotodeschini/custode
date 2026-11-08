// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { changedFields, policyEffects, validatePolicy } from './policy-model';

const CURRENT = { idleTimeoutMinutes: 30, refreshTokenLifetimeDays: 7, clipboardClearSeconds: 30 };

describe('policy validation', () => {
  it('accepts the boundaries of each range', () => {
    expect(validatePolicy({ idleTimeoutMinutes: 5, refreshTokenLifetimeDays: 30, clipboardClearSeconds: 10 })).toEqual({});
    expect(validatePolicy({ idleTimeoutMinutes: 120, refreshTokenLifetimeDays: 1, clipboardClearSeconds: 120 })).toEqual({});
  });

  it('rejects a value outside its range and names the field', () => {
    const errors = validatePolicy({ idleTimeoutMinutes: 2, refreshTokenLifetimeDays: 7, clipboardClearSeconds: 30 });

    expect(errors).toEqual({ idleTimeoutMinutes: 'Idle timeout must be between 5 and 120 minutes.' });
  });

  it('rejects an empty or fractional value', () => {
    expect(validatePolicy({ idleTimeoutMinutes: null, refreshTokenLifetimeDays: 7, clipboardClearSeconds: 30 }).idleTimeoutMinutes).toBeDefined();
    expect(validatePolicy({ idleTimeoutMinutes: 10.5, refreshTokenLifetimeDays: 7, clipboardClearSeconds: 30 }).idleTimeoutMinutes).toBeDefined();
  });
});

describe('policy effects', () => {
  it('states who will be signed out when the idle timeout is shortened', () => {
    const effects = policyEffects({ ...CURRENT, idleTimeoutMinutes: 30 }, { ...CURRENT, idleTimeoutMinutes: 10 });

    expect(effects).toEqual([
      'Users idle for more than 10 minutes will be signed out, down from 30 minutes.',
    ]);
  });

  it('reports no effect when nothing changed', () => {
    expect(policyEffects(CURRENT, { ...CURRENT })).toEqual([]);
    expect(changedFields(CURRENT, { ...CURRENT })).toEqual([]);
  });

  it('lists only the fields that changed', () => {
    expect(changedFields(CURRENT, { ...CURRENT, clipboardClearSeconds: 60 })).toEqual(['clipboardClearSeconds']);
  });
});
