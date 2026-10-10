import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, ElementRef, computed, effect, inject, input, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { DevicesApi } from '../../../core/api/api.services';
import { DeviceCommand } from '../../../core/api/models';
import { AuthService } from '../../../core/auth/auth.service';
import { I18nService } from '../../../core/i18n/i18n.service';
import { ToastService } from '../../../core/ui/toast.service';
import { Button } from '../../../shared/ui/button';
import { Dialog } from '../../../shared/ui/dialog';
import { Drawer } from '../../../shared/ui/drawer';
import { StatusPill } from '../../../shared/ui/status-pill';

/** The six command types of 05 section 9; the service ones need a service name. */
export const COMMAND_TYPES = ['refresh-inventory', 'run-speed-test', 'restart-agent', 'service-start', 'service-stop', 'service-restart'] as const;
const SERVICE_NAME = /^[A-Za-z0-9_.@-]{1,128}$/;

/**
 * Remote Actions on the device header (MC-1103, 07 section 2 rule 10): present only when the plan feature, the
 * `devices.manage` permission and the device setting allow it. Every action asks for a reason (recorded in the audit log)
 * and, for service actions, the service name; "Command history" lists what was sent and what the device answered.
 */
@Component({
  selector: 'mc-remote-actions',
  imports: [DatePipe, Button, Dialog, Drawer, StatusPill],
  template: `
    @if (allowed()) {
      <div class="wrap">
        <button type="button" mcButton="primary-outline" data-testid="remote-actions" aria-haspopup="menu" [attr.aria-expanded]="menuOpen()" (click)="menuOpen.set(!menuOpen())">
          {{ i18n.t('remote.button') }}
        </button>
        @if (menuOpen()) {
          <ul class="items" role="menu" tabindex="-1" (keydown.escape)="menuOpen.set(false)">
            @for (type of types; track type) {
              <li role="none"><button type="button" role="menuitem" [attr.data-testid]="'remote-' + type" (click)="choose(type)">{{ label(type) }}</button></li>
            }
            <li role="separator" class="sep"></li>
            <li role="none"><button type="button" role="menuitem" data-testid="remote-history" (click)="showHistory()">{{ i18n.t('remote.history') }}</button></li>
          </ul>
        }
      </div>

      <mc-dialog [(open)]="dialogOpen" [title]="i18n.t('remote.confirmTitle', { action: label(type()), device: deviceName() })">
        <p class="message">{{ i18n.t('remote.confirmMessage') }}</p>
        @if (needsService()) {
          <label class="field">
            <span>{{ i18n.t('remote.service') }}</span>
            <input type="text" [value]="service()" (input)="service.set($any($event.target).value)" data-testid="remote-service" />
            <small class="hint">{{ i18n.t('remote.serviceHint') }}</small>
          </label>
        }
        <label class="field">
          <span>{{ i18n.t('common.reason') }}</span>
          <textarea rows="3" [value]="reason()" (input)="reason.set($any($event.target).value)" data-testid="reason"></textarea>
          <small class="hint">{{ i18n.t('common.reasonHint', { min: 10 }) }}</small>
        </label>
        <div dialogActions>
          <button type="button" mcButton="secondary" (click)="dialogOpen.set(false)">{{ i18n.t('common.cancel') }}</button>
          <button type="button" mcButton="primary-solid" [disabled]="!valid() || busy()" data-testid="confirm" (click)="send()">{{ i18n.t('remote.send') }}</button>
        </div>
      </mc-dialog>

      <mc-drawer [(open)]="historyOpen" [title]="i18n.t('remote.history')">
        @if (history().length === 0) {
          <p class="empty" data-testid="remote-history-empty">{{ i18n.t('remote.empty') }}</p>
        } @else {
          <ol class="history" data-testid="remote-history-list">
            @for (c of history(); track c.id) {
              <li>
                <div class="row">
                  <strong>{{ label(c.type) }}@if (c.service) { <span class="mono"> {{ c.service }}</span> }</strong>
                  <mc-status-pill [status]="c.status" />
                </div>
                <div class="meta">{{ c.requestedAt | date: 'medium' : undefined : locale() }} · {{ i18n.t('remote.requestedBy', { name: c.requestedByName }) }}</div>
                <div class="reason">{{ c.reason }}</div>
                @if (c.output) {
                  <div class="output"><span>{{ i18n.t('remote.output') }}:</span> {{ c.output }}</div>
                }
              </li>
            }
          </ol>
        }
      </mc-drawer>
    }
  `,
  host: { '(document:click)': 'outside($event)' },
  styles: `
    .wrap { position: relative; display: inline-flex; }
    .items { position: absolute; inset-block-start: 100%; inset-inline-end: 0; z-index: var(--mc-z-topbar); min-inline-size: 200px; margin: 4px 0 0; padding: 4px; list-style: none; background: var(--mc-bg-card-raised); border-radius: var(--mc-radius-md); box-shadow: 0 0 0 1px var(--mc-border-strong), 0 12px 24px rgba(0, 0, 0, 0.35); }
    .items button { inline-size: 100%; text-align: start; background: none; border: 0; color: var(--mc-text); font: inherit; font-size: var(--mc-fs-sm); padding: 6px 10px; border-radius: var(--mc-radius-sm); cursor: pointer; }
    .items button:hover { background: var(--mc-bg-hover); }
    .sep { block-size: 1px; margin: 4px 0; background: var(--mc-border); }
    .message { margin: 0 0 var(--mc-space-4); color: var(--mc-text-secondary); }
    .field { display: flex; flex-direction: column; gap: var(--mc-space-1); margin-block-end: var(--mc-space-3); }
    input, textarea { background: var(--mc-bg-input); border: 1px solid var(--mc-border); border-radius: var(--mc-radius-md); color: var(--mc-text); font: inherit; padding: var(--mc-space-2); }
    textarea { resize: vertical; }
    .hint, .meta { color: var(--mc-text-faint); font-size: var(--mc-fs-xs); }
    .empty { color: var(--mc-text-muted); margin: 0; }
    .history { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: var(--mc-space-3); }
    .history li { padding: var(--mc-space-3); border-radius: var(--mc-radius-md); background: var(--mc-bg-card-raised); display: flex; flex-direction: column; gap: var(--mc-space-1); }
    .row { display: flex; align-items: center; justify-content: space-between; gap: var(--mc-space-2); }
    .reason { color: var(--mc-text-secondary); font-size: var(--mc-fs-sm); }
    .output { font-size: var(--mc-fs-sm); }
    .output span { color: var(--mc-text-muted); }
    .mono { font-family: var(--mc-font-mono); font-weight: normal; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RemoteActions {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(DevicesApi);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly host = inject(ElementRef<HTMLElement>);

  readonly deviceId = input.required<string>();
  readonly deviceName = input('');

  protected readonly types = COMMAND_TYPES;
  protected readonly available = signal(false);
  protected readonly allowed = computed(() => this.available() && this.auth.hasPermission('devices.manage'));
  protected readonly menuOpen = signal(false);
  protected readonly dialogOpen = signal(false);
  protected readonly historyOpen = signal(false);
  protected readonly type = signal<string>(COMMAND_TYPES[0]);
  protected readonly service = signal('');
  protected readonly reason = signal('');
  protected readonly busy = signal(false);
  protected readonly history = signal<DeviceCommand[]>([]);
  protected readonly locale = computed(() => (this.i18n.language() === 'ar' ? 'ar-EG' : 'en-GB'));
  protected readonly needsService = computed(() => this.type().startsWith('service-'));
  protected readonly valid = computed(
    () => this.reason().trim().length >= 10 && this.reason().trim().length <= 500 && (!this.needsService() || SERVICE_NAME.test(this.service().trim())),
  );

  constructor() {
    effect(() => {
      const id = this.deviceId();
      this.available.set(false);
      // Only users who may act ask; a failure leaves the button hidden.
      if (this.auth.hasPermission('devices.manage')) {
        firstValueFrom(this.api.remoteActions(id)).then(
          (r) => this.available.set(r.available),
          () => this.available.set(false),
        );
      }
    });
  }

  protected label(type: string): string {
    return this.i18n.t(`remote.type.${type}`);
  }

  protected choose(type: string): void {
    this.menuOpen.set(false);
    this.type.set(type);
    this.service.set('');
    this.reason.set('');
    this.dialogOpen.set(true);
  }

  async send(): Promise<void> {
    if (!this.valid() || this.busy()) {
      return;
    }
    this.busy.set(true);
    try {
      await firstValueFrom(
        this.api.sendCommand(this.deviceId(), { type: this.type(), service: this.needsService() ? this.service().trim() : null, reason: this.reason().trim() }),
      );
      this.dialogOpen.set(false);
      this.toast.success(this.i18n.t('remote.sent', { action: this.label(this.type()) }));
    } catch {
      // The error interceptor shows the reason.
    } finally {
      this.busy.set(false);
    }
  }

  async showHistory(): Promise<void> {
    this.menuOpen.set(false);
    try {
      this.history.set((await firstValueFrom(this.api.commands(this.deviceId()))).items);
    } catch {
      this.history.set([]);
    }
    this.historyOpen.set(true);
  }

  protected outside(event: Event): void {
    if (this.menuOpen() && !(this.host.nativeElement as HTMLElement).contains(event.target as Node)) {
      this.menuOpen.set(false);
    }
  }
}
