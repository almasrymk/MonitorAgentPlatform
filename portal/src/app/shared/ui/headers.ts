import { ChangeDetectionStrategy, Component, input } from '@angular/core';

import { Icon } from './icon';
import { StatusPill } from './status-pill';

/** Page title, subtitle and actions. */
@Component({
  selector: 'mc-page-header',
  template: `
    <div class="text">
      <h1>{{ title() }}</h1>
      @if (subtitle()) {
        <p>{{ subtitle() }}</p>
      }
    </div>
    <div class="actions"><ng-content /></div>
  `,
  styles: `
    :host { display: flex; align-items: flex-start; justify-content: space-between; gap: var(--mc-space-4); margin-block-end: var(--mc-space-5); }
    h1 { margin: 0; font-size: var(--mc-fs-2xl); font-weight: var(--mc-fw-bold); }
    p { margin: var(--mc-space-1) 0 0; color: var(--mc-text-muted); }
    .actions { display: flex; gap: var(--mc-space-2); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PageHeader {
  readonly title = input.required<string>();
  readonly subtitle = input<string>();
}

/** Entity header: icon, title, status pill, meta row ("Plan | 3 Locations | ...") and actions. */
@Component({
  selector: 'mc-entity-header',
  imports: [Icon, StatusPill],
  template: `
    <span class="icon"><mc-icon [name]="icon()" [size]="28" /></span>
    <div class="text">
      <div class="title-row">
        <h1>{{ title() }}</h1>
        @if (status()) {
          <mc-status-pill [status]="status()!" />
        }
      </div>
      @if (meta().length) {
        <p class="meta">
          @for (item of meta(); track $index) {
            @if (!$first) {
              <span class="sep" aria-hidden="true">|</span>
            }
            <span>{{ item }}</span>
          }
        </p>
      }
    </div>
    <div class="actions"><ng-content /></div>
  `,
  styles: `
    :host { display: flex; align-items: center; gap: var(--mc-space-4); margin-block-end: var(--mc-space-5); }
    .icon { display: inline-flex; padding: var(--mc-space-3); border-radius: var(--mc-radius-lg); background: var(--mc-bg-card-raised); color: var(--mc-brand); }
    .text { flex: 1; min-inline-size: 0; }
    .title-row { display: flex; align-items: center; gap: var(--mc-space-3); }
    h1 { margin: 0; font-size: var(--mc-fs-xl); }
    .meta { margin: var(--mc-space-1) 0 0; color: var(--mc-text-muted); display: flex; flex-wrap: wrap; gap: var(--mc-space-2); }
    .sep { color: var(--mc-text-faint); }
    .actions { display: flex; gap: var(--mc-space-2); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EntityHeader {
  readonly icon = input('customers');
  readonly title = input.required<string>();
  readonly status = input<string>();
  readonly meta = input<string[]>([]);
}
