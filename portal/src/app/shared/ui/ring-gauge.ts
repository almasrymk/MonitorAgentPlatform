import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

import { BarTone } from './progress-bar';

/** A ring with the value in the middle (health score, System Resource Averages). */
@Component({
  selector: 'mc-ring-gauge',
  template: `
    <svg [attr.width]="size()" [attr.height]="size()" viewBox="0 0 36 36" role="img" [attr.aria-label]="ariaLabel()">
      <circle class="track" cx="18" cy="18" r="15.9155" fill="none" stroke-width="3.2" />
      <circle class="fill" [attr.data-tone]="tone()" cx="18" cy="18" r="15.9155" fill="none" stroke-width="3.2" stroke-linecap="round"
        [attr.stroke-dasharray]="dash()" transform="rotate(-90 18 18)" />
      <text x="18" y="20.5" text-anchor="middle" data-testid="ring-value">{{ text() }}</text>
    </svg>
    @if (label()) {
      <span class="label">{{ label() }}</span>
    }
  `,
  styles: `
    :host { display: inline-flex; flex-direction: column; align-items: center; gap: var(--mc-space-1); }
    .track { stroke: var(--mc-bg-card-raised); }
    .fill { stroke: var(--mc-success); transition: stroke-dasharray 0.3s; }
    .fill[data-tone='cpu'] { stroke: var(--mc-series-cpu); }
    .fill[data-tone='ram'] { stroke: var(--mc-series-ram); }
    .fill[data-tone='disk'] { stroke: var(--mc-series-disk); }
    .fill[data-tone='warning'] { stroke: var(--mc-warning); }
    .fill[data-tone='danger'] { stroke: var(--mc-danger); }
    .fill[data-tone='info'] { stroke: var(--mc-info); }
    text { fill: var(--mc-text); font-size: 8px; font-weight: 600; }
    .label { color: var(--mc-text-muted); font-size: var(--mc-fs-xs); text-align: center; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RingGauge {
  readonly value = input<number | null | undefined>(null);
  readonly label = input<string>();
  readonly tone = input<BarTone>('success');
  readonly size = input(72);

  protected readonly clamped = computed(() => {
    const value = this.value();
    return value === null || value === undefined ? null : Math.max(0, Math.min(100, value));
  });
  protected readonly dash = computed(() => `${this.clamped() ?? 0} 100`);
  protected readonly text = computed(() => (this.clamped() === null ? '—' : `${Math.round(this.clamped()! * 10) / 10}%`));
  protected readonly ariaLabel = computed(() => `${this.label() ?? ''} ${this.text()}`.trim());
}
