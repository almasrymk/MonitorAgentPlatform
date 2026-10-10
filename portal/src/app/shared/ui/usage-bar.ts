import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/** "used / limit" bar; turns warning above 80% and danger at the limit. An empty limit means unlimited. */
@Component({
  selector: 'mc-usage-bar',
  template: `
    <div class="track" role="progressbar" [attr.aria-valuenow]="used()" aria-valuemin="0" [attr.aria-valuemax]="limit()" [attr.aria-label]="label() || used() + ' / ' + limit()">
      <div class="fill" [attr.data-level]="level()" [style.inline-size.%]="percent()"></div>
    </div>
    <span class="text" data-testid="usage-text">{{ used() }} / {{ limit() ?? '∞' }}</span>
  `,
  styles: `
    :host { display: flex; align-items: center; gap: var(--mc-space-2); }
    .track { flex: 1; block-size: 6px; border-radius: var(--mc-radius-pill); background: var(--mc-bg-card-raised); overflow: hidden; }
    .fill { block-size: 100%; border-radius: inherit; background: var(--mc-info); }
    .fill[data-level='warning'] { background: var(--mc-warning); }
    .fill[data-level='danger'] { background: var(--mc-danger); }
    .text { color: var(--mc-text-secondary); font-size: var(--mc-fs-xs); white-space: nowrap; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UsageBar {
  readonly used = input(0);
  readonly limit = input<number | null | undefined>(null);
  readonly label = input('');

  readonly percent = computed(() => {
    const limit = this.limit();
    return limit ? Math.min(100, Math.round((this.used() / limit) * 100)) : 0;
  });

  readonly level = computed(() => (this.percent() >= 100 ? 'danger' : this.percent() >= 80 ? 'warning' : 'normal'));
}
