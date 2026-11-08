// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { Clipboard } from '@angular/cdk/clipboard';
import { LiveAnnouncer } from '@angular/cdk/a11y';
import { Injectable, inject, signal } from '@angular/core';
import { TenantPolicyService } from '../policy/tenant-policy.service';
import { SessionService } from '../session/session.service';

const TICK_MS = 1_000;
const COPY_ATTEMPTS = 3;

/**
 * Places credentials on the clipboard and clears them after the tenant delay. Each copy starts
 * a new generation, so an older timer never clears a newer copy (webui-secret-browser, design D7).
 * The clear time is a timestamp checked against the clock, so a throttled tab still clears on time.
 */
@Injectable({ providedIn: 'root' })
export class ClipboardCopyService {
  private readonly clipboard = inject(Clipboard);
  private readonly announcer = inject(LiveAnnouncer);
  private readonly policy = inject(TenantPolicyService);
  private readonly session = inject(SessionService);

  private generation = 0;
  private clearAt: number | null = null;
  private timer: ReturnType<typeof setInterval> | null = null;

  /** Seconds until the application clears its copy, or null when nothing is pending. */
  readonly secondsUntilClear = signal<number | null>(null);

  constructor() {
    this.session.ended.subscribe(() => this.clearNow());
  }

  /** Copies the text. Returns false when the browser refuses; the clipboard is then left unchanged. */
  copy(text: string): boolean {
    const pending = this.clipboard.beginCopy(text);
    let copied = false;
    for (let attempt = 0; attempt < COPY_ATTEMPTS && !copied; attempt++) {
      copied = pending.copy();
    }
    pending.destroy();
    if (!copied) {
      return false;
    }

    this.generation += 1;
    const delaySeconds = this.policy.policy().clipboardClearSeconds;
    this.clearAt = Date.now() + delaySeconds * 1000;
    this.startTimer();
    void this.announcer.announce(`Copied. The clipboard clears in ${delaySeconds} seconds.`, 'polite');
    return true;
  }

  private startTimer(): void {
    this.stopTimer();
    const generation = this.generation;
    this.tick(generation);
    this.timer = setInterval(() => this.tick(generation), TICK_MS);
  }

  private tick(generation: number): void {
    if (generation !== this.generation) {
      this.stopTimer();
      return;
    }
    if (this.clearAt === null) {
      return;
    }
    const remaining = Math.ceil((this.clearAt - Date.now()) / 1000);
    if (remaining <= 0) {
      this.clearNow();
      return;
    }
    this.secondsUntilClear.set(remaining);
  }

  private clearNow(): void {
    if (this.clearAt === null) {
      return;
    }
    this.stopTimer();
    this.clearAt = null;
    this.secondsUntilClear.set(null);
    void this.clearSystemClipboard();
  }

  private async clearSystemClipboard(): Promise<void> {
    try {
      await navigator.clipboard.writeText('');
    } catch {
      void this.announcer.announce('Could not clear the clipboard.', 'assertive');
    }
  }

  private stopTimer(): void {
    if (this.timer !== null) {
      clearInterval(this.timer);
      this.timer = null;
    }
  }
}
