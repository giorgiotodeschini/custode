// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { DOCUMENT } from '@angular/common';
import { Injectable, computed, inject, signal } from '@angular/core';

export type ThemePreference = 'system' | 'light' | 'dark';
export type ResolvedTheme = 'light' | 'dark';

export const THEME_STORAGE_KEY = 'custode.theme';

const DARK_QUERY = '(prefers-color-scheme: dark)';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly document = inject(DOCUMENT);
  private readonly media = this.document.defaultView?.matchMedia(DARK_QUERY) ?? null;
  private readonly systemDark = signal(this.media?.matches ?? false);

  readonly preference = signal<ThemePreference>(readStoredPreference(this.document));
  readonly resolved = computed<ResolvedTheme>(() => {
    const preference = this.preference();
    if (preference !== 'system') {
      return preference;
    }
    return this.systemDark() ? 'dark' : 'light';
  });

  constructor() {
    this.media?.addEventListener('change', (event) => {
      this.systemDark.set(event.matches);
      this.apply();
    });
    this.apply();
  }

  choose(preference: ThemePreference): void {
    writeStoredPreference(this.document, preference);
    this.preference.set(preference);
    this.apply();
  }

  toggle(): void {
    this.choose(this.resolved() === 'dark' ? 'light' : 'dark');
  }

  private apply(): void {
    this.document.documentElement.classList.toggle('dark', this.resolved() === 'dark');
  }
}

function readStoredPreference(document: Document): ThemePreference {
  try {
    const stored = document.defaultView?.localStorage.getItem(THEME_STORAGE_KEY);
    return stored === 'light' || stored === 'dark' ? stored : 'system';
  } catch {
    return 'system';
  }
}

function writeStoredPreference(document: Document, preference: ThemePreference): void {
  try {
    const storage = document.defaultView?.localStorage;
    if (preference === 'system') {
      storage?.removeItem(THEME_STORAGE_KEY);
    } else {
      storage?.setItem(THEME_STORAGE_KEY, preference);
    }
  } catch {
    // Storage may be unavailable (for example in private modes); the choice then lasts for this page only.
  }
}
