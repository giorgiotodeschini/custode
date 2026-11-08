// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export interface SearchFolderHit {
  path: string;
}

export interface SearchSecretHit {
  id: string;
  name: string;
  type: string;
  folderPath: string;
}

export interface SearchResult {
  folders: SearchFolderHit[];
  secrets: SearchSecretHit[];
  truncated: boolean;
}

export const EMPTY_SEARCH_RESULT: SearchResult = { folders: [], secrets: [], truncated: false };

/** Name search over folders and secrets. The server returns only what the caller can see. */
@Injectable({ providedIn: 'root' })
export class SearchService {
  private readonly http = inject(HttpClient);

  search(query: string, limit = 10): Observable<SearchResult> {
    return this.http.get<SearchResult>('/api/search', { params: { query, limit } });
  }
}
