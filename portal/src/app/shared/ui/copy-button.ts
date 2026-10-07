import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';

import { I18nService } from '../../core/i18n/i18n.service';
import { Button } from './button';
import { Icon } from './icon';

/** Copies a text to the clipboard and confirms with "Copied" for two seconds. */
@Component({
  selector: 'mc-copy-button',
  imports: [Button, Icon],
  template: `
    <button type="button" mcButton="secondary" size="sm" data-testid="copy" [attr.aria-label]="label() ?? i18n.t('common.copy')" (click)="copy()">
      <mc-icon [name]="copied() ? 'check' : 'copy'" [size]="14" />
      {{ copied() ? i18n.t('common.copied') : i18n.t('common.copy') }}
    </button>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CopyButton {
  protected readonly i18n = inject(I18nService);
  readonly text = input.required<string>();
  readonly label = input<string>();
  readonly copied = signal(false);

  async copy(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.text());
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 2000);
    } catch {
      this.copied.set(false);
    }
  }
}
