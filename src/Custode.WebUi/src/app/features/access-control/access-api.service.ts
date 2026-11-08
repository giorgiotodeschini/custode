// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export type PrincipalKind = 'user' | 'group';
export type PermissionLevel = 'read' | 'write' | 'create' | 'delete';

export interface GrantSource {
  folderPath: string;
  permissions: PermissionLevel[];
}

/** A principal with a grant on the folder, directly or through an ancestor. */
export interface PrincipalGrant {
  principalKind: PrincipalKind;
  principalId: string;
  principalName: string;
  sources: GrantSource[];
  effectivePermissions: PermissionLevel[];
}

export interface FolderInheritance {
  path: string;
  parentPath: string | null;
  inheritsPermissions: boolean;
}

export interface GroupSummary {
  id: string;
  name: string;
  memberCount: number;
  grantCount: number;
}

export interface GroupMember {
  id: string;
  login: string;
}

export interface TenantMember {
  id: string;
  login: string;
  roles: string[];
  groupCount: number;
  isEnabled: boolean;
  mustChangePassword: boolean;
}

/** Calls to the access-control endpoints. The server decides every change and every refusal. */
@Injectable({ providedIn: 'root' })
export class AccessApi {
  private readonly http = inject(HttpClient);

  grantsOf(folderPath: string): Observable<PrincipalGrant[]> {
    return this.http.get<PrincipalGrant[]>('/api/access/grants', { params: { folderPath } });
  }

  folders(): Observable<FolderInheritance[]> {
    return this.http.get<FolderInheritance[]>('/api/access/folders');
  }

  groups(): Observable<GroupSummary[]> {
    return this.http.get<GroupSummary[]>('/api/access/groups');
  }

  createGroup(name: string): Observable<GroupSummary> {
    return this.http.post<GroupSummary>('/api/access/groups', { name });
  }

  renameGroup(id: string, name: string): Observable<void> {
    return this.http.put<void>(`/api/access/groups/${id}`, { name });
  }

  deleteGroup(id: string): Observable<void> {
    return this.http.delete<void>(`/api/access/groups/${id}`);
  }

  groupMembers(id: string): Observable<GroupMember[]> {
    return this.http.get<GroupMember[]>(`/api/access/groups/${id}/members`);
  }

  addGroupMember(groupId: string, userId: string): Observable<void> {
    return this.http.post<void>(`/api/access/groups/${groupId}/members`, { userId });
  }

  removeGroupMember(groupId: string, userId: string): Observable<void> {
    return this.http.delete<void>(`/api/access/groups/${groupId}/members/${userId}`);
  }

  members(): Observable<TenantMember[]> {
    return this.http.get<TenantMember[]>('/api/tenant/members');
  }

  setGrant(folderPath: string, principalKind: PrincipalKind, principalId: string, permissions: PermissionLevel[]): Observable<void> {
    return this.http.put<void>('/api/access/grants', { folderPath, principalKind, principalId, permissions });
  }

  revokeGrant(folderPath: string, principalKind: PrincipalKind, principalId: string): Observable<void> {
    return this.http.delete<void>('/api/access/grants', { params: { folderPath, principalKind, principalId } });
  }

  breakInheritance(folderPath: string): Observable<void> {
    return this.http.post<void>('/api/access/inheritance/break', null, { params: { folderPath } });
  }

  restoreInheritance(folderPath: string): Observable<void> {
    return this.http.post<void>('/api/access/inheritance/restore', null, { params: { folderPath } });
  }
}
