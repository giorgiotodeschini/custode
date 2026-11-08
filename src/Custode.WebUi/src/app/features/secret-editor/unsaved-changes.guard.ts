// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { CanDeactivateFn } from '@angular/router';
import type { SecretEditorPage } from './secret-editor-page';

/** Asks before leaving a secret form that has unsaved changes. */
export const unsavedChangesGuard: CanDeactivateFn<SecretEditorPage> = (component) => component.confirmLeave();
