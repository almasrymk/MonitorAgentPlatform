import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** A count coloured by the highest severity (open alerts on a device card). */
@Component({
  selector: 'mc-count-badge',
  template: `{{ count() }}`,
  host: { '[attr.data-severity]': 'severity().toLowerCase()', '[attr.aria-label]': 'label()', role: 'status' },
  styles: `
    :host { display: inline-flex; align-items: center; justify-content: center; min-inline-size: 22px; block-size: 22px; padding: 0 6px; border-radius: var(--mc-radius-pill); font-size: var(--mc-fs-xs); font-weight: var(--mc-fw-semibold); background: var(--mc-neutral-soft); color: var(--mc-neutral); }
    :host([data-severity='critical']) { background: var(--mc-danger-soft); color: var(--mc-danger); }
    :host([data-severity='warning']) { background: var(--mc-warning-soft); color: var(--mc-warning); }
    :host([data-severity='info']) { background: var(--mc-info-soft); color: var(--mc-info); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CountBadge {
  readonly count = input(0);
  readonly severity = input('None');
  readonly label = input('');
}
