import { ChangeDetectionStrategy, Component, inject } from '@angular/core';

import { DevicesBrowser } from '../devices/devices-browser';
import { LocationContext } from './location-context';

/** Location devices (07 section 5.6). */
@Component({
  selector: 'mc-location-devices-page',
  imports: [DevicesBrowser],
  template: `
    @if (context.id()) {
      <mc-devices-browser [locationId]="context.id()" [showSummary]="true" [allowEnroll]="true" (changed)="context.load(context.id())" />
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LocationDevicesPage {
  protected readonly context = inject(LocationContext);
}
