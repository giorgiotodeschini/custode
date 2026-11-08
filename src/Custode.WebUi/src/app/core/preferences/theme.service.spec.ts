// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { TestBed } from '@angular/core/testing';
import { isAllowedStorageKey, recordBrowserStorageWrites } from '../../testing/browser-storage-guard';
import { THEME_STORAGE_KEY, ThemeService } from './theme.service';

function stubSystemDark(dark: boolean): { set(value: boolean): void } {
  let matches = dark;
  const listeners: Array<(event: { matches: boolean }) => void> = [];
  vi.stubGlobal('matchMedia', () => ({
    get matches() {
      return matches;
    },
    addEventListener: (_type: string, listener: (event: { matches: boolean }) => void) => listeners.push(listener),
    removeEventListener: vi.fn(),
  }));
  return {
    set(value: boolean) {
      matches = value;
      listeners.forEach((listener) => listener({ matches: value }));
    },
  };
}

function createService(): ThemeService {
  TestBed.resetTestingModule();
  return TestBed.inject(ThemeService);
}

describe('ThemeService', () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.classList.remove('dark');
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    localStorage.clear();
    document.documentElement.classList.remove('dark');
  });

  it('follows a dark operating color scheme when no theme was chosen', () => {
    stubSystemDark(true);
    const service = createService();

    expect(service.preference()).toBe('system');
    expect(service.resolved()).toBe('dark');
    expect(document.documentElement.classList.contains('dark')).toBe(true);
  });

  it('follows a light operating color scheme when no theme was chosen', () => {
    stubSystemDark(false);
    const service = createService();

    expect(service.resolved()).toBe('light');
    expect(document.documentElement.classList.contains('dark')).toBe(false);
  });

  it('updates a system-following theme when the operating color scheme changes', () => {
    const system = stubSystemDark(false);
    const service = createService();

    system.set(true);

    expect(service.resolved()).toBe('dark');
    expect(document.documentElement.classList.contains('dark')).toBe(true);
  });

  it('keeps an explicit light choice when the operating color scheme is dark', () => {
    stubSystemDark(true);
    localStorage.setItem(THEME_STORAGE_KEY, 'light');
    const service = createService();

    expect(service.preference()).toBe('light');
    expect(service.resolved()).toBe('light');
    expect(document.documentElement.classList.contains('dark')).toBe(false);
  });

  it('keeps the chosen theme after a reload', () => {
    stubSystemDark(true);
    createService().choose('light');

    const afterReload = createService();

    expect(afterReload.preference()).toBe('light');
    expect(afterReload.resolved()).toBe('light');
  });

  it('stores only the theme preference in local storage', () => {
    stubSystemDark(false);
    const recorder = recordBrowserStorageWrites();
    try {
      createService().choose('dark');
    } finally {
      recorder.restore();
    }

    expect(recorder.writes).toEqual([{ target: 'localStorage', key: THEME_STORAGE_KEY, value: 'dark' }]);
    expect(localStorage.length).toBe(1);
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('dark');
    expect(document.documentElement.classList.contains('dark')).toBe(true);
  });

  it('writes only allowed preference keys to browser storage', () => {
    stubSystemDark(false);
    const recorder = recordBrowserStorageWrites();
    try {
      const service = createService();
      service.choose('light');
      service.toggle();
      service.choose('system');
    } finally {
      recorder.restore();
    }

    for (const write of recorder.writes) {
      expect(isAllowedStorageKey(write.key)).toBe(true);
    }
  });

  it('removes the stored choice when the theme returns to the system setting', () => {
    stubSystemDark(false);
    const service = createService();
    service.choose('dark');

    service.choose('system');

    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBeNull();
    expect(service.resolved()).toBe('light');
  });

  it('toggles between light and dark', () => {
    stubSystemDark(false);
    const service = createService();

    service.toggle();
    expect(service.resolved()).toBe('dark');

    service.toggle();
    expect(service.resolved()).toBe('light');
  });

  it('ignores an unknown stored value and follows the system setting', () => {
    stubSystemDark(true);
    localStorage.setItem(THEME_STORAGE_KEY, 'sepia');

    expect(createService().preference()).toBe('system');
  });
});
