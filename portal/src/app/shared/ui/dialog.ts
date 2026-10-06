import { ChangeDetectionStrategy, Component, ElementRef, effect, inject, input, model } from '@angular/core';

import { I18nService } from '../../core/i18n/i18n.service';
import { Icon } from './icon';

/** Modal dialog: Escape and the close button close it; focus moves into it when it opens. */
@Component({
  selector: 'mc-dialog',
  imports: [Icon],
  template: `
    @if (open()) {
      <!-- Mouse convenience only: Escape and the close button serve keyboard users. -->
      <!-- eslint-disable-next-line @angular-eslint/template/click-events-have-key-events, @angular-eslint/template/interactive-supports-focus -->
      <div class="backdrop" (click)="close()"></div>
      <section class="dialog" role="dialog" aria-modal="true" [attr.aria-label]="title()" (keydown.escape)="close()" tabindex="-1">
        <header>
          <h2>{{ title() }}</h2>
          <button type="button" class="close" [attr.aria-label]="i18n.t('common.close')" (click)="close()"><mc-icon name="close" [size]="16" /></button>
        </header>
        <div class="content"><ng-content /></div>
        <footer><ng-content select="[dialogActions]" /></footer>
      </section>
    }
  `,
  styles: `
    .backdrop { position: fixed; inset: 0; background: rgba(0, 0, 0, 0.55); z-index: var(--mc-z-drawer); }
    .dialog { position: fixed; inset-block-start: 50%; inset-inline-start: 50%; transform: translate(-50%, -50%); inline-size: min(520px, calc(100vw - 32px)); background: var(--mc-bg-card); border-radius: var(--mc-radius-lg); box-shadow: 0 0 0 1px var(--mc-border-strong), 0 24px 48px rgba(0, 0, 0, 0.5); z-index: calc(var(--mc-z-drawer) + 1); outline: none; }
    :host-context([dir='rtl']) .dialog { transform: translate(50%, -50%); }
    header { display: flex; align-items: center; justify-content: space-between; padding: var(--mc-space-4) var(--mc-space-5); border-block-end: 1px solid var(--mc-border); }
    h2 { margin: 0; font-size: var(--mc-fs-lg); }
    .close { background: none; border: 0; color: var(--mc-text-muted); cursor: pointer; padding: 4px; display: inline-flex; }
    .content { padding: var(--mc-space-5); }
    footer { display: flex; justify-content: flex-end; gap: var(--mc-space-2); padding: 0 var(--mc-space-5) var(--mc-space-5); }
    footer:empty { display: none; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Dialog {
  protected readonly i18n = inject(I18nService);
  private readonly host = inject(ElementRef<HTMLElement>);
  readonly open = model(false);
  readonly title = input.required<string>();

  constructor() {
    effect(() => {
      if (this.open()) {
        queueMicrotask(() => {
          const root = this.host.nativeElement as HTMLElement;
          const target = root.querySelector<HTMLElement>('input, textarea, select, [autofocus]') ?? root.querySelector<HTMLElement>('.dialog');
          target?.focus();
        });
      }
    });
  }

  close(): void {
    this.open.set(false);
  }
}
