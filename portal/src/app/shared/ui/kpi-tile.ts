import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

import { Icon } from './icon';

export type Tone = 'success' | 'info' | 'warning' | 'danger' | 'neutral' | 'brand';

/** KPI tile: icon, label, value, delta coloured by meaning (`goodWhen`, 07 section 1 correction 3), caption. */
@Component({
  selector: 'mc-kpi-tile',
  imports: [Icon],
  template: `
    <span class="tile-icon" [attr.data-tone]="tone()"><mc-icon [name]="icon()" [size]="20" /></span>
    <div class="tile-text">
      <span class="tile-label">{{ label() }}</span>
      <span class="tile-value" data-testid="kpi-value">{{ value() ?? '—' }}</span>
      @if (delta() !== null && delta() !== undefined) {
        <span class="tile-delta" [attr.data-trend]="trend()" data-testid="kpi-delta">
          <mc-icon [name]="delta()! >= 0 ? 'arrowUp' : 'arrowDown'" [size]="12" />
          {{ deltaText() }}
        </span>
      }
      @if (caption()) {
        <span class="tile-caption">{{ caption() }}</span>
      }
    </div>
  `,
  styles: `
    :host { display: flex; gap: var(--mc-space-3); align-items: flex-start; background: var(--mc-bg-card); border-radius: var(--mc-radius-lg); box-shadow: var(--mc-shadow-card); padding: var(--mc-space-4); min-inline-size: 0; }
    .tile-icon { display: inline-flex; padding: var(--mc-space-2); border-radius: var(--mc-radius-md); background: var(--mc-neutral-soft); color: var(--mc-neutral); }
    .tile-icon[data-tone='success'], .tile-icon[data-tone='brand'] { background: var(--mc-success-soft); color: var(--mc-success); }
    .tile-icon[data-tone='info'] { background: var(--mc-info-soft); color: var(--mc-info); }
    .tile-icon[data-tone='warning'] { background: var(--mc-warning-soft); color: var(--mc-warning); }
    .tile-icon[data-tone='danger'] { background: var(--mc-danger-soft); color: var(--mc-danger); }
    .tile-text { display: flex; flex-direction: column; gap: 2px; min-inline-size: 0; }
    .tile-label { color: var(--mc-text-muted); font-size: var(--mc-fs-sm); }
    .tile-value { font-size: var(--mc-fs-2xl); font-weight: var(--mc-fw-bold); }
    .tile-delta { display: inline-flex; align-items: center; gap: 2px; font-size: var(--mc-fs-xs); font-weight: var(--mc-fw-medium); }
    .tile-delta[data-trend='good'] { color: var(--mc-success); }
    .tile-delta[data-trend='bad'] { color: var(--mc-danger); }
    .tile-delta[data-trend='flat'] { color: var(--mc-text-muted); }
    .tile-caption { color: var(--mc-text-faint); font-size: var(--mc-fs-xs); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class KpiTile {
  readonly icon = input('dashboard');
  readonly label = input.required<string>();
  readonly value = input<string | number | null>();
  readonly delta = input<number | null>();
  readonly deltaPercent = input<number | null>();
  readonly goodWhen = input<'up' | 'down'>('up');
  readonly caption = input<string>();
  readonly tone = input<Tone>('neutral');

  protected readonly trend = computed(() => {
    const delta = this.delta() ?? 0;
    if (delta === 0) {
      return 'flat';
    }
    return (delta > 0) === (this.goodWhen() === 'up') ? 'good' : 'bad';
  });

  protected readonly deltaText = computed(() => {
    const delta = this.delta() ?? 0;
    const percent = this.deltaPercent();
    const sign = delta > 0 ? '+' : '';
    return percent === null || percent === undefined ? `${sign}${delta}` : `${sign}${delta} (${sign}${percent}%)`;
  });
}
