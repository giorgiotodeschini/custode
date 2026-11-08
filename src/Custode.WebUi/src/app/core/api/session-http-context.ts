// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { HttpContextToken } from '@angular/common/http';

/**
 * Marks a request that must not trigger renewal or a retry, such as an activity report.
 * A refused activity report ends the session instead (webui-session-security).
 */
export const SKIP_SESSION_HANDLING = new HttpContextToken<boolean>(() => false);
