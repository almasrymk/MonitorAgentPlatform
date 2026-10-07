import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/** A half-circle speed gauge (Internet & Network card): value against a maximum, with the value in the middle. */
@Component({
  selector: 'mc-speed-gauge',
  template: `
    <svg viewBox="0 0 120 70" [attr.width]="size()" [attr.height]="size() * 0.58" role="img" [attr.aria-label]="label() + ' ' + text()">
      <path d="M10 60 A50 50 0 0 1 110 60" class="track" fill="none" stroke-width="10" stroke-linecap="round" />
      <path d="M10 60 A50 50 0 0 1 110 60" class="fill" fill="none" stroke-width="10" stroke-linecap="round" pathLength="100" [attr.stroke-dasharray]="dash()" />
      <text x="60" y="56" text-anchor="middle" data-testid="speed-value">{{ text() }}</text>
    </svg>
  `,
  styles: `
    :host { display: inline-flex; }
    .track { stroke: var(--mc-bg-card-raised); }
    .fill { stroke: var(--mc-series-network); }
    text { fill: var(--mc-text); font-size: 14px; font-weight: 600; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SpeedGauge {
  readonly value = input<number | null>(null);
  readonly max = input(100);
  readonly unit = input('Mbps');
  readonly label = input('');
  readonly size = input(140);

  readonly dash = computed(() => `${Math.max(0, Math.min(100, ((this.value() ?? 0) / (this.max() || 1)) * 100))} 100`);
  readonly text = computed(() => (this.value() === null ? '—' : `${Math.round((this.value() ?? 0) * 10) / 10} ${this.unit()}`));
}
