import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';

import { ReportData, ReportsApi, saveFile } from '../../core/api/reports.api';
import { I18nService } from '../../core/i18n/i18n.service';
import { Button } from '../../shared/ui/button';
import { Card } from '../../shared/ui/card';
import { PageHeader } from '../../shared/ui/headers';
import { Icon } from '../../shared/ui/icon';
import { Skeleton } from '../../shared/ui/skeleton';
import { EmptyState, ErrorState } from '../../shared/ui/states';

const TYPES = ['overview', 'location-summary', 'device-health', 'performance', 'network-usage', 'alerts', 'license-usage', 'custom'];
const RANGES: Record<string, number> = { '24h': 1, '7d': 7, '30d': 30, '90d': 90 };
const MAX_ROWS = 500;

/** A cell as text: numbers with at most two decimals, timestamps in the user's locale. */
export function cellText(value: unknown, locale: string): string {
  if (value === null || value === undefined) {
    return '';
  }
  if (typeof value === 'number') {
    return Number.isInteger(value) ? String(value) : String(Math.round(value * 100) / 100);
  }
  if (typeof value === 'string' && /^\d{4}-\d{2}-\d{2}T/.test(value)) {
    return new Date(value).toLocaleString(locale, { dateStyle: 'medium', timeStyle: 'short' });
  }
  return String(value);
}

