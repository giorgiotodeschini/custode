// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import type { AuditEntryView } from './audit-model';

export interface AuditQuery {
  from?: string | null;
  to?: string | null;
  userId?: string | null;
  action?: string | null;
  outcome?: string | null;
}

export interface AuditPageResult {
  entries: AuditEntryView[];
  nextAfterSequence: number | null;
}

export interface AuditVerification {
  isIntact: boolean;
  entriesVerified: number;
  firstAlteredSequence: number | null;
}

export const AUDIT_PAGE_SIZE = 100;

@Injectable({ providedIn: 'root' })
export class AuditApi {
  private readonly http = inject(HttpClient);

  page(query: AuditQuery, afterSequence: number | null): Observable<AuditPageResult> {
    let params = new HttpParams().set('limit', String(AUDIT_PAGE_SIZE));
    for (const [key, value] of Object.entries(query)) {
      if (value) {
        params = params.set(key, value);
      }
    }
    if (afterSequence !== null) {
      params = params.set('afterSequence', String(afterSequence));
    }
    return this.http.get<AuditPageResult>('/api/audit', { params });
  }

  verify(): Observable<AuditVerification> {
    return this.http.get<AuditVerification>('/api/audit/verify');
  }
}
