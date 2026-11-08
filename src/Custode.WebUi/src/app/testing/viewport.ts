// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { NARROW_VIEWPORT_QUERY } from '../shell/app-shell';

/** A matchMedia replacement that matches the narrow-viewport query only when `narrow` is true. */
export function viewportMatchMedia(narrow: boolean): (query: string) => MediaQueryList {
  return (query: string) =>
    ({
      matches: query === NARROW_VIEWPORT_QUERY ? narrow : false,
      media: query,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
      addListener: () => undefined,
      removeListener: () => undefined,
    }) as unknown as MediaQueryList;
}