/** RFC 4180 CSV of a report (with a BOM so spreadsheet programs read Arabic names); formula-like text is neutralised. */
export function toCsv(data: ReportData): string {
  const escape = (text: string): string => (/[",\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text);
  const cell = (value: unknown): string => {
    if (value === null || value === undefined) {
      return '';
    }
    const text = String(value);
    return typeof value === 'string' && /^[=+\-@\t\r]/.test(text) ? `'${text}` : text;
  };
  const lines = [data.columns.map((c) => escape(c.label)).join(','), ...data.rows.map((row) => row.map((v) => escape(cell(v))).join(','))];
  return '﻿' + lines.join('\r\n') + '\r\n';
}

/** Platform Reports (07 section 5.8, platform scope): the eight reports across every customer, with a Customer column. */
@Component({
  selector: 'mc-platform-reports-page',
  imports: [FormsModule, PageHeader, Card, Button, Icon, Skeleton, EmptyState, ErrorState],
  template: `
    <mc-page-header [title]="i18n.t('nav.reports')" [subtitle]="i18n.t('reports.platformSubtitle')" />
    <div class="layout">
      <mc-card [title]="i18n.t('reports.type')" [flush]="true">
        <ul class="types">
          @for (t of types; track t) {
            <li>
              <button type="button" [class.selected]="type() === t" [attr.aria-pressed]="type() === t" (click)="type.set(t)" [attr.data-testid]="'type-' + t">
                <mc-icon name="reports" [size]="16" /> {{ i18n.t('reports.types.' + t) }}
              </button>
            </li>
          }
        </ul>
      </mc-card>

      <div class="result">
        <div class="toolbar">
          <label class="mc-form"><span>{{ i18n.t('reports.timeRange') }}</span>
            <select [ngModel]="range()" (ngModelChange)="range.set($event)" data-testid="report-range">
              @for (r of ranges; track r) {
                <option [value]="r">{{ i18n.t('range.' + r) }}</option>
              }
            </select>
          </label>
          <button type="button" mcButton="primary-outline" [disabled]="!data()" (click)="exportCsv()" data-testid="export-csv">
            <mc-icon name="download" [size]="14" /> {{ i18n.t('reports.exportCsv') }}
          </button>
        </div>

        @if (failed()) {
          <mc-error-state (retry)="load()" />
        } @else if (data(); as d) {
          <div class="summary" data-testid="report-summary">
            @for (s of d.summary; track s.label) {
              <div class="tile"><span>{{ s.label }}</span><strong>{{ s.value }}</strong></div>
            }
          </div>
          <mc-card [title]="d.title" [flush]="true">
            @if (d.rows.length === 0) {
              <mc-empty-state icon="reports" [title]="i18n.t('reports.noRows')" />
            } @else {
              <div class="scroll">
                <table data-testid="report-table">
                  <thead>
                    <tr>
                      @for (c of d.columns; track c.key) {
                        <th scope="col" [class.n]="c.numeric">{{ c.label }}</th>
                      }
                    </tr>
                  </thead>
                  <tbody>
                    @for (row of shown(); track $index) {
                      <tr>
                        @for (c of d.columns; track c.key; let i = $index) {
                          <td [class.n]="c.numeric">{{ text(row[i]) }}</td>
                        }
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
              @if (d.rows.length > shown().length) {
                <p class="more">{{ i18n.t('reports.truncated', { shown: shown().length, total: d.rows.length }) }}</p>
              }
            }
          </mc-card>
        } @else {
          <mc-skeleton [height]="360" />
        }
      </div>
    </div>
  `,
  styles: `
    :host { display: block; }
    .layout { display: grid; grid-template-columns: minmax(220px, 260px) minmax(0, 1fr); gap: var(--mc-space-4); align-items: start; }
    @media (max-width: 900px) { .layout { grid-template-columns: 1fr; } }
    .types { list-style: none; margin: 0; padding: 0; }
    .types button { inline-size: 100%; display: flex; align-items: center; gap: var(--mc-space-2); padding: var(--mc-space-3) var(--mc-space-4); background: transparent; border: 0;
      border-inline-start: 3px solid transparent; color: var(--mc-text); font: inherit; text-align: start; cursor: pointer; }
    .types button:hover { background: var(--mc-bg-hover); }
    .types button.selected { background: var(--mc-bg-nav-active); border-inline-start-color: var(--mc-brand); color: var(--mc-brand); font-weight: var(--mc-fw-medium); }
    .result { display: flex; flex-direction: column; gap: var(--mc-space-4); min-inline-size: 0; }
    .toolbar { display: flex; align-items: flex-end; justify-content: space-between; gap: var(--mc-space-3); }
    .summary { display: flex; flex-wrap: wrap; gap: var(--mc-space-3); }
    .tile { display: flex; flex-direction: column; gap: 2px; padding: var(--mc-space-3) var(--mc-space-4); background: var(--mc-bg-card); border: 1px solid var(--mc-border); border-radius: var(--mc-radius-md); min-inline-size: 120px; }
    .tile span { color: var(--mc-text-muted); font-size: var(--mc-fs-sm); }
    .tile strong { font-size: var(--mc-fs-lg); }
    .scroll { overflow-x: auto; }
    table { inline-size: 100%; border-collapse: collapse; font-size: var(--mc-fs-sm); }
    th, td { padding: var(--mc-space-2) var(--mc-space-3); text-align: start; border-block-end: 1px solid var(--mc-border); white-space: nowrap; }
    th { color: var(--mc-text-muted); font-weight: var(--mc-fw-medium); background: var(--mc-bg-card-raised); }
    .n { text-align: end; font-variant-numeric: tabular-nums; }
    .more { color: var(--mc-text-muted); padding: var(--mc-space-3) var(--mc-space-4); margin: 0; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PlatformReportsPage {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(ReportsApi);

  protected readonly types = TYPES;
  protected readonly ranges = Object.keys(RANGES);
  protected readonly type = signal('overview');
  protected readonly range = signal('7d');
  protected readonly data = signal<ReportData | null>(null);
  protected readonly failed = signal(false);
  protected readonly shown = computed(() => this.data()?.rows.slice(0, MAX_ROWS) ?? []);
  private request = 0;

  constructor() {
    effect(() => {
      this.type();
      this.range();
      untracked(() => void this.load());
    });
  }

  async load(): Promise<void> {
    const request = ++this.request;
    this.failed.set(false);
    this.data.set(null);
    const to = new Date();
    const from = new Date(to.getTime() - RANGES[this.range()] * 86_400_000);
    try {
      const data = await firstValueFrom(this.api.platform(this.type(), from.toISOString(), to.toISOString()));
      if (request === this.request) {
        this.data.set(data);
      }
    } catch {
      if (request === this.request) {
        this.failed.set(true);
      }
    }
  }

  protected text(value: unknown): string {
    return cellText(value, this.i18n.language() === 'ar' ? 'ar-EG' : 'en-GB');
  }

  protected exportCsv(): void {
    const d = this.data();
    if (d) {
      saveFile({ fileName: `${this.type()}-${new Date().toISOString().slice(0, 10)}.csv`, blob: new Blob([toCsv(d)], { type: 'text/csv;charset=utf-8' }) });
    }
  }
}
