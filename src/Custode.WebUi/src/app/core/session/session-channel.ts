// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { DestroyRef, Injectable, inject } from '@angular/core';
import { Observable, Subject } from 'rxjs';
import type { SessionEndReason } from './session.service';

export const SESSION_CHANNEL_NAME = 'custode-session';

interface SessionEndedMessage {
  type: 'session-ended';
  reason: SessionEndReason;
}

/** Shares session end between the tabs of the application in one browser profile. */
@Injectable({ providedIn: 'root' })
export class SessionChannel {
  private readonly ends = new Subject<SessionEndReason>();
  private readonly channel: BroadcastChannel | null =
    typeof BroadcastChannel === 'undefined' ? null : new BroadcastChannel(SESSION_CHANNEL_NAME);

  readonly sessionEnded: Observable<SessionEndReason> = this.ends.asObservable();

  constructor() {
    if (this.channel) {
      this.channel.onmessage = (event: MessageEvent<unknown>) => {
        const reason = readEndReason(event.data);
        if (reason) {
          this.ends.next(reason);
        }
      };
      inject(DestroyRef).onDestroy(() => this.channel?.close());
    }
  }

  postSessionEnded(reason: SessionEndReason): void {
    const message: SessionEndedMessage = { type: 'session-ended', reason };
    this.channel?.postMessage(message);
  }
}

function readEndReason(data: unknown): SessionEndReason | null {
  if (typeof data !== 'object' || data === null) {
    return null;
  }
  const message = data as Partial<SessionEndedMessage>;
  return message.type === 'session-ended' && isSessionEndReason(message.reason) ? message.reason : null;
}

function isSessionEndReason(value: unknown): value is SessionEndReason {
  return value === 'signed-out' || value === 'refresh-failed' || value === 'idle-timeout' || value === 'activity-refused';
}
