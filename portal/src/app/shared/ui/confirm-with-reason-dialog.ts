import { ChangeDetectionStrategy, Component, computed, effect, inject, input, model, output, signal } from '@angular/core';

import { I18nService } from '../../core/i18n/i18n.service';
import { Button } from './button';
import { Dialog } from './dialog';

/** Confirmation that needs a written reason (suspend, archive, open workspace - 07 section 1 correction 6). */
@Component({
  selector: 'mc-confirm-reason-dialog',
  imports: [Dialog, Button],
  template: `
    <mc-dialog [(open)]="open" [title]="title()">
      @if (message()) {
        <p class="message">{{ message() }}</p>
      }
      <label class="field">
        <span>{{ i18n.t('common.reason') }}</span>
        <textarea rows="3" [value]="reason()" (input)="reason.set($any($event.target).value)" data-testid="reason"></textarea>
        <small class="hint">{{ i18n.t('common.reasonHint', { min: minLength() }) }}</small>
      </label>
      <div dialogActions>
        <button type="button" mcButton="secondary" (click)="open.set(false)">{{ i18n.t('common.cancel') }}</button>
        <button type="button" [mcButton]="danger() ? 'danger-outline' : 'primary-solid'" [disabled]="!valid() || busy()" data-testid="confirm" (click)="confirm()">
          {{ confirmLabel() }}
        </button>
      </div>
    </mc-dialog>
  `,
  styles: `
    .message { margin: 0 0 var(--mc-space-4); color: var(--mc-text-secondary); }
    .field { display: flex; flex-direction: column; gap: var(--mc-space-1); }
    textarea { background: var(--mc-bg-input); border: 1px solid var(--mc-border); border-radius: var(--mc-radius-md); color: var(--mc-text); font: inherit; padding: var(--mc-space-2); resize: vertical; }
    .hint { color: var(--mc-text-faint); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ConfirmWithReasonDialog {
  protected readonly i18n = inject(I18nService);
  readonly open = model(false);
  readonly title = input.required<string>();
  readonly message = input<string>();
  readonly confirmLabel = input.required<string>();
  readonly minLength = input(10);
  readonly danger = input(false);
  readonly busy = input(false);
  readonly confirmed = output<string>();

  protected readonly reason = signal('');
  protected readonly valid = computed(() => this.reason().trim().length >= this.minLength());

  constructor() {
    effect(() => {
      if (this.open()) {
        this.reason.set('');
      }
    });
  }

  confirm(): void {
    if (this.valid()) {
      this.confirmed.emit(this.reason().trim());
    }
  }
}
