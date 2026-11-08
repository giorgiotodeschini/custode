// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { Component, inject } from '@angular/core';
import { ThemePreference, ThemeService } from '../../core/preferences/theme.service';

@Component({
  selector: 'app-settings-page',
  templateUrl: './settings-page.html',
})
export class SettingsPage {
  protected readonly theme = inject(ThemeService);
  protected readonly choices: ReadonlyArray<{ value: ThemePreference; label: string }> = [
    { value: 'system', label: 'Follow the operating system' },
    { value: 'light', label: 'Light' },
    { value: 'dark', label: 'Dark' },
  ];

  protected choose(value: ThemePreference): void {
    this.theme.choose(value);
  }
}
