import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/** A tiny SVG trend line (no axes) for tiles and tables. */
@Component({
  selector: 'mc-sparkline',
  template: `
    <svg [attr.width]="width()" [attr.height]="height()" [attr.viewBox]="'0 0 ' + width() + ' ' + height()" role="img" [attr.aria-label]="label()">
      <path [attr.d]="path()" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linejoin="round" stroke-linecap="round" />
    </svg>
  `,
  styles: `:host { display: inline-flex; color: var(--mc-info); }`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Sparkline {
  readonly values = input.required<number[]>();
  readonly width = input(80);
  readonly height = input(24);
  readonly label = input('');

  readonly path = computed(() => {
    const values = this.values();
    if (values.length < 2) {
      return '';
    }
    const min = Math.min(...values);
    const max = Math.max(...values);
    const span = max - min || 1;
    const w = this.width();
    const h = this.height() - 2;
    return values.map((v, i) => `${i === 0 ? 'M' : 'L'}${((i / (values.length - 1)) * w).toFixed(1)},${(1 + h - ((v - min) / span) * h).toFixed(1)}`).join(' ');
  });
}
