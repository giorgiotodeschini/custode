// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { viewportMatchMedia } from '../testing/viewport';
import { AppShell } from './app-shell';

function createShell(narrow: boolean): HTMLElement {
  vi.stubGlobal('matchMedia', viewportMatchMedia(narrow));
  TestBed.configureTestingModule({
    imports: [AppShell],
    providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
  });
  const fixture = TestBed.createComponent(AppShell);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('AppShell', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('shows the sidebar beside the content on a wide viewport', () => {
    const root = createShell(false);

    expect(root.querySelector('button[aria-controls="app-sidebar"]')).toBeNull();
    expect(root.querySelector('#app-sidebar')?.hasAttribute('inert')).toBe(false);
  });

  it('hides the sidebar behind a menu button on a narrow viewport', () => {
    const root = createShell(true);

    const menu = root.querySelector<HTMLButtonElement>('button[aria-controls="app-sidebar"]');
    expect(menu?.getAttribute('aria-expanded')).toBe('false');
    expect(root.querySelector('#app-sidebar')?.hasAttribute('inert')).toBe(true);
  });
});
