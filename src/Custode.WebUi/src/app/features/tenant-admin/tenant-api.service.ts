// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import type { PolicyValues } from './policy-model';

export interface CreatedMember {
  id: string;
  login: string;
}

export interface PolicyResult extends PolicyValues {
  accessTokenLifetimeMinutes: number;
}

/** Member and policy endpoints of the tenant. The server checks the role and every range. */
@Injectable({ providedIn: 'root' })
export class TenantApi {
  private readonly http = inject(HttpClient);

  createMember(login: string, initialPassword: string): Observable<CreatedMember> {
    return this.http.post<CreatedMember>('/api/tenant/members', { login, initialPassword });
  }

  setMemberEnabled(id: string, enabled: boolean): Observable<void> {
    return this.http.post<void>(`/api/tenant/members/${id}/${enabled ? 'enable' : 'disable'}`, null);
  }

  setAdministrator(id: string, isAdministrator: boolean): Observable<void> {
    return this.http.put<void>(`/api/tenant/members/${id}/administrator`, { isAdministrator });
  }

  policy(): Observable<PolicyResult> {
    return this.http.get<PolicyResult>('/api/tenant/policies');
  }

  /**
   * The server requires every value, so the access token lifetime is sent with the value it currently has:
   * the form does not change it.
   */
  updatePolicy(values: PolicyValues & { accessTokenLifetimeMinutes: number }): Observable<PolicyResult> {
    return this.http.put<PolicyResult>('/api/tenant/policies', values);
  }
}
