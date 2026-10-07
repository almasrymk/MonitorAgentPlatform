import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';

import { I18nService } from '../../core/i18n/i18n.service';

const GLYPHS: Record<string, string> = {
  windows: 'M3 5.5l7.5-1v7H3zM11.5 4.3L21 3v8.5h-9.5zM3 12.5h7.5v7L3 18.5zM11.5 12.5H21V21l-9.5-1.3z',
  linux: 'M12 3c-2 0-3 1.8-3 4 0 1.4.4 2.4-.6 3.8C7 12.8 6 14.6 6 16.5 6 19 8.5 21 12 21s6-2 6-4.5c0-1.9-1-3.7-2.4-5.7-1-1.4-.6-2.4-.6-3.8 0-2.2-1-4-3-4zM10.5 7.5h.01M13.5 7.5h.01',
  macos: 'M16.5 12.6c0-2.3 1.9-3.4 2-3.5-1.1-1.6-2.8-1.8-3.4-1.8-1.4-.1-2.8.9-3.5.9s-1.8-.9-3-.8C7 7.4 5.6 8.3 4.8 9.7c-1.7 2.9-.4 7.2 1.2 9.6.8 1.2 1.7 2.4 3 2.4 1.2 0 1.6-.8 3.1-.8s1.8.8 3.1.8 2.1-1.2 2.9-2.4c.9-1.3 1.3-2.6 1.3-2.7 0 0-2.5-1-2.5-3.9zM14.2 5.4c.7-.8 1.1-1.9 1-3-.9 0-2.1.6-2.8 1.4-.6.7-1.2 1.8-1 2.9 1.1.1 2.1-.5 2.8-1.3z',
  other: 'M4 5h16v11H4zM9 20h6M12 16v4',
};

/** Operating system glyph with an accessible name. */
@Component({
  selector: 'mc-os-icon',
  template: `
    <svg [attr.width]="size()" [attr.height]="size()" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" role="img" [attr.aria-label]="name()">
      <path [attr.d]="path()" />
    </svg>
  `,
  host: { '[attr.data-os]': 'key()' },
  styles: `
    :host { display: inline-flex; color: var(--mc-series-other); }
    :host([data-os='windows']) { color: var(--mc-series-windows); }
    :host([data-os='linux']) { color: var(--mc-series-linux); }
    :host([data-os='macos']) { color: var(--mc-series-macos); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OsIcon {
  private readonly i18n = inject(I18nService);
  readonly os = input.required<string>();
  readonly size = input(20);

  protected readonly key = computed(() => {
    const os = this.os().toLowerCase();
    return os in GLYPHS ? os : 'other';
  });
  protected readonly path = computed(() => GLYPHS[this.key()]);
  protected readonly name = computed(() => this.i18n.t(`os.${this.key()}`));
}
