import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';

import { LocationCard as LocationCardData } from '../../core/api/models';
import { I18nService } from '../../core/i18n/i18n.service';
import { Icon } from './icon';
import { RingGauge } from './ring-gauge';
import { StatusPill } from './status-pill';

/** A location: vertical on the Locations screen, horizontal in Locations Overview (07 sections 5.3 and 5.4). */
@Component({
  selector: 'mc-location-card',
  imports: [RouterLink, Icon, RingGauge, StatusPill],
  template: `
    <a class="card" [routerLink]="link()" [attr.data-variant]="variant()" data-testid="location-card">
      <div class="image" aria-hidden="true"><mc-icon name="location" [size]="28" /></div>
      <div class="body">
        <div class="head">
          <h3>{{ location().isDefault ? i18n.t('locations.unassigned') : location().name }}</h3>
          <mc-status-pill [status]="online() ? 'online' : 'offline'" />
        </div>
        <p class="muted">{{ place() }}</p>
        <dl class="counts">
          <div><dt>{{ i18n.t('locations.devices') }}</dt><dd data-testid="location-devices">{{ location().devices }}</dd></div>
          <div><dt>{{ i18n.t('locations.online') }}</dt><dd>{{ location().online }}</dd></div>
          <div><dt>{{ i18n.t('locations.warning') }}</dt><dd class="warning">{{ location().warning }}</dd></div>
          <div><dt>{{ i18n.t('locations.critical') }}</dt><dd class="danger">{{ location().critical }}</dd></div>
        </dl>
      </div>
      @if (variant() === 'vertical') {
        <div class="health">
          <mc-ring-gauge [value]="location().healthScore" [label]="i18n.t('customers.healthScore')" [size]="64" [tone]="tone()" />
          <div class="bars" aria-hidden="true">
            <span class="bar online" [style.inline-size.%]="share(location().online)"></span>
            <span class="bar warning" [style.inline-size.%]="share(location().warning)"></span>
            <span class="bar critical" [style.inline-size.%]="share(location().critical)"></span>
          </div>
        </div>
      }
    </a>
  `,
  styles: `
    :host { display: block; min-inline-size: 0; }
    .card { display: flex; flex-direction: column; gap: var(--mc-space-3); block-size: 100%; padding: var(--mc-space-4); border-radius: var(--mc-radius-lg); background: var(--mc-bg-card); box-shadow: var(--mc-shadow-card); color: inherit; text-decoration: none; }
    .card:hover { background: var(--mc-bg-hover); }
    .card[data-variant='horizontal'] { flex-direction: row; align-items: center; min-inline-size: 280px; }
    .image { display: flex; align-items: center; justify-content: center; block-size: 72px; border-radius: var(--mc-radius-md); background: var(--mc-bg-card-raised); color: var(--mc-brand); }
    .card[data-variant='horizontal'] .image { inline-size: 72px; flex: none; }
    .body { flex: 1; min-inline-size: 0; }
    .head { display: flex; align-items: center; justify-content: space-between; gap: var(--mc-space-2); }
    h3 { margin: 0; font-size: var(--mc-fs-md); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .muted { margin: 2px 0 var(--mc-space-2); color: var(--mc-text-muted); font-size: var(--mc-fs-xs); }
    .counts { display: grid; grid-template-columns: repeat(4, 1fr); gap: var(--mc-space-2); margin: 0; }
    dt { color: var(--mc-text-faint); font-size: var(--mc-fs-xs); }
    dd { margin: 0; font-weight: var(--mc-fw-semibold); }
    .warning { color: var(--mc-warning); }
    .danger { color: var(--mc-danger); }
    .health { display: flex; align-items: center; gap: var(--mc-space-3); }
    .bars { flex: 1; display: flex; flex-direction: column; gap: 6px; }
    .bar { display: block; block-size: 4px; border-radius: var(--mc-radius-pill); min-inline-size: 2px; }
    .bar.online { background: var(--mc-success); }
    .bar.warning { background: var(--mc-warning); }
    .bar.critical { background: var(--mc-danger); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LocationCard {
  protected readonly i18n = inject(I18nService);
  readonly location = input.required<LocationCardData>();
  readonly link = input.required<string[]>();
  readonly variant = input<'vertical' | 'horizontal'>('vertical');

  readonly online = computed(() => this.location().online > 0);
  readonly place = computed(() => [this.location().city, this.location().country].filter((p) => !!p).join(', ') || '—');
  readonly tone = computed(() => {
    const score = this.location().healthScore ?? 100;
    return score >= 80 ? 'success' : score >= 50 ? 'warning' : 'danger';
  });

  protected share(value: number): number {
    const total = this.location().devices;
    return total === 0 ? 0 : Math.round((value / total) * 100);
  }
}
