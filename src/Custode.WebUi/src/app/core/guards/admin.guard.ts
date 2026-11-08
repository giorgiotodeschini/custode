// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { NoticeService } from '../notifications/notice.service';
import { isTenantAdministrator } from '../session/access-token';
import { SessionService } from '../session/session.service';

/**
 * Keeps administration screens away from users without the tenant administrator role.
 * The check is a convenience; the server enforces every rule again (webui-shell-navigation).
 */
export const adminGuard: CanActivateFn = () => {
  if (isTenantAdministrator(inject(SessionService).claims())) {
    return true;
  }
  inject(NoticeService).show('This page is not available.');
  return inject(Router).createUrlTree(['/folders']);
};
