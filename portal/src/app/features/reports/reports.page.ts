import { ChangeDetectionStrategy, Component, inject } from '@angular/core';

import { I18nService } from '../../core/i18n/i18n.service';
import { DeviceContext } from '../devices/device/device-context';
import { LocationContext } from '../locations/location-context';
import { PageHeader } from '../../shared/ui/headers';
import { ReportsPanel } from './reports-panel';

/** Customer Reports (07 section 5.8). */
@Component({
  selector: 'mc-reports-page',
  imports: [PageHeader, ReportsPanel],
  template: `
    <mc-page-header [title]="i18n.t('nav.reports')" [subtitle]="i18n.t('reports.subtitle')" />
    <mc-reports-panel />
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReportsPage {
  protected readonly i18n = inject(I18nService);
}

/** Location > Reports: the same screen for one location. */
@Component({
  selector: 'mc-location-reports-page',
  imports: [ReportsPanel],
  template: `
    @if (context.id()) {
      <mc-reports-panel [locationId]="context.id()" />
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LocationReportsPage {
  protected readonly context = inject(LocationContext);
}

/** Device > Reports: report types for one device and the recent reports (07 section 5.7). */
@Component({
  selector: 'mc-device-reports-page',
  imports: [ReportsPanel],
  template: `
    @if (context.id()) {
      <mc-reports-panel [deviceId]="context.id()" />
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DeviceReportsPage {
  protected readonly context = inject(DeviceContext);
}
