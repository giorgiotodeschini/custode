// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { HttpClient, HttpContext, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject } from 'rxjs';
import { SessionChannel } from '../session/session-channel';
import { SessionEndReason, SessionService } from '../session/session.service';
import { accessTokenInterceptor } from './access-token.interceptor';
import { SKIP_SESSION_HANDLING } from './session-http-context';

const NOW = Date.parse('2026-11-08T10:00:00Z');
const LIFETIME_MS = 10 * 60_000;

function encodeSegment(value: object): string {
  return btoa(JSON.stringify(value)).replace(/=+$/, '').replace(/\+/g, '-').replace(/\//g, '_');
}

function accessToken(name: string): string {
  return `${encodeSegment({ alg: 'HS256', typ: 'JWT' })}.${encodeSegment({ sub: name })}.signature`;
}

function issued(token: string): { accessToken: string; expiresAt: string } {
  return { accessToken: token, expiresAt: new Date(Date.now() + LIFETIME_MS).toISOString() };
}

describe('accessTokenInterceptor', () => {
  let channel: { sessionEnded: Subject<SessionEndReason>; postSessionEnded: ReturnType<typeof vi.fn> };
  let http: HttpTestingController;
  let client: HttpClient;

  beforeEach(() => {
    vi.useFakeTimers({ now: NOW });
    channel = { sessionEnded: new Subject<SessionEndReason>(), postSessionEnded: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: '**', children: [] }]),
        provideHttpClient(withInterceptors([accessTokenInterceptor])),
        provideHttpClientTesting(),
        { provide: SessionChannel, useValue: channel },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    client = TestBed.inject(HttpClient);
  });

  afterEach(() => {
    http.verify();
    vi.useRealTimers();
  });

  function signIn(token: string): void {
    TestBed.inject(SessionService).signIn('alice', 'secret').subscribe();
    http.expectOne('/api/auth/sign-in').flush(issued(token));
  }

  it('attaches the in-memory access token to API calls', () => {
    const token = accessToken('first');
    signIn(token);

    client.get('/api/folders').subscribe();

    const request = http.expectOne('/api/folders');
    expect(request.request.headers.get('Authorization')).toBe(`Bearer ${token}`);
    request.flush([]);
  });

  it('does not attach a token to the sign-in request', () => {
    TestBed.inject(SessionService).signIn('alice', 'secret').subscribe();

    const request = http.expectOne('/api/auth/sign-in');
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush(issued(accessToken('first')));
  });

  it('retries an unauthorized call once with a renewed token', () => {
    signIn(accessToken('first'));
    const renewed = accessToken('renewed');
    const response = vi.fn();

    client.get('/api/folders').subscribe(response);
    http.expectOne('/api/folders').flush(null, { status: 401, statusText: 'Unauthorized' });
    http.expectOne('/api/auth/refresh').flush(issued(renewed));
    const retry = http.expectOne('/api/folders');
    expect(retry.request.headers.get('Authorization')).toBe(`Bearer ${renewed}`);
    retry.flush([{ name: 'production' }]);

    expect(response).toHaveBeenCalledWith([{ name: 'production' }]);
  });

  it('ends the session and reports the original error when the renewal is refused', () => {
    signIn(accessToken('first'));
    const failure = vi.fn();

    client.get('/api/folders').subscribe({ error: failure });
    http.expectOne('/api/folders').flush(null, { status: 401, statusText: 'Unauthorized' });
    http.expectOne('/api/auth/refresh').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(failure).toHaveBeenCalledWith(expect.objectContaining({ status: 401 }));
    expect(TestBed.inject(SessionService).signedIn()).toBe(false);
    expect(channel.postSessionEnded).toHaveBeenCalledWith('refresh-failed');
  });

  it('does not renew or retry a request that opts out of session handling', () => {
    signIn(accessToken('first'));
    const failure = vi.fn();
    const context = new HttpContext().set(SKIP_SESSION_HANDLING, true);

    client.post('/api/auth/activity', null, { context }).subscribe({ error: failure });
    http.expectOne('/api/auth/activity').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(failure).toHaveBeenCalledWith(expect.objectContaining({ status: 401 }));
    expect(http.match('/api/auth/refresh')).toHaveLength(0);
  });
});
