import { ChangeDetectionStrategy, Component, inject, model } from '@angular/core';

import { I18nService } from '../../core/i18n/i18n.service';
import { Button } from './button';
import { Icon } from './icon';

export type ViewMode = 'grid' | 'list';

/** Grid / list toggle of the card screens. */
@Component({
  selector: 'mc-view-toggle',
  imports: [Button, Icon],
  template: `
    <div class="toggle" role="group" [attr.aria-label]="i18n.t('common.view')">
      <button type="button" mcButton="icon" data-testid="view-grid" [attr.aria-pressed]="view() === 'grid'" [attr.aria-label]="i18n.t('common.grid')" (click)="view.set('grid')"><mc-icon name="grid" /></button>
      <button type="button" mcButton="icon" data-testid="view-list" [attr.aria-pressed]="view() === 'list'" [attr.aria-label]="i18n.t('common.list')" (click)="view.set('list')"><mc-icon name="list" /></button>
    </div>
  `,
  styles: `
    .toggle { display: inline-flex; gap: 2px; padding: 2px; border-radius: var(--mc-radius-md); background: var(--mc-bg-input); }
    button[aria-pressed='true'] { background: var(--mc-bg-nav-active); color: var(--mc-text); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ViewToggle {
  protected readonly i18n = inject(I18nService);
  readonly view = model<ViewMode>('grid');
}
