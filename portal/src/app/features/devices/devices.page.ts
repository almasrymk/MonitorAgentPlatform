import { ChangeDetectionStrategy, Component, inject } from '@angular/core';

import { I18nService } from '../../core/i18n/i18n.service';
import { PageHeader } from '../../shared/ui/headers';
import { DevicesBrowser } from './devices-browser';

/** Devices (all) - 07 section 5.8: the location devices screen without the location header, with a Location filter. */
@Component({
  selector: 'mc-devices-page',
  imports: [PageHeader, DevicesBrowser],
  template: `
    <mc-page-header [title]="i18n.t('devices.title')" [subtitle]="i18n.t('devices.subtitle')" />
    <mc-devices-browser [showSummary]="true" [showLocationFilter]="true" />
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DevicesPage {
  protected readonly i18n = inject(I18nService);
}
