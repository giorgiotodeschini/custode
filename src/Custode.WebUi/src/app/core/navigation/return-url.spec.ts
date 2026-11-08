// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { safeReturnUrl } from './return-url';

describe('safeReturnUrl', () => {
  it('honors a relative path within the application', () => {
    expect(safeReturnUrl('/folders/production')).toBe('/folders/production');
  });

  it('keeps the query and fragment of a relative path', () => {
    expect(safeReturnUrl('/audit?action=read#top')).toBe('/audit?action=read#top');
  });

  it.each([
    'https://evil.example/phish',
    '//evil.example/phish',
    '/\\evil.example',
    'javascript:alert(1)',
    'folders/production',
    '',
  ])('ignores the external or invalid address %s', (candidate) => {
    expect(safeReturnUrl(candidate)).toBe('/folders');
  });

  it('falls back to the folders screen when there is no address', () => {
    expect(safeReturnUrl(null)).toBe('/folders');
    expect(safeReturnUrl(undefined)).toBe('/folders');
  });
});
