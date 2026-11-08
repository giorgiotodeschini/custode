// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { HttpClient, HttpErrorResponse, HttpContext } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Dialog } from '@angular/cdk/dialog';
import { Router } from '@angular/router';
import { Observable, Subject, catchError, finalize, map, of, share, tap, throwError } from 'rxjs';
import { SKIP_SESSION_HANDLING } from '../api/session-http-context';
import { SessionClaims, decodeAccessTokenClaims } from './access-token';
import { SessionChannel } from './session-channel';

export const CUSTODE_REQUEST_HEADER = 'X-Custode-Request';

export type SessionEndReason = 'signed-out' | 'refresh-failed' | 'idle-timeout' | 'activity-refused';

/** The access token is renewed once it has used this share of its lifetime. */
const RENEWAL_FRACTION = 0.8;

interface IssuedToken {
  accessToken: string;
  expiresAt: string;
}

interface HeldToken {
  value: string;
  renewAt: number;
  claims: SessionClaims;
}

@Injectable({ providedIn: 'root' })
export class SessionService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly dialog = inject(Dialog);
  private readonly channel = inject(SessionChannel);

  private readonly held = signal<HeldToken | null>(null);
  private readonly startedEvents = new Subject<void>();
  private readonly endedEvents = new Subject<SessionEndReason>();
  private refreshInFlight: Observable<void> | null = null;
  private renewalTimer: ReturnType<typeof setTimeout> | null = null;

  readonly signedIn = computed(() => this.held() !== null);
  readonly claims = computed(() => this.held()?.claims ?? null);
  /** Why the last session ended. Shown as a notice on the sign-in form; cleared by a sign-in. */
  readonly endReason = signal<SessionEndReason | null>(null);
  readonly started = this.startedEvents.asObservable();
  /** Emits when a session ends, before the sensitive state is cleared by listeners. */
  readonly ended = this.endedEvents.asObservable();

  constructor() {
    this.channel.sessionEnded.subscribe((reason) => this.endSession(reason, false));
  }

  currentToken(): string | null {
    return this.held()?.value ?? null;
  }

  signIn(login: string, password: string): Observable<void> {
    return this.http
      .post<IssuedToken>('/api/auth/sign-in', { login, password })
      .pipe(map((issued) => this.store(issued)));
  }

  /** Renews the access token when it has reached the renewal point. */
  renewIfDue(): Observable<void> {
    const held = this.held();
    if (!held || Date.now() < held.renewAt) {
      return of(undefined);
    }
    return this.refresh().pipe(
      catchError((error: unknown) => {
        if (isUnauthorized(error)) {
          this.endSession('refresh-failed');
        }
        return throwError(() => error);
      }),
    );
  }

  /** Obtains a new access token. Concurrent callers share one request. */
  refresh(): Observable<void> {
    this.refreshInFlight ??= this.http
      .post<IssuedToken>('/api/auth/refresh', null, {
        headers: { [CUSTODE_REQUEST_HEADER]: 'true' },
        withCredentials: true,
        context: new HttpContext().set(SKIP_SESSION_HANDLING, true),
      })
      .pipe(
        tap((issued) => this.store(issued)),
        map(() => undefined),
        finalize(() => {
          this.refreshInFlight = null;
        }),
        share(),
      );
    return this.refreshInFlight;
  }

  /** Restores a session after a page load when the refresh cookie still holds one. */
  restore(): Observable<boolean> {
    if (this.signedIn()) {
      return of(true);
    }
    return this.refresh().pipe(
      map(() => true),
      catchError(() => of(false)),
    );
  }

  signOut(): Observable<void> {
    return this.http
      .post<void>('/api/auth/sign-out', null, {
        headers: { [CUSTODE_REQUEST_HEADER]: 'true' },
        withCredentials: true,
        context: new HttpContext().set(SKIP_SESSION_HANDLING, true),
      })
      .pipe(
        catchError(() => of(undefined)),
        tap(() => this.endSession('signed-out')),
        map(() => undefined),
      );
  }

  /** Clears the in-memory session and notifies the listeners and the other tabs. */
  endSession(reason: SessionEndReason, broadcast = true): void {
    if (!this.held()) {
      return;
    }
    this.held.set(null);
    this.clearRenewalTimer();
    this.dialog.closeAll();
    this.endReason.set(reason === 'signed-out' ? null : reason);
    this.endedEvents.next(reason);
    if (broadcast) {
      this.channel.postSessionEnded(reason);
    }
    this.navigateToSignIn(reason);
  }

  private store(issued: IssuedToken): void {
    const receivedAt = Date.now();
    const expiresAt = Date.parse(issued.expiresAt);
    const wasSignedIn = this.held() !== null;
    this.held.set({
      value: issued.accessToken,
      renewAt: receivedAt + RENEWAL_FRACTION * (expiresAt - receivedAt),
      claims: decodeAccessTokenClaims(issued.accessToken),
    });
    this.endReason.set(null);
    this.scheduleRenewal();
    if (!wasSignedIn) {
      this.startedEvents.next();
    }
  }

  private scheduleRenewal(): void {
    this.clearRenewalTimer();
    const held = this.held();
    if (!held) {
      return;
    }
    this.renewalTimer = setTimeout(() => {
      this.renewalTimer = null;
      this.renewIfDue().subscribe({ error: () => undefined });
    }, Math.max(0, held.renewAt - Date.now()));
  }

  private clearRenewalTimer(): void {
    if (this.renewalTimer !== null) {
      clearTimeout(this.renewalTimer);
      this.renewalTimer = null;
    }
  }

  private navigateToSignIn(reason: SessionEndReason): void {
    const current = this.router.url;
    if (current.startsWith('/sign-in')) {
      return;
    }
    const queryParams = reason === 'signed-out' ? {} : { returnUrl: current };
    void this.router.navigate(['/sign-in'], { queryParams });
  }
}

function isUnauthorized(error: unknown): boolean {
  return error instanceof HttpErrorResponse && error.status === 401;
}
