// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, defer, firstValueFrom, map, of, shareReplay } from 'rxjs';
import { SessionService } from '../session/session.service';

interface FolderResponse {
  path: string;
  inheritsPermissions: boolean;
}

interface CallerPermissionsResponse {
  folderPath: string;
  permissions: string[];
}

/** Upper bound on the folders examined when looking for folders the caller can create in. */
const MAX_FOLDERS_EXAMINED = 200;

/** Lists the visible folders in which the caller holds Create. The tree is scanned once per session. */
@Injectable({ providedIn: 'root' })
export class FolderCatalogService {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);
  private createable: Observable<string[]> | null = null;

  constructor() {
    this.session.ended.subscribe(() => {
      this.createable = null;
    });
  }

  createableFolders(): Observable<string[]> {
    this.createable ??= defer(() => this.scanForCreate()).pipe(shareReplay({ bufferSize: 1, refCount: false }));
    return this.createable;
  }

  canCreateSecret(): Observable<boolean> {
    return this.createableFolders().pipe(map((paths) => paths.length > 0));
  }

  private async scanForCreate(): Promise<string[]> {
    const createable: string[] = [];
    const root = await firstValueFrom(
      this.http
        .get<CallerPermissionsResponse>('/api/access/me/permissions', { params: { folderPath: '/' } })
        .pipe(catchError(() => of({ folderPath: '/', permissions: [] as string[] }))),
    );
    if (root.permissions.includes('create')) {
      createable.push('/');
    }
    const pending = ['/'];
    let examined = 0;
    while (pending.length > 0 && examined < MAX_FOLDERS_EXAMINED) {
      const parent = pending.shift() ?? '/';
      const children = await firstValueFrom(
        this.http.get<FolderResponse[]>('/api/folders', { params: { path: parent } }).pipe(
          catchError(() => of([] as FolderResponse[])),
        ),
      );
      for (const child of children) {
        if (examined >= MAX_FOLDERS_EXAMINED) {
          break;
        }
        examined += 1;
        const permissions = await firstValueFrom(
          this.http
            .get<CallerPermissionsResponse>('/api/access/me/permissions', { params: { folderPath: child.path } })
            .pipe(catchError(() => of({ folderPath: child.path, permissions: [] as string[] }))),
        );
        if (permissions.permissions.includes('create')) {
          createable.push(child.path);
        }
        pending.push(child.path);
      }
    }
    return createable;
  }
}
