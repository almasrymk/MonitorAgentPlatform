import { ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({
  selector: 'mc-skeleton',
  template: '',
  host: { 'aria-hidden': 'true', '[style.block-size.px]': 'height()', '[style.inline-size]': 'width()' },
  styles: `
    :host { display: block; border-radius: var(--mc-radius-sm); background: linear-gradient(90deg, var(--mc-bg-card-raised), var(--mc-bg-hover), var(--mc-bg-card-raised)); background-size: 200% 100%; animation: shimmer 1.2s linear infinite; }
    @keyframes shimmer { from { background-position: 200% 0; } to { background-position: -200% 0; } }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Skeleton {
  readonly height = input(16);
  readonly width = input('100%');
}
