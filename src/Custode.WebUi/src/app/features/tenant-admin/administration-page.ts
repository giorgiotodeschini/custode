// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

/** Entry point of tenant administration. Each section is added by its own task. */
@Component({
  selector: 'app-administration-page',
  imports: [RouterLink],
  templateUrl: './administration-page.html',
})
export class AdministrationPage {}
