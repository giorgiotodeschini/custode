// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject } from 'rxjs';
import { accessTokenInterceptor } from '../api/access-token.interceptor';
import { recordBrowserStorageWrites } from '../../testing/browser-storage-guard';
import { SessionChannel } from './session-channel';
import { SessionEndReason, SessionService } from './session.service';

const NOW = Date.parse('2026-11-08T10:00:00Z');
const LIFETIME_MS = 10 * 60_000;

function encodeSegment(value: object): string {
  return btoa(JSON.stringify(value)).replace(/=+$/, '').replace(/\+/g, '-').replace(/\//g, '_');
}

function accessToken(sessionId: string): string {
  return `${encodeSegment({ alg: 'HS256', typ: 'JWT' })}.${encodeSegment({
    sub: 'user-1',
    tenant_id: 'tenant-1',
    sid: sessionId,
    role: ['TenantAdministrator'],
  })}.signature`;
}

function issuedAt(token: string, now: number): { accessToken: string; expiresAt: string } {
  return { accessToken: token, expiresAt: new Date(now + LIFETIME_MS).toISOString() };
}

describe('SessionService', () => {
  let channel: { sessionEnded: Subject<SessionEndReason>; postSessionEnded: ReturnType<typeof vi.fn> };
  let http: HttpTestingController;

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
  });

  afterEach(() => {
    http.verify();
    vi.useRealTimers();
  });

  function signIn(sessionId = 'session-1'): string {
    const service = TestBed.inject(SessionService);
    const token = accessToken(sessionId);
    service.signIn('alice', 'correct horse battery').subscribe();
    http.expectOne('/api/auth/sign-in').flush(issuedAt(token, Date.now()));
    return token;
  }

  it('keeps the access token in memory and writes nothing to browser storage', () => {
    const recorder = recordBrowserStorageWrites();
    let token = '';
    try {
      token = signIn();
    } finally {
      recorder.restore();
    }

    const service = TestBed.inject(SessionService);
    expect(recorder.writes).toEqual([]);
    expect(service.currentToken()).toBe(token);
    expect(service.signedIn()).toBe(true);
    expect(service.claims()?.tenantId).toBe('tenant-1');
    expect(service.claims()?.roles).toEqual(['TenantAdministrator']);
  });

  it('shares one refresh request between concurrent callers', () => {
    signIn();
    const service = TestBed.inject(SessionService);
    const first = vi.fn();
    const second = vi.fn();

    service.refresh().subscribe(first);
    service.refresh().subscribe(second);

    const requests = http.match('/api/auth/refresh');
    expect(requests).toHaveLength(1);
    expect(requests[0].request.headers.get('X-Custode-Request')).toBe('true');
    requests[0].flush(issuedAt(accessToken('session-1'), Date.now()));
    expect(first).toHaveBeenCalledTimes(1);
    expect(second).toHaveBeenCalledTimes(1);
  });

  it('renews the access token at 80 percent of its lifetime without a request before', () => {
    signIn();
    const service = TestBed.inject(SessionService);

    vi.advanceTimersByTime(LIFETIME_MS * 0.8 - 1_000);
    service.renewIfDue().subscribe();
    expect(http.match('/api/auth/refresh')).toHaveLength(0);

    vi.advanceTimersByTime(1_000);
    const requests = http.match('/api/auth/refresh');
    expect(requests).toHaveLength(1);
    requests[0].flush(issuedAt(accessToken('session-1'), Date.now()));
  });

  it('ends the session when the background renewal is refused', () => {
    signIn();
    const service = TestBed.inject(SessionService);

    vi.advanceTimersByTime(LIFETIME_MS * 0.8);
    http.expectOne('/api/auth/refresh').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(service.signedIn()).toBe(false);
    expect(service.endReason()).toBe('refresh-failed');
    expect(channel.postSessionEnded).toHaveBeenCalledWith('refresh-failed');
  });

  it('ends the local session on sign-out even when the server call fails', () => {
    signIn();
    const service = TestBed.inject(SessionService);

    service.signOut().subscribe();
    http.expectOne('/api/auth/sign-out').flush(null, { status: 500, statusText: 'Server Error' });

    expect(service.signedIn()).toBe(false);
    expect(service.endReason()).toBeNull();
    expect(channel.postSessionEnded).toHaveBeenCalledWith('signed-out');
  });

  it('ends a session that another tab ended, without broadcasting it again', () => {
    signIn();
    const service = TestBed.inject(SessionService);

    channel.sessionEnded.next('idle-timeout');

    expect(service.signedIn()).toBe(false);
    expect(service.endReason()).toBe('idle-timeout');
    expect(channel.postSessionEnded).not.toHaveBeenCalled();
  });

  it('reports no session when the refresh cookie holds none', () => {
    const service = TestBed.inject(SessionService);
    const result = vi.fn();

    service.restore().subscribe(result);
    http.expectOne('/api/auth/refresh').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(result).toHaveBeenCalledWith(false);
    expect(service.signedIn()).toBe(false);
  });
});
