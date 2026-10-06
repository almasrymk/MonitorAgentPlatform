import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';

import { toProblem } from '../../core/api/problem';
import { AuthService } from '../../core/auth/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { Button } from '../../shared/ui/button';

/** Centred sign-in card with a language switch (07 section 6). */
@Component({
  selector: 'mc-login-page',
  imports: [FormsModule, Button],
  template: `
    <main class="page">
      <form class="card mc-form" (ngSubmit)="submit()" data-testid="login-form">
        <div class="brand">
          <span class="brand-mark" aria-hidden="true"></span>
          <h1>{{ i18n.t('app.name') }}</h1>
        </div>
        <p class="subtitle">{{ i18n.t('login.subtitle') }}</p>
        <label>
          <span>{{ i18n.t('login.email') }}</span>
          <input type="email" name="email" autocomplete="username" required [(ngModel)]="email" data-testid="email" />
        </label>
        <label>
          <span>{{ i18n.t('login.password') }}</span>
          <input type="password" name="password" autocomplete="current-password" required [(ngModel)]="password" data-testid="password" />
        </label>
        @if (error()) {
          <p class="error" role="alert" data-testid="login-error">{{ error() }}</p>
        }
        <button type="submit" mcButton="primary-solid" [disabled]="busy()" data-testid="sign-in">{{ i18n.t('login.submit') }}</button>
        <button type="button" mcButton="ghost" size="sm" class="language" data-testid="login-language" (click)="toggleLanguage()">
          {{ i18n.language() === 'en' ? i18n.t('language.ar') : i18n.t('language.en') }}
        </button>
      </form>
    </main>
  `,
  styles: `
    .page { min-block-size: 100vh; display: grid; place-items: center; padding: var(--mc-space-5); }
    .card { inline-size: min(400px, 100%); padding: var(--mc-space-6); background: var(--mc-bg-card); border-radius: var(--mc-radius-lg); box-shadow: var(--mc-shadow-card); }
    .brand { display: flex; align-items: center; gap: var(--mc-space-3); }
    .brand-mark { inline-size: 28px; block-size: 28px; border-radius: var(--mc-radius-sm); background: var(--mc-brand); }
    h1 { margin: 0; font-size: var(--mc-fs-xl); }
    .subtitle { margin: 0; color: var(--mc-text-muted); }
    .error { margin: 0; color: var(--mc-danger); }
    .language { align-self: center; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginPage {
  protected readonly i18n = inject(I18nService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected email = '';
  protected password = '';
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  async submit(): Promise<void> {
    if (!this.email || !this.password) {
      this.error.set(this.i18n.t('login.required'));
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.auth.login(this.email, this.password);
      const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
      await this.router.navigateByUrl(returnUrl && returnUrl.startsWith('/') && !returnUrl.startsWith('//') ? returnUrl : this.auth.home());
    } catch (e) {
      const problem = toProblem(e);
      this.error.set(
        problem.code === 'AUTH_LOCKED'
          ? this.i18n.t('error.AUTH_LOCKED_MINUTES', { minutes: Math.ceil((problem.retryAfterSeconds ?? 60) / 60) })
          : this.i18n.errorMessage(problem.code),
      );
    } finally {
      this.busy.set(false);
    }
  }

  protected toggleLanguage(): Promise<void> {
    return this.i18n.use(this.i18n.language() === 'en' ? 'ar' : 'en');
  }
}
