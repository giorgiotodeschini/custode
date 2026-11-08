// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { Clipboard } from '@angular/cdk/clipboard';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject } from 'rxjs';
import { SessionChannel } from '../session/session-channel';
import { SessionEndReason, SessionService } from '../session/session.service';
import { ClipboardCopyService } from './clipboard-copy.service';

describe('ClipboardCopyService generation logic', () => {
  let written: string[];
  let channel: { sessionEnded: Subject<SessionEndReason>; postSessionEnded: ReturnType<typeof vi.fn> };
  let clipboard: { beginCopy: ReturnType<typeof vi.fn>; copy: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    vi.useFakeTimers({ now: Date.parse('2026-11-08T10:00:00Z') });
    written = [];
    channel = { sessionEnded: new Subject<SessionEndReason>(), postSessionEnded: vi.fn() };
    clipboard = {
      beginCopy: vi.fn((text: string) => ({
        copy: () => {
          written.push(text);
          return true;
        },
        destroy: () => undefined,
      })),
      copy: vi.fn(),
    };
    vi.stubGlobal('navigator', { clipboard: { writeText: vi.fn((text: string) => { written.push(`clear:${text}`); return Promise.resolve(); }) } });
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: SessionChannel, useValue: channel },
        { provide: Clipboard, useValue: clipboard },
      ],
    });
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it('clears the copied value after the default delay', () => {
    TestBed.inject(ClipboardCopyService).copy('first-secret');

    vi.advanceTimersByTime(29_000);
    expect(written).toEqual(['first-secret']);

    vi.advanceTimersByTime(1_000);
    expect(written).toEqual(['first-secret', 'clear:']);
  });

  it('never clears a newer copy with the timer of an older copy', () => {
    const service = TestBed.inject(ClipboardCopyService);

    service.copy('first-secret');
    vi.advanceTimersByTime(20_000);
    service.copy('second-secret');

    vi.advanceTimersByTime(15_000);
    expect(written).not.toContain('clear:');

    vi.advanceTimersByTime(15_000);
    expect(written.filter((entry) => entry.startsWith('clear:'))).toEqual(['clear:']);
  });

  it('reports the seconds left as a countdown', () => {
    const service = TestBed.inject(ClipboardCopyService);
    service.copy('first-secret');

    vi.advanceTimersByTime(10_000);

    expect(service.secondsUntilClear()).toBe(20);
  });

  it('does not clear when the browser refuses the copy', () => {
    clipboard.beginCopy.mockReturnValue({ copy: () => false, destroy: () => undefined });
    const service = TestBed.inject(ClipboardCopyService);

    expect(service.copy('x')).toBe(false);
    vi.advanceTimersByTime(60_000);
    expect(written).toEqual([]);
  });

  it('clears at once when the session ends', () => {
    const session = TestBed.inject(SessionService);
    const http = TestBed.inject(HttpTestingController);
    session.signIn('alice', 'secret').subscribe();
    http.expectOne('/api/auth/sign-in').flush({ accessToken: 'h.p.s', expiresAt: new Date(Date.now() + 600_000).toISOString() });
    TestBed.inject(ClipboardCopyService).copy('first-secret');

    session.endSession('signed-out');

    expect(written).toEqual(['first-secret', 'clear:']);
    http.verify();
  });
});
