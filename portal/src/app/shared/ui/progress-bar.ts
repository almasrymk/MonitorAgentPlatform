import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

export type BarTone = 'cpu' | 'ram' | 'disk' | 'success' | 'warning' | 'danger' | 'info';

/** A labelled percentage bar (CPU green, RAM blue, Disk purple; 07 section 5.6). */
@Component({
  selector: 'mc-progress-bar',
  template: `
    @if (label()) {
      <span class="label">{{ label() }}</span>
    }
    <div class="track" role="progressbar" [attr.aria-valuenow]="clamped()" aria-valuemin="0" aria-valuemax="100" [attr.aria-label]="label() || clamped() + '%'">
      <div class="fill" [attr.data-tone]="tone()" [style.inline-size.%]="clamped() ?? 0"></div>
    </div>
    <span class="value" data-testid="progress-value">{{ clamped() === null ? '—' : clamped() + '%' }}</span>
  `,
  styles: `
    :host { display: grid; grid-template-columns: 3.5em 1fr 3em; align-items: center; gap: var(--mc-space-2); font-size: var(--mc-fs-xs); }
    .label { color: var(--mc-text-muted); }
    .track { block-size: 6px; border-radius: var(--mc-radius-pill); background: var(--mc-bg-card-raised); overflow: hidden; }
    .fill { block-size: 100%; border-radius: inherit; background: var(--mc-info); }
    .fill[data-tone='cpu'] { background: var(--mc-series-cpu); }
    .fill[data-tone='ram'] { background: var(--mc-series-ram); }
    .fill[data-tone='disk'] { background: var(--mc-series-disk); }
    .fill[data-tone='success'] { background: var(--mc-success); }
    .fill[data-tone='warning'] { background: var(--mc-warning); }
    .fill[data-tone='danger'] { background: var(--mc-danger); }
    .value { color: var(--mc-text-secondary); text-align: end; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProgressBar {
  readonly value = input<number | null | undefined>(null);
  readonly label = input<string>();
  readonly tone = input<BarTone>('info');

  readonly clamped = computed(() => {
    const value = this.value();
    return value === null || value === undefined ? null : Math.max(0, Math.min(100, Math.round(value)));
  });
}
