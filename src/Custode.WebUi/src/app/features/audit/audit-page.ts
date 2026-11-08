// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { map } from 'rxjs';
import { AccessApi } from '../access-control/access-api.service';
import { AuditApi, AuditPageResult, AuditQuery, AuditVerification } from './audit-api.service';
import {
  AUDIT_ACTIONS,
  AuditEntryView,
  DATA_LAYER_ACTOR,
  LinkStatus,
  TimeframePreset,
  actorLabel,
  linkStatuses,
  localInputToInstant,
  presetStart,
} from './audit-model';

interface FilterState {
  preset: TimeframePreset;
  from: string;
  to: string;
  user: string;
  action: string;
  outcome: string;
  order: 'asc' | 'desc';
}

export interface AuditRow {
  entry: AuditEntryView;
  link: LinkStatus;
}

type Integrity =
  | { state: 'not-verified' }
  | { state: 'intact'; entries: number }
  | { state: 'altered'; sequence: number | null };

const PRESETS: TimeframePreset[] = ['last-24-hours', 'last-7-days', 'last-30-days', 'custom'];

function readFilters(params: Map<string, string>): FilterState {
  const preset = PRESETS.find((candidate) => candidate === params.get('timeframe')) ?? 'last-24-hours';
  return {
    preset,
    from: params.get('from') ?? '',
    to: params.get('to') ?? '',
    user: params.get('user') ?? '',
    action: params.get('action') ?? '',
    outcome: params.get('outcome') ?? '',
    order: params.get('order') === 'desc' ? 'desc' : 'asc',
  };
}

/**
 * Audit viewer for tenant administrators. Filters live in the URL so that a filtered view can be
 * shared; entries are loaded by sequence cursor and never dropped or duplicated on paging.
 */
@Component({
  selector: 'app-audit-page',
  imports: [DatePipe],
  templateUrl: './audit-page.html',
})
export class AuditPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly audit = inject(AuditApi);
  private readonly access = inject(AccessApi);

  protected readonly actions = AUDIT_ACTIONS;
  protected readonly presets = PRESETS;
  protected readonly dataLayerActor = DATA_LAYER_ACTOR;

  private readonly params = toSignal(
    this.route.queryParamMap.pipe(map((query) => new Map(query.keys.map((key) => [key, query.get(key) ?? '']) as [string, string][]))),
    { initialValue: new Map<string, string>() },
  );
  protected readonly filters = computed(() => readFilters(this.params()));
  /** The order is display only: changing it must not reload the entries. */
  private readonly queryFilters = computed(() => JSON.stringify({ ...this.filters(), order: '' }));

  protected readonly members = signal<Array<{ id: string; login: string }>>([]);
  private readonly loginById = computed(() => new Map(this.members().map((member) => [member.id, member.login] as [string, string])));

  protected readonly entries = signal<AuditEntryView[]>([]);
  protected readonly cursor = signal<number | null>(null);
  protected readonly loading = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly integrity = signal<Integrity>({ state: 'not-verified' });
  protected readonly verifying = signal(false);

  private generation = 0;

  protected readonly rows = computed<AuditRow[]>(() => {
    const ascending = this.entries();
    const statuses = linkStatuses(ascending);
    const rows = ascending.map((entry, index): AuditRow => ({ entry, link: statuses[index] ?? 'predecessor-not-loaded' }));
    return this.filters().order === 'desc' ? rows.reverse() : rows;
  });

  protected readonly brokenLinks = computed(() => this.rows().filter((row) => row.link === 'broken').map((row) => row.entry.sequence));

  /** A broken link in the loaded entries means the chain is altered, whatever the last verification said. */
  protected readonly displayedIntegrity = computed<Integrity>(() => {
    const broken = this.brokenLinks();
    if (broken.length > 0) {
      return { state: 'altered', sequence: Math.min(...broken) };
    }
    return this.integrity();
  });

  constructor() {
    this.access.members().subscribe({
      next: (list) => this.members.set(list.map((member) => ({ id: member.id, login: member.login }))),
      error: () => this.members.set([]),
    });
    toObservable(this.queryFilters)
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.restart());
  }

  protected actorName(entry: AuditEntryView): string {
    return actorLabel(entry.actorUserId, this.loginById());
  }

  protected targetOf(entry: AuditEntryView): string {
    const primary = entry.targetType ? `${entry.targetType} ${entry.targetId ?? ''}`.trim() : '—';
    const secondary = entry.secondaryTargetType ? ` · ${entry.secondaryTargetType} ${entry.secondaryTargetId ?? ''}`.trim() : '';
    return primary + secondary;
  }

  protected linkText(link: LinkStatus): string {
    switch (link) {
      case 'verified':
        return 'Link verified';
      case 'broken':
        return 'Link broken';
      case 'first':
        return 'First entry';
      default:
        return 'Earlier entry not loaded';
    }
  }

  protected applyFilters(change: Partial<FilterState>): void {
    const next: FilterState = { ...this.filters(), ...change };
    const query: Record<string, string> = {
      timeframe: next.preset,
      order: next.order,
    };
    if (next.preset === 'custom') {
      if (next.from) query['from'] = next.from;
      if (next.to) query['to'] = next.to;
    }
    if (next.user) query['user'] = next.user;
    if (next.action) query['action'] = next.action;
    if (next.outcome) query['outcome'] = next.outcome;
    void this.router.navigate([], { relativeTo: this.route, queryParams: query, replaceUrl: true });
  }

  protected loadMore(): void {
    if (this.cursor() === null || this.loading()) {
      return;
    }
    this.fetch(this.generation, this.cursor());
  }

  protected verify(): void {
    this.verifying.set(true);
    this.audit.verify().subscribe({
      next: (result: AuditVerification) => {
        this.verifying.set(false);
        this.integrity.set(
          result.isIntact
            ? { state: 'intact', entries: result.entriesVerified }
            : { state: 'altered', sequence: result.firstAlteredSequence },
        );
      },
      error: () => {
        this.verifying.set(false);
        this.loadError.set('The integrity check could not be run.');
      },
    });
  }

  private restart(): void {
    this.generation += 1;
    this.entries.set([]);
    this.cursor.set(null);
    this.loadError.set(null);
    this.fetch(this.generation, null);
  }

  private fetch(generation: number, after: number | null): void {
    const filters = this.filters();
    const query: AuditQuery = {
      userId: filters.user || null,
      action: filters.action || null,
      outcome: filters.outcome || null,
      ...this.timeframeOf(filters),
    };
    this.loading.set(true);
    this.audit.page(query, after).subscribe({
      next: (page: AuditPageResult) => {
        if (generation !== this.generation) {
          return;
        }
        this.loading.set(false);
        this.entries.update((current) => [...current, ...page.entries]);
        this.cursor.set(page.nextAfterSequence);
      },
      error: () => {
        if (generation !== this.generation) {
          return;
        }
        this.loading.set(false);
        this.loadError.set('The audit entries could not be loaded.');
      },
    });
  }

  private timeframeOf(filters: FilterState): Pick<AuditQuery, 'from' | 'to'> {
    if (filters.preset === 'custom') {
      return { from: localInputToInstant(filters.from), to: localInputToInstant(filters.to) };
    }
    return { from: presetStart(filters.preset, new Date()).toISOString(), to: null };
  }
}
