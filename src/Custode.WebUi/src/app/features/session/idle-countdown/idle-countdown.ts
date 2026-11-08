// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { Component, ElementRef, Injector, afterNextRender, inject, viewChild } from '@angular/core';
import { SessionService } from '../../../core/session/session.service';
import { IdleTimeoutService } from '../idle-timeout.service';

@Component({
  selector: 'app-idle-countdown',
  templateUrl: './idle-countdown.html',
})
export class IdleCountdown {
  private readonly idle = inject(IdleTimeoutService);
  private readonly session = inject(SessionService);
  private readonly injector = inject(Injector);
  private readonly staySignedInButton = viewChild.required<ElementRef<HTMLButtonElement>>('stayButton');

  protected readonly secondsLeft = this.idle.secondsLeft;

  constructor() {
    afterNextRender(() => this.staySignedInButton().nativeElement.focus(), { injector: this.injector });
  }

  protected staySignedIn(): void {
    this.idle.staySignedIn();
  }

  protected signOut(): void {
    this.session.signOut().subscribe();
  }
}
