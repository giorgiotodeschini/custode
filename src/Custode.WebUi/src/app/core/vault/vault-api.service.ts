// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { HttpClient, HttpHeaders, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';

export type SecretTypeName = 'username-password' | 'pat' | 'api-key' | 'file' | 'structured' | 'key-value';

/** Metadata only. Listings and the detail panel never carry a secret value. */
export interface SecretMetadata {
  id: string;
  name: string;
  type: SecretTypeName;
  folderPath: string;
  fileName: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  version: number;
}

export interface SecretPair {
  key: string;
  value: string;
}

export interface SecretValue {
  id: string;
  name: string;
  type: SecretTypeName;
  folderPath: string;
  fileName: string | null;
  username: string | null;
  password: string | null;
  value: string | null;
  contentBase64: string | null;
  fields: SecretPair[] | null;
  pairs: SecretPair[] | null;
  version: number;
}

/** The value together with the version token the server attached to it. */
export interface VersionedValue {
  value: SecretValue;
  etag: string;
}

export interface SecretVersion {
  number: number;
  name: string;
  fileName: string | null;
  changeKind: string;
  createdAtUtc: string;
  createdByUserId: string;
  sourceVersion: number | null;
}

export interface SecretWriteBody {
  name: string;
  type: SecretTypeName;
  username?: string;
  password?: string;
  value?: string;
  fileName?: string;
  contentBase64?: string;
  fields?: Record<string, string>;
  pairs?: SecretPair[];
}

export interface CreateSecretBody extends SecretWriteBody {
  folderPath: string;
}

export interface FolderEntry {
  path: string;
  inheritsPermissions: boolean;
}

export interface CallerPermissions {
  folderPath: string;
  permissions: string[];
}

/** Calls to the existing secret and folder endpoints. The server decides every permission. */
@Injectable({ providedIn: 'root' })
export class VaultApi {
  private readonly http = inject(HttpClient);

  listFolders(path: string): Observable<FolderEntry[]> {
    return this.http.get<FolderEntry[]>('/api/folders', { params: { path } });
  }

  createFolder(path: string): Observable<void> {
    return this.http.post<void>('/api/folders', { path });
  }

  deleteFolder(path: string): Observable<void> {
    return this.http.delete<void>('/api/folders', { params: { path } });
  }

  listSecrets(folderPath: string): Observable<SecretMetadata[]> {
    return this.http.get<SecretMetadata[]>('/api/secrets', { params: { folderPath } });
  }

  /** Reads a value on demand. Used only by a copy action; the value is never stored in component state. */
  readValue(id: string): Observable<SecretValue> {
    return this.http.get<SecretValue>(`/api/secrets/${id}/value`);
  }

  /** Reads the value for the editor, keeping the version token the server sends with it. */
  readVersionedValue(id: string): Observable<VersionedValue> {
    return this.http.get<SecretValue>(`/api/secrets/${id}/value`, { observe: 'response' }).pipe(
      map((response: HttpResponse<SecretValue>) => ({
        value: response.body as SecretValue,
        etag: response.headers.get('ETag') ?? '',
      })),
    );
  }

  deleteSecret(id: string): Observable<void> {
    return this.http.delete<void>(`/api/secrets/${id}`);
  }

  moveSecret(id: string, folderPath: string): Observable<SecretMetadata> {
    return this.http.post<SecretMetadata>(`/api/secrets/${id}/move`, { folderPath });
  }

  createSecret(body: CreateSecretBody): Observable<SecretMetadata> {
    return this.http.post<SecretMetadata>('/api/secrets', body);
  }

  /** Saves a change. The version token of the loaded secret goes in `If-Match`. */
  updateSecret(id: string, body: SecretWriteBody, etag: string): Observable<HttpResponse<SecretMetadata>> {
    return this.http.put<SecretMetadata>(`/api/secrets/${id}`, body, {
      headers: new HttpHeaders({ 'If-Match': etag }),
      observe: 'response',
    });
  }

  listVersions(id: string): Observable<SecretVersion[]> {
    return this.http.get<SecretVersion[]>(`/api/secrets/${id}/versions`);
  }

  readVersionValue(id: string, number: number): Observable<SecretValue> {
    return this.http.get<SecretValue>(`/api/secrets/${id}/versions/${number}/value`);
  }

  rollback(id: string, number: number, etag: string): Observable<HttpResponse<SecretMetadata>> {
    return this.http.post<SecretMetadata>(`/api/secrets/${id}/versions/${number}/rollback`, null, {
      headers: new HttpHeaders({ 'If-Match': etag }),
      observe: 'response',
    });
  }

  callerPermissions(folderPath: string): Observable<CallerPermissions> {
    return this.http.get<CallerPermissions>('/api/access/me/permissions', { params: { folderPath } });
  }
}
