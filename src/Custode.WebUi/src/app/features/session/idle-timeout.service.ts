// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { LiveAnnouncer } from '@angular/cdk/a11y';
import { Dialog, DialogRef } from '@angular/cdk/dialog';
import { DOCUMENT } from '@angular/common';
import { HttpClient, HttpContext, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { SKIP_SESSION_HANDLING } from '../../core/api/session-http-context';
import { TenantPolicyService } from '../../core/policy/tenant-policy.service';
import { CUSTODE_REQUEST_HEADER, SessionService } from '../../core/session/session.service';
import { IdleCountdown } from './idle-countdown/idle-countdown';

const COUNTDOWN_MS = 60_000;
const ANNOUNCEMENT_INTERVAL_MS = 10_000;
const ACTIVITY_REPORT_INTERVAL_MS = 60_000;
const INTERACTION_THROTTLE_MS = 1_000;
const TICK_MS = 1_000;
const INTERACTION_EVENTS = ['pointerdown', 'keydown', 'touchstart'] as const;

/**
 * Ends the session after the tenant idle timeout. The deadline is a timestamp checked against
 * the clock, so a background tab that resumes sees the true time (webui-session-security).
 */
@Injectable({ providedIn: 'root' })
export class IdleTimeoutService {
  private readonly session = inject(SessionService);
  private readonly policy = inject(TenantPolicyService);
  private readonly http = inject(HttpClient);
  private readonly dialog = inject(Dialog);
  private readonly announcer = inject(LiveAnnouncer);
  private readonly document = inject(DOCUMENT);

  readonly secondsLeft = signal(0);

  private deadline = 0;
  private lastInteractionAt = 0;
  private lastReportAt = 0;
  private activityTimer: ReturnType<typeof setTimeout> | null = null;
  private tickTimer: ReturnType<typeof setInterval> | null = null;
  private countdown: DialogRef<unknown, IdleCountdown> | null = null;
  private lastAnnouncementAt = 0;
  private readonly onInteraction = (): void => this.recordInteraction();
  private readonly onVisibilityChange = (): void => this.tick();

  constructor() {
    if (this.session.signedIn()) {
      this.start();
    }
    this.session.started.subscribe(() => this.start());
    this.session.ended.subscribe(() => this.stop());
  }

  /** Called from the countdown: resets the timer and reports the activity at once. */
  staySignedIn(): void {
    this.closeCountdown();
    this.extendDeadline(Date.now());
    this.reportActivity(true);
  }

  private start(): void {
    this.stop();
    this.extendDeadline(Date.now());
    this.policy.load().subscribe(() => this.extendDeadline(Date.now()));
    for (const event of INTERACTION_EVENTS) {
      this.document.addEventListener(event, this.onInteraction, { passive: true });
    }
    this.document.addEventListener('visibilitychange', this.onVisibilityChange);
    this.tickTimer = setInterval(() => this.tick(), TICK_MS);
  }

  private stop(): void {
    for (const event of INTERACTION_EVENTS) {
      this.document.removeEventListener(event, this.onInteraction);
    }
    this.document.removeEventListener('visibilitychange', this.onVisibilityChange);
    if (this.tickTimer !== null) {
      clearInterval(this.tickTimer);
      this.tickTimer = null;
    }
    if (this.activityTimer !== null) {
      clearTimeout(this.activityTimer);
      this.activityTimer = null;
    }
    this.closeCountdown();
    this.secondsLeft.set(0);
  }

  private extendDeadline(now: number): void {
    this.lastInteractionAt = now;
    this.deadline = now + this.policy.policy().idleTimeoutMinutes * 60_000;
  }

  private recordInteraction(): void {
    if (!this.session.signedIn() || this.countdown) {
      return;
    }
    const now = Date.now();
    if (now - this.lastInteractionAt >= INTERACTION_THROTTLE_MS) {
      this.extendDeadline(now);
    }
    this.reportActivity(false);
  }

  private tick(): void {
    if (!this.session.signedIn()) {
      return;
    }
    const now = Date.now();
    const remaining = this.deadline - now;
    if (remaining <= 0) {
      this.session.endSession('idle-timeout');
      return;
    }
    if (remaining > COUNTDOWN_MS) {
      return;
    }
    this.openCountdown();
    this.secondsLeft.set(Math.ceil(remaining / 1000));
    if (now - this.lastAnnouncementAt >= ANNOUNCEMENT_INTERVAL_MS) {
      this.lastAnnouncementAt = now;
      void this.announcer.announce(
        `Session ends in ${this.secondsLeft()} seconds. Choose Stay signed in to continue.`,
        'assertive',
      );
    }
  }

  private openCountdown(): void {
    if (this.countdown) {
      return;
    }
    this.countdown = this.dialog.open<unknown, unknown, IdleCountdown>(IdleCountdown, {
      role: 'alertdialog',
      disableClose: true,
      autoFocus: false,
      ariaLabelledBy: 'idle-countdown-title',
      backdropClass: ['bg-text/40'],
    });
    this.countdown.closed.subscribe(() => {
      this.countdown = null;
    });
  }

  private closeCountdown(): void {
    this.countdown?.close();
    this.countdown = null;
    this.lastAnnouncementAt = 0;
  }

  /**
   * Reports interaction at most once per minute. An interaction inside the window is reported
   * when the window ends, so the server never sees an older last activity than the user's.
   */
  private reportActivity(force: boolean): void {
    if (!this.session.signedIn()) {
      return;
    }
    const now = Date.now();
    const due = this.lastReportAt + ACTIVITY_REPORT_INTERVAL_MS;
    if (force || now >= due) {
      this.sendActivity();
      return;
    }
    this.activityTimer ??= setTimeout(() => {
      this.activityTimer = null;
      this.sendActivity();
    }, due - now);
  }

  private sendActivity(): void {
    if (!this.session.signedIn()) {
      return;
    }
    this.lastReportAt = Date.now();
    this.http
      .post<void>('/api/auth/activity', null, {
        headers: { [CUSTODE_REQUEST_HEADER]: 'true' },
        context: new HttpContext().set(SKIP_SESSION_HANDLING, true),
      })
      .subscribe({
        error: (error: unknown) => {
          if (error instanceof HttpErrorResponse && error.status === 401) {
            this.session.endSession('activity-refused');
          }
        },
      });
  }
}
