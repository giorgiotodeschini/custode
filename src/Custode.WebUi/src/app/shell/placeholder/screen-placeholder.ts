// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { Component, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';

/** Holds the URL of a screen whose content is built by a later task. The title comes from the route data. */
@Component({
  selector: 'app-screen-placeholder',
  templateUrl: './screen-placeholder.html',
})
export class ScreenPlaceholder {
  protected readonly title = inject(ActivatedRoute).snapshot.data['title'] as string;
}
