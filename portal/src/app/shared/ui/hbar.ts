import { ChangeDetectionStrategy, Component, input } from '@angular/core';

export interface HBarItem {
  key: string;
  label: string;
  value: number;
  percent: number;
  /** A design token name without the leading dashes, e.g. `mc-series-windows`. */
  color?: string;
}

/** Horizontal bars with count and percentage (Devices by Operating System, License Usage by OS). */
@Component({
  selector: 'mc-hbar',
  template: `
    <ul [attr.aria-label]="ariaLabel()">
      @for (item of items(); track item.key) {
        <li data-testid="hbar-row">
          <span class="label">{{ item.label }}</span>
          <span class="track"><span class="fill" [style.inline-size.%]="item.percent" [style.background]="'var(--' + (item.color ?? 'mc-info') + ')'"></span></span>
          <span class="value">{{ item.value }} <span class="muted">({{ item.percent }}%)</span></span>
        </li>
      }
    </ul>
  `,
  styles: `
    ul { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: var(--mc-space-3); }
    li { display: grid; grid-template-columns: 6em 1fr 6.5em; align-items: center; gap: var(--mc-space-2); font-size: var(--mc-fs-sm); }
    .track { block-size: 8px; border-radius: var(--mc-radius-pill); background: var(--mc-bg-card-raised); overflow: hidden; }
    .fill { display: block; block-size: 100%; border-radius: inherit; }
    .value { text-align: end; }
    .muted { color: var(--mc-text-muted); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HBar {
  readonly items = input.required<HBarItem[]>();
  readonly ariaLabel = input('');
}
