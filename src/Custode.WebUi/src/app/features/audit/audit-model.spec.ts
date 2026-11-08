// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { AuditEntryView, DATA_LAYER_ACTOR, actorLabel, linkStatuses, localInputToInstant, presetStart } from './audit-model';

function entry(sequence: number, hash: string, previousHash: string): AuditEntryView {
  return {
    sequence,
    occurredAtUtc: '2026-11-08T10:00:00Z',
    actorUserId: 'u-1',
    action: 'secret value read',
    outcome: 'succeeded',
    targetType: 'secret',
    targetId: 's-1',
    secondaryTargetType: null,
    secondaryTargetId: null,
    hash,
    previousHash,
  };
}

describe('chain link status', () => {
  it('verifies a link when the previous hash equals the preceding loaded hash', () => {
    const entries = [entry(1, 'h1', ''), entry(2, 'h2', 'h1'), entry(3, 'h3', 'h2')];

    expect(linkStatuses(entries)).toEqual(['first', 'verified', 'verified']);
  });

  it('marks a broken link where the previous hash does not match', () => {
    const entries = [entry(1, 'h1', ''), entry(2, 'h2', 'x'), entry(3, 'h3', 'h2')];

    expect(linkStatuses(entries)).toEqual(['first', 'broken', 'verified']);
  });

  it('does not judge the first loaded entry when its predecessor is not loaded', () => {
    expect(linkStatuses([entry(5, 'h5', 'h4')])).toEqual(['predecessor-not-loaded']);
  });
});

describe('actors', () => {
  it('labels the data layer actor and never shows it as an unknown user', () => {
    expect(actorLabel(DATA_LAYER_ACTOR, new Map())).toBe('Data layer');
  });

  it('shows the login of a known user and the identifier of an unknown one', () => {
    const logins = new Map([['u-1', 'alice']]);

    expect(actorLabel('u-1', logins)).toBe('alice');
    expect(actorLabel('u-9', logins)).toBe('u-9');
  });
});

describe('timeframes', () => {
  it('computes the start of each preset from the current time', () => {
    const now = new Date('2026-11-08T12:00:00Z');

    expect(presetStart('last-24-hours', now).toISOString()).toBe('2026-11-07T12:00:00.000Z');
    expect(presetStart('last-7-days', now).toISOString()).toBe('2026-11-01T12:00:00.000Z');
  });

  it('treats an empty local input as no bound', () => {
    expect(localInputToInstant('')).toBeNull();
  });
});
