// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, catchError, map, of, tap } from 'rxjs';

export interface TenantPolicy {
  idleTimeoutMinutes: number;
  accessTokenLifetimeMinutes: number;
  refreshTokenLifetimeDays: number;
  clipboardClearSeconds: number;
}

/** Used until the tenant policy is loaded, and when it cannot be loaded (webui-session-security). */
export const DEFAULT_TENANT_POLICY: TenantPolicy = {
  idleTimeoutMinutes: 15,
  accessTokenLifetimeMinutes: 15,
  refreshTokenLifetimeDays: 7,
  clipboardClearSeconds: 30,
};

@Injectable({ providedIn: 'root' })
export class TenantPolicyService {
  private readonly http = inject(HttpClient);

  readonly policy = signal<TenantPolicy>(DEFAULT_TENANT_POLICY);

  load(): Observable<TenantPolicy> {
    return this.http.get<TenantPolicy>('/api/tenant/policies').pipe(
      map((loaded) => ({
        idleTimeoutMinutes: loaded.idleTimeoutMinutes,
        accessTokenLifetimeMinutes: loaded.accessTokenLifetimeMinutes,
        refreshTokenLifetimeDays: loaded.refreshTokenLifetimeDays,
        clipboardClearSeconds: loaded.clipboardClearSeconds,
      })),
      tap((loaded) => this.policy.set(loaded)),
      catchError(() => of(this.policy())),
    );
  }
}
