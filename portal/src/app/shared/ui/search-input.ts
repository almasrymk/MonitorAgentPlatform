import { ChangeDetectionStrategy, Component, DestroyRef, inject, input, model, output } from '@angular/core';

import { Icon } from './icon';

/** Search box; `search` fires 300 ms after the last keystroke (or on Enter). */
@Component({
  selector: 'mc-search-input',
  imports: [Icon],
  template: `
    <mc-icon name="search" [size]="16" />
    <input type="search" [value]="value()" [attr.placeholder]="placeholder()" [attr.aria-label]="label() ?? placeholder()"
      (input)="onInput($any($event.target).value)" (keydown.enter)="emitNow()" />
  `,
  styles: `
    :host { display: flex; align-items: center; gap: var(--mc-space-2); padding-inline: var(--mc-space-3); block-size: 36px; background: var(--mc-bg-input); border: 1px solid var(--mc-border); border-radius: var(--mc-radius-md); color: var(--mc-text-muted); min-inline-size: 220px; }
    :host(:focus-within) { box-shadow: var(--mc-focus-ring); }
    input { flex: 1; min-inline-size: 0; background: transparent; border: 0; outline: none; color: var(--mc-text); font: inherit; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SearchInput {
  readonly placeholder = input('');
  readonly label = input<string>();
  readonly value = model('');
  readonly searched = output<string>();
  private timer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.timer));
  }

  protected onInput(value: string): void {
    this.value.set(value);
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.searched.emit(value.trim()), 300);
  }

  emitNow(): void {
    clearTimeout(this.timer);
    this.searched.emit(this.value().trim());
  }
}
