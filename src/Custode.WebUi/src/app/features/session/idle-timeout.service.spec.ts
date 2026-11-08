// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { LiveAnnouncer } from '@angular/cdk/a11y';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject } from 'rxjs';
import { accessTokenInterceptor } from '../../core/api/access-token.interceptor';
import { SessionChannel } from '../../core/session/session-channel';
import { SessionEndReason, SessionService } from '../../core/session/session.service';
import { IdleTimeoutService } from './idle-timeout.service';

const NOW = Date.parse('2026-11-08T10:00:00Z');
const MINUTE = 60_000;

function encodeSegment(value: object): string {
  return btoa(JSON.stringify(value)).replace(/=+$/, '').replace(/\+/g, '-').replace(/\//g, '_');
}

function issuedToken(): { accessToken: string; expiresAt: string } {
  const token = `${encodeSegment({ alg: 'HS256' })}.${encodeSegment({ sub: 'alice' })}.signature`;
  return { accessToken: token, expiresAt: new Date(Date.now() + 60 * MINUTE).toISOString() };
}

function alertDialog(): Element | null {
  TestBed.inject(ApplicationRef).tick();
  return document.querySelector('[role="alertdialog"]');
}

function interact(type: 'pointerdown' | 'keydown'): void {
  document.dispatchEvent(new Event(type));
}

describe('IdleTimeoutService', () => {
  let channel: { sessionEnded: Subject<SessionEndReason>; postSessionEnded: ReturnType<typeof vi.fn> };
  let http: HttpTestingController;
  let session: SessionService;

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
    session = TestBed.inject(SessionService);
    TestBed.inject(IdleTimeoutService);
  });

  afterEach(() => {
    http.verify();
    vi.useRealTimers();
    document.querySelectorAll('[role="alertdialog"]').forEach((element) => element.remove());
  });

  function signInWithIdleTimeout(minutes: number): void {
    session.signIn('alice', 'secret').subscribe();
    http.expectOne('/api/auth/sign-in').flush(issuedToken());
    http.expectOne('/api/tenant/policies').flush({
      idleTimeoutMinutes: minutes,
      accessTokenLifetimeMinutes: 15,
      refreshTokenLifetimeDays: 7,
      clipboardClearSeconds: 30,
    });
  }

  function flushActivity(status = 204): void {
    for (const request of http.match('/api/auth/activity')) {
      request.flush(null, { status, statusText: status === 204 ? 'No Content' : 'Unauthorized' });
    }
  }

  it('opens the countdown sixty seconds before the idle timeout', () => {
    signInWithIdleTimeout(5);

    vi.advanceTimersByTime(4 * MINUTE - 1_000);
    expect(alertDialog()).toBeNull();

    vi.advanceTimersByTime(1_000);
    expect(alertDialog()).not.toBeNull();
    expect(alertDialog()?.getAttribute('aria-labelledby')).toBe('idle-countdown-title');
    expect(document.activeElement?.textContent?.trim()).toBe('Stay signed in');
  });

  it('ends the session when the countdown reaches zero', () => {
    signInWithIdleTimeout(5);

    vi.advanceTimersByTime(5 * MINUTE);

    expect(session.signedIn()).toBe(false);
    expect(session.endReason()).toBe('idle-timeout');
    expect(alertDialog()).toBeNull();
  });

  it('moves the deadline with each interaction', () => {
    signInWithIdleTimeout(5);
    vi.advanceTimersByTime(3 * MINUTE);
    interact('pointerdown');
    flushActivity();

    vi.advanceTimersByTime(3 * MINUTE);
    expect(alertDialog()).toBeNull();
    expect(session.signedIn()).toBe(true);

    vi.advanceTimersByTime(MINUTE);
    expect(alertDialog()).not.toBeNull();
  });

  it('sends no activity report while the user is idle', () => {
    signInWithIdleTimeout(15);

    vi.advanceTimersByTime(3 * MINUTE);

    expect(http.match('/api/auth/activity')).toHaveLength(0);
  });

  it('reports interaction at most once a minute and reports the last one at the window end', () => {
    signInWithIdleTimeout(15);

    interact('keydown');
    flushActivity();
    vi.advanceTimersByTime(10_000);
    interact('keydown');
    vi.advanceTimersByTime(20_000);
    interact('keydown');
    expect(http.match('/api/auth/activity')).toHaveLength(0);

    vi.advanceTimersByTime(30_000);
    const reports = http.match('/api/auth/activity');
    expect(reports).toHaveLength(1);
    reports[0].flush(null, { status: 204, statusText: 'No Content' });
  });

  it('ends the session when an activity report is refused', () => {
    signInWithIdleTimeout(15);

    interact('pointerdown');
    flushActivity(401);

    expect(session.signedIn()).toBe(false);
    expect(session.endReason()).toBe('activity-refused');
  });

  it('keeps the session when "Stay signed in" is chosen and reports the activity at once', () => {
    signInWithIdleTimeout(5);
    vi.advanceTimersByTime(4 * MINUTE);
    TestBed.inject(ApplicationRef).tick();
    const stay = Array.from(document.querySelectorAll('button')).find(
      (button) => button.textContent?.trim() === 'Stay signed in',
    );
    expect(stay).toBeDefined();

    stay?.click();
    flushActivity();

    expect(alertDialog()).toBeNull();
    vi.advanceTimersByTime(MINUTE);
    expect(session.signedIn()).toBe(true);
  });

  it('announces the remaining time when the countdown opens and every ten seconds after', () => {
    signInWithIdleTimeout(5);
    const announcer = TestBed.inject(LiveAnnouncer);
    const announce = vi.spyOn(announcer, 'announce').mockResolvedValue(undefined);

    vi.advanceTimersByTime(4 * MINUTE);
    expect(announce).toHaveBeenCalledTimes(1);

    vi.advanceTimersByTime(10_000);
    expect(announce).toHaveBeenCalledTimes(2);
  });

  it('recomputes the deadline from the clock after a background period', () => {
    signInWithIdleTimeout(5);

    vi.setSystemTime(Date.now() + 6 * MINUTE);
    document.dispatchEvent(new Event('visibilitychange'));

    expect(session.signedIn()).toBe(false);
    expect(session.endReason()).toBe('idle-timeout');
  });
});
