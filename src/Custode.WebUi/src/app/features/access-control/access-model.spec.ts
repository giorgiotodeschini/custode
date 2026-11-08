// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import type { PrincipalGrant } from './access-api.service';
import { directLevels, inheritedOnly, levelStates, presetFor } from './access-model';

function grant(overrides: Partial<PrincipalGrant>): PrincipalGrant {
  return {
    principalKind: 'group',
    principalId: 'g-1',
    principalName: 'Operators',
    sources: [],
    effectivePermissions: [],
    ...overrides,
  };
}

describe('access levels per folder', () => {
  it('marks an inherited level with the ancestor it comes from', () => {
    const operators = grant({
      sources: [{ folderPath: '/production', permissions: ['read'] }],
      effectivePermissions: ['read'],
    });

    const states = levelStates(operators, '/production/aws');

    expect(states.read).toEqual({ kind: 'inherited', from: '/production' });
    expect(states.write).toEqual({ kind: 'none' });
  });

  it('marks a level set on this folder as direct', () => {
    const operators = grant({
      sources: [{ folderPath: '/production/aws', permissions: ['write'] }],
      effectivePermissions: ['write'],
    });

    expect(levelStates(operators, '/production/aws').write).toEqual({ kind: 'direct' });
    expect(directLevels(operators, '/production/aws')).toEqual(['write']);
  });

  it('prefers a direct level over an inherited one with the same name', () => {
    const operators = grant({
      sources: [
        { folderPath: '/production', permissions: ['read'] },
        { folderPath: '/production/aws', permissions: ['read'] },
      ],
    });

    expect(levelStates(operators, '/production/aws').read).toEqual({ kind: 'direct' });
  });

  it('lists the levels that a broken inheritance would remove', () => {
    const operators = grant({
      sources: [
        { folderPath: '/production', permissions: ['read', 'write'] },
        { folderPath: '/production/aws', permissions: ['delete'] },
      ],
    });

    const lost = inheritedOnly([operators], '/production/aws');

    expect(lost).toHaveLength(1);
    expect(lost[0]?.effectivePermissions).toEqual(['read', 'write']);
  });
});

describe('presets', () => {
  it('recognises the three presets regardless of order', () => {
    expect(presetFor(['create', 'read', 'write'])).toBe('editor');
    expect(presetFor(['read'])).toBe('viewer');
    expect(presetFor(['delete', 'read', 'write', 'create'])).toBe('full');
  });

  it('calls any other combination custom', () => {
    expect(presetFor(['delete'])).toBe('custom');
  });
});
