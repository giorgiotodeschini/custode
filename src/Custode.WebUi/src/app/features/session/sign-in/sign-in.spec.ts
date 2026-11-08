// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject } from 'rxjs';
import { SessionChannel } from '../../../core/session/session-channel';
import { SessionEndReason, SessionService } from '../../../core/session/session.service';
import { SignIn } from './sign-in';

describe('SignIn', () => {
  let fixture: ComponentFixture<SignIn>;
  let http: HttpTestingController;
  let session: SessionService;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SignIn],
      providers: [
        provideRouter([{ path: '**', children: [] }]),
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: SessionChannel,
          useValue: { sessionEnded: new Subject<SessionEndReason>(), postSessionEnded: vi.fn() },
        },
      ],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    session = TestBed.inject(SessionService);
    fixture = TestBed.createComponent(SignIn);
    fixture.detectChanges();
  });

  afterEach(() => {
    http.verify();
  });

  function fill(login: string, password: string): void {
    const element = fixture.nativeElement as HTMLElement;
    const loginInput = element.querySelector<HTMLInputElement>('#sign-in-login');
    const passwordInput = element.querySelector<HTMLInputElement>('#sign-in-password');
    if (!loginInput || !passwordInput) {
      throw new Error('sign-in fields are missing');
    }
    loginInput.value = login;
    loginInput.dispatchEvent(new Event('input'));
    passwordInput.value = password;
    passwordInput.dispatchEvent(new Event('input'));
  }

  function submit(): void {
    (fixture.nativeElement as HTMLElement).querySelector('form')?.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
  }

  it('shows the same generic message for a wrong password and an unknown login', () => {
    for (const login of ['alice', 'nobody']) {
      fill(login, 'wrong password');
      submit();
      http.expectOne('/api/auth/sign-in').flush(null, { status: 401, statusText: 'Unauthorized' });
      fixture.detectChanges();

      const alert = (fixture.nativeElement as HTMLElement).querySelector('[role="alert"]');
      expect(alert?.textContent?.trim()).toBe('Invalid login or password');
    }
  });

  it('clears the password after a failed attempt and does not echo it', () => {
    fill('alice', 'wrong password');
    submit();
    http.expectOne('/api/auth/sign-in').flush(null, { status: 401, statusText: 'Unauthorized' });
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector<HTMLInputElement>('#sign-in-password')?.value).toBe('');
    expect(element.textContent).not.toContain('wrong password');
  });

  function endSignedInSession(reason: SessionEndReason): void {
    session.signIn('alice', 'secret').subscribe();
    http.expectOne('/api/auth/sign-in').flush({ accessToken: 'token', expiresAt: new Date(Date.now() + 600_000).toISOString() });
    session.endSession(reason, false);
    fixture.detectChanges();
  }

  it('explains that an idle session ended', () => {
    endSignedInSession('idle-timeout');

    const notice = (fixture.nativeElement as HTMLElement).querySelector('[role="status"]');
    expect(notice?.textContent).toContain('period of inactivity');
  });

  it('explains that a replayed refresh ended the session', () => {
    endSignedInSession('refresh-failed');

    const notice = (fixture.nativeElement as HTMLElement).querySelector('[role="status"]');
    expect(notice?.textContent).toContain('Your session has ended');
  });
});
