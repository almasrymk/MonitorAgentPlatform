import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { ConfigDocument } from '../../core/api/monitoring.api';
import { I18nService } from '../../core/i18n/i18n.service';

/** A deep copy the form can edit in place. */
export function editable(document: ConfigDocument): ConfigDocument {
  return JSON.parse(JSON.stringify(document)) as ConfigDocument;
}

/** Numbers typed in the form arrive as strings or empty values; the API needs numbers (and null for an empty clear level). */
export function normalised(document: ConfigDocument): ConfigDocument {
  const usage = (t: ConfigDocument['thresholds']['cpu']) => ({
    warningPercent: Number(t.warningPercent),
    criticalPercent: Number(t.criticalPercent),
    forSeconds: Number(t.forSeconds),
    clearBelowPercent: t.clearBelowPercent === null || t.clearBelowPercent === undefined || `${t.clearBelowPercent}` === '' ? null : Number(t.clearBelowPercent),
  });
  return {
    telemetry: { sampleSeconds: Number(document.telemetry.sampleSeconds) },
    thresholds: {
      cpu: usage(document.thresholds.cpu),
      ram: usage(document.thresholds.ram),
      disk: usage(document.thresholds.disk),
      tempC: { critical: Number(document.thresholds.tempC.critical), forSeconds: Number(document.thresholds.tempC.forSeconds) },
    },
    features: { remoteActions: !!document.features.remoteActions },
  };
}

/** Threshold and interval fields of a configuration document (05 section 8), used by the device Settings tab and Settings > Monitoring. */
@Component({
  selector: 'mc-thresholds-form',
  imports: [FormsModule],
  template: `
    @let d = document();
    <table class="grid" data-testid="thresholds">
      <thead>
        <tr>
          <th scope="col"></th>
          <th scope="col">{{ i18n.t('config.warning') }}</th>
          <th scope="col">{{ i18n.t('config.critical') }}</th>
          <th scope="col">{{ i18n.t('config.forSeconds') }}</th>
          <th scope="col">{{ i18n.t('config.clearBelow') }}</th>
        </tr>
      </thead>
      <tbody>
        @for (row of rows; track row) {
          <tr>
            <th scope="row">{{ i18n.t('config.metric.' + row) }}</th>
            <td><input type="number" min="1" max="100" [name]="row + '-warning'" [(ngModel)]="d.thresholds[row].warningPercent" [disabled]="readonly()" [attr.data-testid]="row + '-warning'" /> %</td>
            <td><input type="number" min="1" max="100" [name]="row + '-critical'" [(ngModel)]="d.thresholds[row].criticalPercent" [disabled]="readonly()" [attr.data-testid]="row + '-critical'" /> %</td>
            <td><input type="number" min="0" max="3600" [name]="row + '-for'" [(ngModel)]="d.thresholds[row].forSeconds" [disabled]="readonly()" /> s</td>
            <td><input type="number" min="0" max="100" [name]="row + '-clear'" [(ngModel)]="d.thresholds[row].clearBelowPercent" [disabled]="readonly()" /> %</td>
          </tr>
        }
        <tr>
          <th scope="row">{{ i18n.t('config.metric.temp') }}</th>
          <td></td>
          <td><input type="number" min="30" max="120" name="temp-critical" [(ngModel)]="d.thresholds.tempC.critical" [disabled]="readonly()" /> °C</td>
          <td><input type="number" min="0" max="3600" name="temp-for" [(ngModel)]="d.thresholds.tempC.forSeconds" [disabled]="readonly()" /> s</td>
          <td></td>
        </tr>
      </tbody>
    </table>
    <label class="sample">
      <span>{{ i18n.t('config.sampleSeconds') }}</span>
      <input type="number" min="1" max="30" name="sample" [(ngModel)]="d.telemetry.sampleSeconds" [disabled]="readonly()" /> s
    </label>
  `,
  styles: `
    .grid { inline-size: 100%; border-collapse: collapse; }
    th, td { padding: var(--mc-space-2); text-align: start; border-block-end: 1px solid var(--mc-border); font-weight: var(--mc-fw-regular); }
    thead th { color: var(--mc-text-muted); font-size: var(--mc-fs-sm); }
    tbody th { font-weight: var(--mc-fw-medium); }
    input { inline-size: 80px; block-size: 32px; padding-inline: var(--mc-space-2); background: var(--mc-bg-input); color: var(--mc-text); border: 1px solid var(--mc-border); border-radius: var(--mc-radius-sm); font: inherit; }
    .sample { display: flex; align-items: center; gap: var(--mc-space-2); margin-block-start: var(--mc-space-4); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ThresholdsForm {
  protected readonly i18n = inject(I18nService);
  /** Edited in place; read it back after the user saves. */
  readonly document = input.required<ConfigDocument>();
  readonly readonly = input(false);
  protected readonly rows = ['cpu', 'ram', 'disk'] as const;
}
