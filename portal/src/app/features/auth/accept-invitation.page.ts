import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { AuthApi } from '../../core/api/api.services';
import { toProblem } from '../../core/api/problem';
import { I18nService } from '../../core/i18n/i18n.service';
import { Button } from '../../shared/ui/button';

/** Invited users set their password (06 section 1). */
@Component({
  selector: 'mc-accept-invitation-page',
  imports: [FormsModule, Button, RouterLink],
  template: `
    <main class="page">
      <form class="card mc-form" (ngSubmit)="submit()">
        <h1>{{ i18n.t('invitation.title') }}</h1>
        @if (done()) {
          <p>{{ i18n.t('invitation.done') }}</p>
          <a mcButton="primary-solid" routerLink="/login">{{ i18n.t('login.submit') }}</a>
        } @else {
          <p class="hint">{{ i18n.t('invitation.rules') }}</p>
          <label>
            <span>{{ i18n.t('login.password') }}</span>
            <input type="password" name="password" autocomplete="new-password" [(ngModel)]="password" />
          </label>
          <label>
            <span>{{ i18n.t('invitation.confirm') }}</span>
            <input type="password" name="confirm" autocomplete="new-password" [(ngModel)]="confirm" />
          </label>
          @if (error()) {
            <p class="error" role="alert">{{ error() }}</p>
          }
          <button type="submit" mcButton="primary-solid" [disabled]="busy()">{{ i18n.t('invitation.submit') }}</button>
        }
      </form>
    </main>
  `,
  styles: `
    .page { min-block-size: 100vh; display: grid; place-items: center; padding: var(--mc-space-5); }
    .card { inline-size: min(420px, 100%); padding: var(--mc-space-6); background: var(--mc-bg-card); border-radius: var(--mc-radius-lg); box-shadow: var(--mc-shadow-card); }
    h1 { margin: 0; font-size: var(--mc-fs-xl); }
    .hint { margin: 0; color: var(--mc-text-muted); }
    .error { margin: 0; color: var(--mc-danger); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AcceptInvitationPage {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(AuthApi);
  private readonly token = inject(ActivatedRoute).snapshot.queryParamMap.get('token') ?? '';
  protected password = '';
  protected confirm = '';
  protected readonly busy = signal(false);
  protected readonly done = signal(false);
  protected readonly error = signal<string | null>(null);

  async submit(): Promise<void> {
    if (this.password !== this.confirm) {
      this.error.set(this.i18n.t('invitation.mismatch'));
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    try {
      await firstValueFrom(this.api.acceptInvitation(this.token, this.password));
      this.done.set(true);
    } catch (e) {
      const problem = toProblem(e);
      this.error.set(problem.detail && problem.code === 'AUTH_PASSWORD_WEAK' ? problem.detail : this.i18n.errorMessage(problem.code));
    } finally {
      this.busy.set(false);
    }
  }
}
