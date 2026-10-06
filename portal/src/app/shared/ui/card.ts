import { ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({
  selector: 'mc-card',
  template: `
    @if (title()) {
      <header class="card-header">
        <h2 class="card-title">{{ title() }}</h2>
        <div class="card-actions"><ng-content select="[cardActions]" /></div>
      </header>
    }
    <div class="card-body" [class.flush]="flush()"><ng-content /></div>
  `,
  styles: `
    :host { display: block; background: var(--mc-bg-card); border-radius: var(--mc-radius-lg); box-shadow: var(--mc-shadow-card); }
    .card-header { display: flex; align-items: center; justify-content: space-between; gap: var(--mc-space-3); padding: var(--mc-space-4) var(--mc-space-4) 0; }
    .card-title { margin: 0; font-size: var(--mc-fs-lg); font-weight: var(--mc-fw-semibold); }
    .card-actions { display: flex; gap: var(--mc-space-2); }
    .card-body { padding: var(--mc-space-4); }
    .card-body.flush { padding: 0; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Card {
  readonly title = input<string>();
  readonly flush = input(false);
}
