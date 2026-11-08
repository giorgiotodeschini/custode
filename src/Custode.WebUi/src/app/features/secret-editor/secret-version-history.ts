// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { Dialog } from '@angular/cdk/dialog';
import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnDestroy, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { NoticeService } from '../../core/notifications/notice.service';
import { FieldDiff, diffVersions } from './secret-model';
import { SecretVersion, SecretValue, VaultApi } from '../../core/vault/vault-api.service';
import { confirmAction } from '../../shared/ui/confirm-dialog';

const MAX_COMPARED = 2;

/** Version history with a visual diff and one-click rollback for users with Write. */
@Component({
  selector: 'app-secret-version-history',
  imports: [DatePipe],
  templateUrl: './secret-version-history.html',
})
export class SecretVersionHistory implements OnInit, OnDestroy {
  private readonly api = inject(VaultApi);
  private readonly dialog = inject(Dialog);
  private readonly notices = inject(NoticeService);

  readonly secretId = input.required<string>();
  readonly canWrite = input.required<boolean>();
  readonly etag = input<string | null>(null);
  readonly dirty = input(false);
  readonly restored = output<void>();

  protected readonly versions = signal<SecretVersion[]>([]);
  protected readonly loadError = signal<string | null>(null);
  protected readonly selected = signal<readonly number[]>([]);
  protected readonly comparing = signal(false);
  protected readonly compareError = signal<string | null>(null);
  protected readonly diff = signal<FieldDiff[] | null>(null);
  protected readonly olderNumber = signal<number | null>(null);
  protected readonly newerNumber = signal<number | null>(null);
  protected readonly revealed = signal(false);
  protected readonly rollbackError = signal<string | null>(null);
  protected readonly latest = computed(() => Math.max(0, ...this.versions().map((version) => version.number)));
  protected readonly hasRevealableValues = computed(() => (this.diff() ?? []).some((entry) => entry.revealable));

  ngOnInit(): void {
    this.reload();
  }

  ngOnDestroy(): void {
    this.closeDiff();
  }

  protected toggleSelected(number: number): void {
    this.selected.update((current) => {
      if (current.includes(number)) {
        return current.filter((item) => item !== number);
      }
      const next = [...current, number];
      return next.length > MAX_COMPARED ? next.slice(next.length - MAX_COMPARED) : next;
    });
  }

  protected isSelected(number: number): boolean {
    return this.selected().includes(number);
  }

  protected compare(): void {
    const [first, second] = this.selected();
    if (first === undefined || second === undefined) {
      return;
    }
    const older = Math.min(first, second);
    const newer = Math.max(first, second);
    this.comparing.set(true);
    this.compareError.set(null);
    this.revealed.set(false);
    this.api.readVersionValue(this.secretId(), older).subscribe({
      next: (olderValue) => {
        this.api.readVersionValue(this.secretId(), newer).subscribe({
          next: (newerValue) => this.showDiff(older, newer, olderValue, newerValue),
          error: (error: unknown) => this.failCompare(error),
        });
      },
      error: (error: unknown) => this.failCompare(error),
    });
  }

  protected closeDiff(): void {
    this.diff.set(null);
    this.revealed.set(false);
    this.olderNumber.set(null);
    this.newerNumber.set(null);
  }

  protected toggleReveal(): void {
    this.revealed.update((value) => !value);
  }

  protected shown(entry: FieldDiff, side: 'before' | 'after'): string {
    if (!this.revealed() || !entry.revealable) {
      return '••••••';
    }
    return entry[side] ?? '—';
  }

  protected rollback(version: SecretVersion): void {
    const etag = this.etag();
    if (!etag) {
      return;
    }
    confirmAction(this.dialog, {
      title: `Roll back to version ${version.number}?`,
      message: this.dirty()
        ? 'A new version is created with the content of this version. Unsaved edits in the form are discarded.'
        : 'A new version is created with the content of this version. The history is kept.',
      confirmLabel: 'Roll back',
      destructive: true,
    }).subscribe((confirmed) => {
      if (!confirmed) {
        return;
      }
      this.rollbackError.set(null);
      this.api.rollback(this.secretId(), version.number, etag).subscribe({
        next: () => {
          this.notices.show(`Rolled back to version ${version.number}.`);
          this.closeDiff();
          this.reload();
          this.restored.emit();
        },
        error: (error: unknown) => {
          if (error instanceof HttpErrorResponse && error.status === 412) {
            this.rollbackError.set('The secret changed since it was opened. Reload the page and try again.');
          } else if (error instanceof HttpErrorResponse && error.status === 403) {
            this.rollbackError.set('Write permission is required to roll back this secret.');
          } else {
            this.rollbackError.set('The rollback was refused by the server.');
          }
        },
      });
    });
  }

  private reload(): void {
    this.api.listVersions(this.secretId()).subscribe({
      next: (versions) => {
        this.versions.set([...versions].sort((a, b) => b.number - a.number));
        this.loadError.set(null);
      },
      error: () => this.loadError.set('The version history could not be loaded.'),
    });
  }

  private showDiff(olderVersion: number, newerVersion: number, older: SecretValue, newer: SecretValue): void {
    this.diff.set(diffVersions(older, newer));
    this.olderNumber.set(olderVersion);
    this.newerNumber.set(newerVersion);
    this.comparing.set(false);
  }

  private failCompare(error: unknown): void {
    this.comparing.set(false);
    const denied = error instanceof HttpErrorResponse && error.status === 403;
    this.compareError.set(denied ? 'You do not have Read permission on this folder.' : 'The versions could not be compared.');
  }
}
