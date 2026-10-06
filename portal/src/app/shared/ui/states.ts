import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';

import { I18nService } from '../../core/i18n/i18n.service';
import { Button } from './button';
import { Icon } from './icon';

/** Empty data view: icon, sentence and (when the user may create) a primary action. */
@Component({
  selector: 'mc-empty-state',
  imports: [Icon],
  template: `
    <mc-icon [name]="icon()" [size]="32" />
    <p class="title">{{ title() }}</p>
    @if (message()) {
      <p class="message">{{ message() }}</p>
    }
    <ng-content />
  `,
  styles: `
    :host { display: flex; flex-direction: column; align-items: center; gap: var(--mc-space-2); padding: var(--mc-space-6); color: var(--mc-text-muted); text-align: center; }
    .title { margin: 0; color: var(--mc-text); font-weight: var(--mc-fw-medium); }
    .message { margin: 0; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EmptyState {
  readonly icon = input('empty');
  readonly title = input.required<string>();
  readonly message = input<string>();
}

/** Error data view: message and retry. */
@Component({
  selector: 'mc-error-state',
  imports: [Icon, Button],
  template: `
    <mc-icon name="alert" [size]="32" />
    <p class="title">{{ message() ?? i18n.t('common.loadError') }}</p>
    <button type="button" mcButton="secondary" data-testid="retry" (click)="retry.emit()">{{ i18n.t('common.retry') }}</button>
  `,
  host: { role: 'alert' },
  styles: `
    :host { display: flex; flex-direction: column; align-items: center; gap: var(--mc-space-3); padding: var(--mc-space-6); color: var(--mc-danger); text-align: center; }
    .title { margin: 0; color: var(--mc-text); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ErrorState {
  protected readonly i18n = inject(I18nService);
  readonly message = input<string>();
  readonly retry = output();
}
