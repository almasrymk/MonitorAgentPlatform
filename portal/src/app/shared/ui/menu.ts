import { ChangeDetectionStrategy, Component, ElementRef, inject, input, output, signal } from '@angular/core';

import { Icon } from './icon';

export interface MenuItem {
  id: string;
  label: string;
  danger?: boolean;
  disabled?: boolean;
}

/** Kebab menu: a button that opens a list of actions; Escape or a click outside closes it. */
@Component({
  selector: 'mc-menu',
  imports: [Icon],
  template: `
    <button type="button" class="trigger" data-testid="menu-trigger" [attr.aria-label]="label()" aria-haspopup="menu" [attr.aria-expanded]="open()" (click)="toggle()">
      <mc-icon name="more" [size]="18" />
    </button>
    @if (open()) {
      <ul class="items" role="menu" tabindex="-1" (keydown.escape)="close()">
        @for (item of items(); track item.id) {
          <li role="none">
            <button type="button" role="menuitem" [class.danger]="item.danger" [disabled]="item.disabled" [attr.data-testid]="'menu-' + item.id" (click)="choose(item)">{{ item.label }}</button>
          </li>
        }
      </ul>
    }
  `,
  host: { '(document:click)': 'outside($event)' },
  styles: `
    :host { position: relative; display: inline-flex; }
    .trigger { background: none; border: 0; color: var(--mc-text-muted); padding: 4px; border-radius: var(--mc-radius-sm); cursor: pointer; display: inline-flex; }
    .trigger:hover { background: var(--mc-bg-hover); color: var(--mc-text); }
    .items { position: absolute; inset-block-start: 100%; inset-inline-end: 0; z-index: var(--mc-z-topbar); min-inline-size: 160px; margin: 4px 0 0; padding: 4px; list-style: none; background: var(--mc-bg-card-raised); border-radius: var(--mc-radius-md); box-shadow: 0 0 0 1px var(--mc-border-strong), 0 12px 24px rgba(0, 0, 0, 0.35); }
    .items button { inline-size: 100%; text-align: start; background: none; border: 0; color: var(--mc-text); font: inherit; font-size: var(--mc-fs-sm); padding: 6px 10px; border-radius: var(--mc-radius-sm); cursor: pointer; }
    .items button:hover:not(:disabled) { background: var(--mc-bg-hover); }
    .items button.danger { color: var(--mc-danger); }
    .items button:disabled { color: var(--mc-text-faint); cursor: default; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Menu {
  private readonly host = inject(ElementRef<HTMLElement>);
  readonly items = input.required<MenuItem[]>();
  readonly label = input('');
  readonly selected = output<string>();
  readonly open = signal(false);

  toggle(): void {
    this.open.update((open) => !open);
  }

  close(): void {
    this.open.set(false);
  }

  choose(item: MenuItem): void {
    this.open.set(false);
    this.selected.emit(item.id);
  }

  protected outside(event: Event): void {
    if (this.open() && !(this.host.nativeElement as HTMLElement).contains(event.target as Node)) {
      this.open.set(false);
    }
  }
}
