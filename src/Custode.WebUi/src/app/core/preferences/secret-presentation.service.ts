// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { BreakpointObserver } from '@angular/cdk/layout';
import { Injectable, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs';
import { NARROW_VIEWPORT_QUERY } from '../../shell/app-shell';
import { SessionService } from '../session/session.service';

export type SecretPresentation = 'table' | 'cards';

const KEY_PREFIX = 'custode.secrets.presentation.';

/**
 * The list presentation chosen by a user, stored per account on this browser. The default is the
 * table on desktop and the cards on mobile (webui-secret-browser).
 */
@Injectable({ providedIn: 'root' })
export class SecretPresentationService {
  private readonly session = inject(SessionService);
  private readonly narrow = toSignal(
    inject(BreakpointObserver)
      .observe(NARROW_VIEWPORT_QUERY)
      .pipe(map((state) => state.matches)),
    { initialValue: false },
  );

  private readonly choice = signal<SecretPresentation | null>(null);

  readonly presentation = computed<SecretPresentation>(() => this.choice() ?? (this.narrow() ? 'cards' : 'table'));

  constructor() {
    if (this.session.signedIn()) {
      this.choice.set(this.readStored());
    }
    this.session.started.subscribe(() => this.choice.set(this.readStored()));
    this.session.ended.subscribe(() => this.choice.set(null));
  }

  choose(presentation: SecretPresentation): void {
    const key = this.storageKey();
    if (key) {
      try {
        localStorage.setItem(key, presentation);
      } catch {
        // Storage may be unavailable; the choice then lasts for this page only.
      }
    }
    this.choice.set(presentation);
  }

  private readStored(): SecretPresentation | null {
    const key = this.storageKey();
    if (!key) {
      return null;
    }
    try {
      const stored = localStorage.getItem(key);
      return stored === 'table' || stored === 'cards' ? stored : null;
    } catch {
      return null;
    }
  }

  private storageKey(): string | null {
    const subject = this.session.claims()?.subject;
    return subject ? `${KEY_PREFIX}${subject}` : null;
  }
}
