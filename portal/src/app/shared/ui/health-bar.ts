import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/** Health percentage bar: green from 80%, amber from 50%, red below (Top Customers, Device Status by Location). */
@Component({
  selector: 'mc-health-bar',
  template: `
    <div class="track" role="meter" [attr.aria-valuenow]="value()" aria-valuemin="0" aria-valuemax="100" [attr.aria-label]="label()">
      <div class="fill" [attr.data-level]="level()" [style.inline-size.%]="value() ?? 0"></div>
    </div>
    <span class="value" data-testid="health-value">{{ value() === null || value() === undefined ? '—' : value() + '%' }}</span>
  `,
  styles: `
    :host { display: flex; align-items: center; gap: var(--mc-space-2); min-inline-size: 7em; }
    .track { flex: 1; block-size: 6px; border-radius: var(--mc-radius-pill); background: var(--mc-bg-card-raised); overflow: hidden; }
    .fill { block-size: 100%; border-radius: inherit; background: var(--mc-success); }
    .fill[data-level='warning'] { background: var(--mc-warning); }
    .fill[data-level='danger'] { background: var(--mc-danger); }
    .value { font-size: var(--mc-fs-xs); color: var(--mc-text-secondary); min-inline-size: 3em; text-align: end; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HealthBar {
  readonly value = input<number | null | undefined>(null);
  readonly label = input('');

  readonly level = computed(() => {
    const value = this.value() ?? 0;
    return value >= 80 ? 'success' : value >= 50 ? 'warning' : 'danger';
  });
}
