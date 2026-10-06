import { ChangeDetectionStrategy, Component, input, model } from '@angular/core';

export interface TabItem {
  id: string;
  label: string;
  count?: number | null;
}

@Component({
  selector: 'mc-tabs',
  template: `
    <div role="tablist" class="tabs">
      @for (tab of tabs(); track tab.id) {
        <button type="button" role="tab" class="tab" [attr.aria-selected]="tab.id === active()" [class.active]="tab.id === active()"
          [attr.data-testid]="'tab-' + tab.id" (click)="active.set(tab.id)">
          {{ tab.label }}
          @if (tab.count !== undefined && tab.count !== null) {
            <span class="count">({{ tab.count }})</span>
          }
        </button>
      }
    </div>
  `,
  styles: `
    .tabs { display: flex; gap: var(--mc-space-5); border-block-end: 1px solid var(--mc-border); }
    .tab { background: none; border: 0; padding: var(--mc-space-2) 0; color: var(--mc-text-muted); font: inherit; cursor: pointer; border-block-end: 2px solid transparent; margin-block-end: -1px; }
    .tab.active { color: var(--mc-text); border-block-end-color: var(--mc-brand); font-weight: var(--mc-fw-medium); }
    .count { color: var(--mc-text-faint); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Tabs {
  readonly tabs = input.required<TabItem[]>();
  readonly active = model('');
}
