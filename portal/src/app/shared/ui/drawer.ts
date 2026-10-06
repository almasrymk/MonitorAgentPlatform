import { ChangeDetectionStrategy, Component, inject, input, model } from '@angular/core';

import { I18nService } from '../../core/i18n/i18n.service';
import { Icon } from './icon';

/** Side panel at the inline end (flips in RTL) for add/edit forms. */
@Component({
  selector: 'mc-drawer',
  imports: [Icon],
  template: `
    @if (open()) {
      <!-- Mouse convenience only: Escape and the close button serve keyboard users. -->
      <!-- eslint-disable-next-line @angular-eslint/template/click-events-have-key-events, @angular-eslint/template/interactive-supports-focus -->
      <div class="backdrop" (click)="open.set(false)"></div>
      <aside class="drawer" role="dialog" aria-modal="true" [attr.aria-label]="title()" (keydown.escape)="open.set(false)">
        <header>
          <h2>{{ title() }}</h2>
          <button type="button" class="close" [attr.aria-label]="i18n.t('common.close')" (click)="open.set(false)"><mc-icon name="close" [size]="16" /></button>
        </header>
        <div class="content"><ng-content /></div>
        <footer><ng-content select="[drawerActions]" /></footer>
      </aside>
    }
  `,
  styles: `
    .backdrop { position: fixed; inset: 0; background: rgba(0, 0, 0, 0.45); z-index: var(--mc-z-drawer); }
    .drawer { position: fixed; inset-block: 0; inset-inline-end: 0; inline-size: min(440px, 100vw); background: var(--mc-bg-card); border-inline-start: 1px solid var(--mc-border-strong); z-index: calc(var(--mc-z-drawer) + 1); display: flex; flex-direction: column; }
    header { display: flex; align-items: center; justify-content: space-between; padding: var(--mc-space-4) var(--mc-space-5); border-block-end: 1px solid var(--mc-border); }
    h2 { margin: 0; font-size: var(--mc-fs-lg); }
    .close { background: none; border: 0; color: var(--mc-text-muted); cursor: pointer; padding: 4px; display: inline-flex; }
    .content { flex: 1; overflow: auto; padding: var(--mc-space-5); }
    footer { display: flex; justify-content: flex-end; gap: var(--mc-space-2); padding: var(--mc-space-4) var(--mc-space-5); border-block-start: 1px solid var(--mc-border); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Drawer {
  protected readonly i18n = inject(I18nService);
  readonly open = model(false);
  readonly title = input.required<string>();
}
