// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { safeReturnUrl } from '../../../core/navigation/return-url';
import { SessionEndReason, SessionService } from '../../../core/session/session.service';

const INVALID_CREDENTIALS = 'Invalid login or password';
const UNAVAILABLE = 'Sign-in is unavailable. Try again in a moment.';

const SESSION_END_NOTICES: Partial<Record<SessionEndReason, string>> = {
  'refresh-failed': 'Your session has ended. Sign in again to continue.',
  'activity-refused': 'Your session has ended. Sign in again to continue.',
  'idle-timeout': 'Your session ended after a period of inactivity. Sign in again to continue.',
};

@Component({
  selector: 'app-sign-in',
  imports: [ReactiveFormsModule],
  templateUrl: './sign-in.html',
})
export class SignIn {
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly form = new FormGroup({
    login: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    password: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });
  protected readonly submitting = signal(false);
  protected readonly failure = signal<string | null>(null);
  protected readonly notice = computed(() => {
    const reason = this.session.endReason();
    return reason ? (SESSION_END_NOTICES[reason] ?? null) : null;
  });

  protected submit(): void {
    if (this.submitting()) {
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { login, password } = this.form.getRawValue();
    this.submitting.set(true);
    this.failure.set(null);
    this.session.signIn(login, password).subscribe({
      next: () => {
        void this.router.navigateByUrl(safeReturnUrl(this.route.snapshot.queryParamMap.get('returnUrl')));
      },
      error: (error: unknown) => {
        this.submitting.set(false);
        this.form.controls.password.reset();
        this.failure.set(error instanceof HttpErrorResponse && error.status === 401 ? INVALID_CREDENTIALS : UNAVAILABLE);
      },
    });
  }
}
