import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';

import { I18nService } from '../../../core/i18n/i18n.service';
import { Card } from '../../../shared/ui/card';
import { DeviceContext } from './device-context';

/** About tab (07 section 5.7): agent and protocol version, enrolment, masked fingerprint, device id, configuration. */
@Component({
  selector: 'mc-device-about-page',
  imports: [DatePipe, Card],
  template: `
    @if (context.device(); as d) {
      <mc-card [title]="i18n.t('device.tab.about')" data-testid="device-about">
        <dl class="kv">
          <dt>{{ i18n.t('device.agentVersion') }}</dt><dd>{{ d.agentVersion ?? '—' }}</dd>
          <dt>{{ i18n.t('device.protocolVersion') }}</dt><dd>{{ d.protocolVersion }}</dd>
          <dt>{{ i18n.t('device.enrolledAt') }}</dt><dd>{{ d.enrolledAt | date: 'medium' : undefined : locale() }}</dd>
          <dt>{{ i18n.t('device.fingerprint') }}</dt><dd class="mono" data-testid="fingerprint">{{ masked() }}</dd>
          <dt>{{ i18n.t('device.deviceId') }}</dt><dd class="mono">{{ d.id }}</dd>
          <dt>{{ i18n.t('device.hostname') }}</dt><dd>{{ d.hostname }}</dd>
          <dt>{{ i18n.t('device.os') }}</dt><dd>{{ d.osName ?? d.osFamily }} {{ d.osVersion ?? '' }} {{ d.architecture ?? '' }}</dd>
          <dt>{{ i18n.t('device.lastConfig') }}</dt><dd>{{ d.appliedConfigVersion ?? '—' }}</dd>
          <dt>{{ i18n.t('devices.license') }}</dt><dd>{{ i18n.t('status.' + d.licenseState.toLowerCase()) }}@if (d.licenseReason) { ({{ d.licenseReason }}) }</dd>
        </dl>
      </mc-card>
    }
  `,
  styles: `
    .kv { display: grid; grid-template-columns: max-content 1fr; gap: var(--mc-space-2) var(--mc-space-5); margin: 0; }
    dt { color: var(--mc-text-muted); }
    dd { margin: 0; }
    .mono { font-family: var(--mc-font-mono); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DeviceAboutPage {
  protected readonly i18n = inject(I18nService);
  protected readonly context = inject(DeviceContext);
  protected readonly locale = computed(() => (this.i18n.language() === 'ar' ? 'ar-EG' : 'en-GB'));

  /** The fingerprint is masked: the first 4 and last 4 characters only (07 section 5.7). */
  protected readonly masked = computed(() => {
    const fp = this.context.device()?.fingerprint ?? '';
    return fp.length <= 8 ? '••••' : `${fp.slice(0, 4)}••••${fp.slice(-4)}`;
  });
}
