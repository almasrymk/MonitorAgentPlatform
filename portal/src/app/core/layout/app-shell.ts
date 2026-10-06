import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';

import { I18nService } from '../i18n/i18n.service';
import { TranslatePipe } from '../i18n/translate.pipe';
import { APP_VERSION } from '../version';

/**
 * The page frame: top bar, contextual sidebar and content. Navigation, the user menu and the breadcrumb are added
 * in M1 (07 section 3).
 */
@Component({
  selector: 'mc-app-shell',
  imports: [RouterOutlet, TranslatePipe],
  templateUrl: './app-shell.html',
  styleUrl: './app-shell.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AppShell {
  private readonly i18n = inject(I18nService);
  protected readonly versionText = computed(() => this.i18n.t('app.version', { version: APP_VERSION }));
}
