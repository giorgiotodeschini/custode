// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { Overlay, OverlayRef } from '@angular/cdk/overlay';
import { ComponentPortal } from '@angular/cdk/portal';
import { DOCUMENT } from '@angular/common';
import { Injector, inject, Injectable, signal } from '@angular/core';
import { SessionService } from '../../core/session/session.service';
import { CommandPalette } from './command-palette';

/** Opens and closes the command palette and restores focus when it closes. */
@Injectable({ providedIn: 'root' })
export class CommandPaletteService {
  private readonly overlay = inject(Overlay);
  private readonly injector = inject(Injector);
  private readonly document = inject(DOCUMENT);
  private readonly session = inject(SessionService);

  private overlayRef: OverlayRef | null = null;
  private focusBeforeOpen: HTMLElement | null = null;

  readonly isOpen = signal(false);

  constructor() {
    this.document.addEventListener('keydown', (event: KeyboardEvent) => this.onKeydown(event));
  }

  open(): void {
    if (this.overlayRef || !this.session.signedIn()) {
      return;
    }
    this.focusBeforeOpen = this.document.activeElement instanceof HTMLElement ? this.document.activeElement : null;
    const ref = this.overlay.create({
      positionStrategy: this.overlay.position().global().top('12vh').centerHorizontally(),
      hasBackdrop: true,
      backdropClass: 'bg-text/40',
      panelClass: ['w-full', 'max-w-xl', 'px-4'],
      scrollStrategy: this.overlay.scrollStrategies.block(),
    });
    ref.backdropClick().subscribe(() => this.close());
    ref.attach(new ComponentPortal(CommandPalette, null, this.injector));
    this.overlayRef = ref;
    this.isOpen.set(true);
  }

  close(): void {
    if (!this.overlayRef) {
      return;
    }
    this.overlayRef.dispose();
    this.overlayRef = null;
    this.isOpen.set(false);
    this.focusBeforeOpen?.focus();
    this.focusBeforeOpen = null;
  }

  private onKeydown(event: KeyboardEvent): void {
    const shortcut = (event.ctrlKey || event.metaKey) && !event.altKey && !event.shiftKey;
    if (!shortcut || event.key.toLowerCase() !== 'k') {
      return;
    }
    event.preventDefault();
    if (this.isOpen()) {
      this.close();
    } else {
      this.open();
    }
  }
}
