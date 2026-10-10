import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';

import { TenantCard } from '../../core/api/models';
import { I18nService } from '../../core/i18n/i18n.service';
import { Avatar } from './avatar';
import { Button } from './button';
import { Icon } from './icon';
import { RingGauge } from './ring-gauge';
import { StatusPill } from './status-pill';
import { UsageBar } from './usage-bar';

export type CustomerAction = 'workspace' | 'suspend' | 'archive' | 'reactivate';

/** A customer on the Customers screen (07 section 5.2). */
@Component({
  selector: 'mc-customer-card',
  imports: [Avatar, Button, Icon, RingGauge, StatusPill, UsageBar],
  template: `
    <header>
      <mc-avatar [name]="card().name" [letters]="1" [size]="40" />
      <div class="name">
        <h2>{{ card().name }}</h2>
        <span class="plan" data-testid="plan-name">{{ card().planName ?? i18n.t('customers.planPending') }}</span>
      </div>
      <mc-status-pill [status]="pill()" />
    </header>
    <div class="meta">
      <span><mc-icon name="location" [size]="14" /> {{ i18n.t('dashboard.locationsCount', { n: card().locations }) }}</span>
      <span data-testid="customer-devices" [attr.data-count]="card().devices ?? 0"><mc-icon name="devices" [size]="14" /> {{ i18n.t('dashboard.devicesCount', { n: card().devices ?? 0 }) }}</span>
    </div>
    <dl class="health">
      <div><span class="dot success" aria-hidden="true"></span><dd data-testid="customer-healthy">{{ card().healthy ?? 0 }}</dd><dt>{{ i18n.t('status.healthy') }}</dt></div>
      <div><span class="dot warning" aria-hidden="true"></span><dd>{{ card().warning ?? 0 }}</dd><dt>{{ i18n.t('status.warning') }}</dt></div>
      <div><span class="dot danger" aria-hidden="true"></span><dd data-testid="customer-critical">{{ card().critical ?? 0 }}</dd><dt>{{ i18n.t('status.critical') }}</dt></div>
    </dl>
    <div class="middle">
      <div class="score">
        <mc-ring-gauge [value]="card().healthScore" [label]="i18n.t('customers.healthScore')" [size]="64" [tone]="tone()" />
        <small>{{ i18n.t('customers.healthScore') }}</small>
      </div>
      <dl class="facts">
        <div><dt>{{ i18n.t('customers.licenseUsage') }}</dt><dd><mc-usage-bar [used]="card().licensesUsed ?? 0" [limit]="card().licenseLimit" [label]="i18n.t('customers.licenseUsage')" /></dd></div>
        <div>
          <dt>{{ i18n.t('customers.nextRenewal') }}</dt>
          <dd data-testid="renewal">{{ renewal() }}@if (daysLeft(); as d) { <span class="left">{{ i18n.t('customers.daysLeft', { n: d }) }}</span> }</dd>
        </div>
      </dl>
    </div>    <footer>
      @if (card().status !== 'Archived') {
        <button type="button" mcButton="primary-solid" size="sm" data-testid="open-workspace" (click)="action.emit('workspace')">{{ i18n.t('customers.openWorkspace') }}</button>
      }
      @if (canManage() && card().status !== 'Archived') {
        <button type="button" mcButton="secondary" size="sm" (click)="action.emit('archive')">{{ i18n.t('customers.archive') }}</button>
        @if (card().status === 'Suspended') {
          <button type="button" mcButton="success-outline" size="sm" (click)="action.emit('reactivate')">{{ i18n.t('customers.reactivate') }}</button>
        } @else {
          <button type="button" mcButton="danger-outline" size="sm" (click)="action.emit('suspend')">{{ i18n.t('customers.suspend') }}</button>
        }
      }
    </footer>
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: var(--mc-space-4); background: var(--mc-bg-card); border-radius: var(--mc-radius-lg); box-shadow: var(--mc-shadow-card); padding: var(--mc-space-4); min-inline-size: 0; }
    header { display: flex; align-items: center; gap: var(--mc-space-3); }
    .name { flex: 1; min-inline-size: 0; display: flex; flex-direction: column; }
    h2 { margin: 0; font-size: var(--mc-fs-md); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .plan { color: var(--mc-text-muted); font-size: var(--mc-fs-xs); }
    .meta { display: flex; gap: var(--mc-space-5); color: var(--mc-text-secondary); font-size: var(--mc-fs-sm); }
    .meta span { display: inline-flex; align-items: center; gap: var(--mc-space-1); }
    .health { display: grid; grid-template-columns: repeat(3, 1fr); margin: 0; }
    .health div { display: grid; grid-template-columns: auto 1fr; column-gap: var(--mc-space-2); align-items: center; }
    .health .dot { grid-row: span 2; inline-size: 12px; block-size: 12px; border-radius: 50%; }
    .dot.success { background: var(--mc-success); }
    .dot.warning { background: var(--mc-warning); }
    .dot.danger { background: var(--mc-danger); }
    dt { color: var(--mc-text-muted); font-size: var(--mc-fs-xs); }
    dd { margin: 0; font-weight: var(--mc-fw-semibold); }
    .middle { display: flex; align-items: center; gap: var(--mc-space-4); }
    .score { display: flex; flex-direction: column; align-items: center; gap: var(--mc-space-1); }
    .score small { color: var(--mc-text-muted); font-size: var(--mc-fs-xs); }
    .facts { flex: 1; display: flex; flex-direction: column; gap: var(--mc-space-3); margin: 0; min-inline-size: 0; }
    .left { margin-inline-start: var(--mc-space-2); color: var(--mc-warning); font-size: var(--mc-fs-xs); font-weight: var(--mc-fw-medium); }
    footer { display: flex; gap: var(--mc-space-2); flex-wrap: wrap; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CustomerCard {
  protected readonly i18n = inject(I18nService);
  readonly card = input.required<TenantCard>();
  readonly canManage = input(false);
  readonly action = output<CustomerAction>();

  /** Suspended customer, else "Expiring" when the renewal is within 30 days, else the subscription status. */
  readonly pill = computed(() => {
    const card = this.card();
    if (card.status !== 'Active') {
      return card.status;
    }
    if (card.expiringSoon) {
      return 'expiring';
    }
    return card.subscriptionStatus && card.subscriptionStatus !== 'None' ? card.subscriptionStatus : card.status;
  });

  readonly tone = computed(() => {
    const score = this.card().healthScore ?? 100;
    return score >= 80 ? 'success' : score >= 50 ? 'warning' : 'danger';
  });

  /** Days to the renewal when it is close ("28 days left" on expiring customers). */
  readonly daysLeft = computed(() => {
    const at = this.card().nextRenewal;
    if (!at || !this.card().expiringSoon) {
      return null;
    }
    return Math.max(0, Math.ceil((new Date(at).getTime() - Date.now()) / 86_400_000));
  });

  readonly renewal = computed(() => {
    const at = this.card().nextRenewal;
    return at ? new Date(at).toLocaleDateString(this.i18n.language() === 'ar' ? 'ar-EG' : 'en-GB', { day: 'numeric', month: 'short', year: 'numeric' }) : '—';
  });
}
