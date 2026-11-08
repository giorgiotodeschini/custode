// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

const DEFAULT_RETURN_URL = '/folders';
const PROBE_ORIGIN = 'https://custode.invalid';

/**
 * Returns the return address only when it is a relative path within the application.
 * Anything else (another site, a protocol-relative or scheme URL) is ignored.
 */
export function safeReturnUrl(candidate: string | null | undefined): string {
  if (!candidate || !candidate.startsWith('/') || candidate.startsWith('//') || candidate.includes('\\')) {
    return DEFAULT_RETURN_URL;
  }
  try {
    const resolved = new URL(candidate, PROBE_ORIGIN);
    if (resolved.origin !== PROBE_ORIGIN) {
      return DEFAULT_RETURN_URL;
    }
    return `${resolved.pathname}${resolved.search}${resolved.hash}`;
  } catch {
    return DEFAULT_RETURN_URL;
  }
}
