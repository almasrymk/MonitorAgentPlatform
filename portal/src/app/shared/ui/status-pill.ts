import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';

import { I18nService } from '../../core/i18n/i18n.service';

const TONES: Record<string, string> = {
  online: 'success', active: 'success', healthy: 'success', licensed: 'success', succeeded: 'success',
  warning: 'warning', expiring: 'warning', invited: 'info', info: 'info', pending: 'info', sent: 'info',
  critical: 'danger', suspended: 'danger', failed: 'danger', rejected: 'danger',
  offline: 'neutral', inactive: 'neutral', unknown: 'neutral', unlicensed: 'neutral', archived: 'neutral', resolved: 'neutral', closed: 'neutral',
};

/** Status shown with a dot and text, never by colour alone (07 section 4). */
@Component({
  selector: 'mc-status-pill',
  template: `<span class="dot" aria-hidden="true"></span>{{ text() }}`,
  host: { '[attr.data-tone]': 'tone()', role: 'status' },
  styles: `
    :host { display: inline-flex; align-items: center; gap: 6px; padding: 2px 10px; border-radius: var(--mc-radius-pill); font-size: var(--mc-fs-xs); font-weight: var(--mc-fw-medium); white-space: nowrap; background: var(--mc-neutral-soft); color: var(--mc-text-secondary); }
    /* Neutral text uses the secondary text colour for 4.5:1 contrast (ADR 0010); the dot keeps the neutral colour. */
    :host(:not([data-tone])) .dot, :host([data-tone='neutral']) .dot { background: var(--mc-neutral); }
    .dot { inline-size: 6px; block-size: 6px; border-radius: 50%; background: currentColor; }
    :host([data-tone='success']) { background: var(--mc-success-soft); color: var(--mc-success); }
    :host([data-tone='warning']) { background: var(--mc-warning-soft); color: var(--mc-warning); }
    :host([data-tone='danger']) { background: var(--mc-danger-soft); color: var(--mc-danger); }
    :host([data-tone='info']) { background: var(--mc-info-soft); color: var(--mc-info); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StatusPill {
  private readonly i18n = inject(I18nService);
  readonly status = input.required<string>();
  readonly label = input<string>();
  protected readonly key = computed(() => this.status().toLowerCase());
  protected readonly tone = computed(() => TONES[this.key()] ?? 'neutral');
  protected readonly text = computed(() => this.label() ?? this.i18n.t(`status.${this.key()}`));
}
