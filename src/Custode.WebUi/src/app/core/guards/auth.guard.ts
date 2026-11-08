// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map } from 'rxjs';
import { SessionService } from '../session/session.service';

/** Lets a signed-in user through, restores a session after a reload, and otherwise shows sign-in. */
export const authGuard: CanActivateFn = (_route, state) => {
  const session = inject(SessionService);
  const router = inject(Router);
  if (session.signedIn()) {
    return true;
  }
  return session.restore().pipe(
    map((restored) =>
      restored ? true : router.createUrlTree(['/sign-in'], { queryParams: { returnUrl: state.url } }),
    ),
  );
};
