// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { NoticeService } from '../../core/notifications/notice.service';
import { serverMessage } from '../secret-browser/create-folder-dialog';
import { PolicyField, PolicyValues, POLICY_FIELDS, POLICY_RANGES, changedFields, policyEffects, validatePolicy } from './policy-model';
import { PolicyResult, TenantApi } from './tenant-api.service';

type Draft = Record<PolicyField, number | null>;

/** Tenant-wide security policies with a validated form and a summary of the effect before saving. */
@Component({
  selector: 'app-policies-page',
  templateUrl: './policies-page.html',
})
export class PoliciesPage {
  private readonly tenant = inject(TenantApi);
  private readonly notices = inject(NoticeService);

  protected readonly fields = POLICY_FIELDS;
  protected readonly ranges = POLICY_RANGES;
  protected readonly saved = signal<PolicyValues | null>(null);
  /** Not edited by this form, but the server requires it in every save. */
  private accessTokenLifetimeMinutes = 15;
  protected readonly draft = signal<Draft>({ idleTimeoutMinutes: null, refreshTokenLifetimeDays: null, clipboardClearSeconds: null });
  protected readonly loadError = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly serverErrors = signal<string[]>([]);
  protected readonly attempted = signal(false);

  protected readonly errors = computed(() => validatePolicy(this.draft()));
  protected readonly valid = computed(() => Object.keys(this.errors()).length === 0);
  protected readonly changed = computed(() => {
    const saved = this.saved();
    return saved && this.valid() ? changedFields(saved, this.values()) : [];
  });
  protected readonly effects = computed(() => {
    const saved = this.saved();
    return saved && this.valid() ? policyEffects(saved, this.values()) : [];
  });

  constructor() {
    this.load();
  }

  protected setField(field: PolicyField, raw: string): void {
    const value = raw.trim() === '' ? null : Number(raw);
    this.draft.update((current) => ({ ...current, [field]: value }));
    this.serverErrors.set([]);
  }

  protected save(): void {
    this.attempted.set(true);
    if (!this.valid() || this.changed().length === 0 || this.saving()) {
      return;
    }
    this.saving.set(true);
    this.serverErrors.set([]);
    this.tenant.updatePolicy({ ...this.values(), accessTokenLifetimeMinutes: this.accessTokenLifetimeMinutes }).subscribe({
      next: (result) => {
        this.saving.set(false);
        this.applyServerValues(result);
        this.notices.show('Security policy saved.');
      },
      error: (error: unknown) => {
        this.saving.set(false);
        const reasons = error instanceof HttpErrorResponse ? (error.error as { errors?: string[] } | null)?.errors : undefined;
        this.serverErrors.set(reasons && reasons.length > 0 ? reasons : [serverMessage(error, 'The policy was refused by the server.')]);
      },
    });
  }

  protected rangeText(field: PolicyField): string {
    const range = POLICY_RANGES[field];
    return `${range.min} to ${range.max} ${range.unit}`;
  }

  private values(): PolicyValues {
    const draft = this.draft();
    return {
      idleTimeoutMinutes: draft.idleTimeoutMinutes ?? 0,
      refreshTokenLifetimeDays: draft.refreshTokenLifetimeDays ?? 0,
      clipboardClearSeconds: draft.clipboardClearSeconds ?? 0,
    };
  }

  private load(): void {
    this.tenant.policy().subscribe({
      next: (result) => this.applyServerValues(result),
      error: () => this.loadError.set('The security policy could not be loaded.'),
    });
  }

  private applyServerValues(result: PolicyResult): void {
    this.accessTokenLifetimeMinutes = result.accessTokenLifetimeMinutes;
    const values: PolicyValues = {
      idleTimeoutMinutes: result.idleTimeoutMinutes,
      refreshTokenLifetimeDays: result.refreshTokenLifetimeDays,
      clipboardClearSeconds: result.clipboardClearSeconds,
    };
    this.saved.set(values);
    this.draft.set({ ...values });
    this.attempted.set(false);
    this.loadError.set(null);
  }
}
