import { ChangeDetectionStrategy, Component, computed, inject, input, model, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';

import { EnrollmentCodesApi } from '../../core/api/api.services';
import { EnrollmentCodeCreated } from '../../core/api/models';
import { I18nService } from '../../core/i18n/i18n.service';
import { Button } from '../../shared/ui/button';
import { CopyButton } from '../../shared/ui/copy-button';
import { Dialog } from '../../shared/ui/dialog';
import { Select, SelectOption } from '../../shared/ui/select';
import { TabItem, Tabs } from '../../shared/ui/tabs';

/**
 * Add Device (07 section 5.6): creates an enrollment code for the location and shows the install command for
 * Windows, Linux and macOS with copy buttons. The code is shown once.
 */
@Component({
  selector: 'mc-add-device-dialog',
  imports: [FormsModule, Dialog, Button, Select, Tabs, CopyButton],
  template: `
    <mc-dialog [open]="open()" (openChange)="setOpen($event)" [title]="i18n.t('devices.addTitle')">
      @if (code(); as c) {
        <p>{{ i18n.t('devices.codeShownOnce') }}</p>
        <div class="code-row">
          <code data-testid="enrollment-code">{{ c.code }}</code>
          <mc-copy-button [text]="c.code" [label]="i18n.t('devices.copyCode')" />
        </div>
        <p class="muted">{{ i18n.t('devices.codeExpires', { date: expires() }) }}</p>
        <mc-tabs [tabs]="osTabs()" [active]="os()" (activeChange)="os.set($event)" />
        <pre class="command" data-testid="install-command">{{ command() }}</pre>
        <mc-copy-button [text]="command()" [label]="i18n.t('devices.copyCommand')" />
        <p class="muted">{{ i18n.t('devices.productKeyHint') }}</p>
      } @else {
        <p>{{ i18n.t('devices.addIntro') }}</p>
        <mc-select [label]="i18n.t('devices.codeLifetime')" [options]="lifetimes()" [value]="hours()" (valueChange)="hours.set($event)" data-testid="code-lifetime" />
      }
      <div dialogActions>
        @if (code()) {
          <button type="button" mcButton="primary-solid" (click)="setOpen(false)">{{ i18n.t('common.close') }}</button>
        } @else {
          <button type="button" mcButton="secondary" (click)="setOpen(false)">{{ i18n.t('common.cancel') }}</button>
          <button type="button" mcButton="primary-solid" data-testid="create-code" [disabled]="busy()" (click)="create()">{{ i18n.t('devices.createCode') }}</button>
        }
      </div>
    </mc-dialog>
  `,
  styles: `
    .code-row { display: flex; align-items: center; gap: var(--mc-space-3); margin-block: var(--mc-space-3); }
    code { font-family: var(--mc-font-mono); font-size: var(--mc-fs-lg); letter-spacing: 0.05em; padding: var(--mc-space-2) var(--mc-space-3); border-radius: var(--mc-radius-md); background: var(--mc-bg-input); }
    .command { white-space: pre-wrap; word-break: break-all; font-family: var(--mc-font-mono); font-size: var(--mc-fs-xs); padding: var(--mc-space-3); border-radius: var(--mc-radius-md); background: var(--mc-bg-input); margin-block: var(--mc-space-3); direction: ltr; text-align: start; }
    .muted { color: var(--mc-text-muted); font-size: var(--mc-fs-sm); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AddDeviceDialog {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(EnrollmentCodesApi);

  readonly open = model(false);
  readonly locationId = input.required<string>();
  readonly code = signal<EnrollmentCodeCreated | null>(null);
  readonly created = output<EnrollmentCodeCreated>();

  protected readonly hours = signal('24');
  protected readonly os = signal('windows');
  protected readonly busy = signal(false);

  protected readonly lifetimes = computed<SelectOption[]>(() =>
    [1, 24, 72, 168, 720].map((h) => ({ value: String(h), label: this.i18n.t(h === 1 ? 'devices.lifetime.hour' : h === 24 ? 'devices.lifetime.day' : 'devices.lifetime.days', { n: h / 24 }) })),
  );
  protected readonly osTabs = computed<TabItem[]>(() => [
    { id: 'windows', label: this.i18n.t('os.windows') },
    { id: 'linux', label: this.i18n.t('os.linux') },
    { id: 'macos', label: this.i18n.t('os.macos') },
  ]);
  protected readonly command = computed(() => {
    const c = this.code();
    if (!c) {
      return '';
    }
    return this.os() === 'linux' ? c.installCommands.linux : this.os() === 'macos' ? c.installCommands.macOs : c.installCommands.windows;
  });
  protected readonly expires = computed(() => {
    const c = this.code();
    return c ? new Date(c.expiresAt).toLocaleString(this.i18n.language() === 'ar' ? 'ar-EG' : 'en-GB', { dateStyle: 'medium', timeStyle: 'short' }) : '';
  });

  async create(): Promise<void> {
    this.busy.set(true);
    try {
      const created = await firstValueFrom(this.api.create(this.locationId(), { expiresInHours: Number(this.hours()) }));
      this.code.set(created);
      this.created.emit(created);
    } catch {
      // The error interceptor shows the problem.
    } finally {
      this.busy.set(false);
    }
  }

  protected setOpen(open: boolean): void {
    this.open.set(open);
    if (!open) {
      // The code is shown once: closing forgets it.
      this.code.set(null);
      this.os.set('windows');
    }
  }
}
