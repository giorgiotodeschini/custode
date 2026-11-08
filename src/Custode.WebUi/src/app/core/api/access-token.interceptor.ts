// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { HttpErrorResponse, HttpEvent, HttpHandlerFn, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { Observable, catchError, switchMap, throwError } from 'rxjs';
import { SessionService } from '../session/session.service';
import { SKIP_SESSION_HANDLING } from './session-http-context';

const ANONYMOUS_API_PATHS: ReadonlySet<string> = new Set(['/api/auth/sign-in', '/api/auth/refresh']);

/**
 * Attaches the in-memory access token to API calls, renews it when due, and retries an
 * unauthorized call once after a renewal. The token is never written to browser storage.
 */
export const accessTokenInterceptor: HttpInterceptorFn = (req, next) => {
  if (!isSessionRequest(req)) {
    return next(req);
  }

  const session = inject(SessionService);
  if (req.context.get(SKIP_SESSION_HANDLING)) {
    return next(withAccessToken(req, session.currentToken()));
  }

  return session.renewIfDue().pipe(
    switchMap(() => sendWithRetry(req, next, session)),
  );
};

function sendWithRetry(
  req: HttpRequest<unknown>,
  next: HttpHandlerFn,
  session: SessionService,
): Observable<HttpEvent<unknown>> {
  return next(withAccessToken(req, session.currentToken())).pipe(
    catchError((error: unknown) => {
      if (!isUnauthorized(error)) {
        return throwError(() => error);
      }
      return session.refresh().pipe(
        catchError(() => {
          session.endSession('refresh-failed');
          return throwError(() => error);
        }),
        switchMap(() => next(withAccessToken(req, session.currentToken()))),
      );
    }),
  );
}

function isSessionRequest(req: HttpRequest<unknown>): boolean {
  return req.url.startsWith('/api/') && !ANONYMOUS_API_PATHS.has(req.url);
}

function withAccessToken(req: HttpRequest<unknown>, token: string | null): HttpRequest<unknown> {
  return token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;
}

function isUnauthorized(error: unknown): boolean {
  return error instanceof HttpErrorResponse && error.status === 401;
}
