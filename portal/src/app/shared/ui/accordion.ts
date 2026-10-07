import { ChangeDetectionStrategy, Component, input, model } from '@angular/core';

import { Icon } from './icon';

/** One section of an accordion (Hardware & OS); keyboard and screen readers get a real button with `aria-expanded`. */
@Component({
  selector: 'mc-accordion-item',
  imports: [Icon],
  template: `
    <h3>
      <button type="button" [attr.aria-expanded]="open()" data-testid="accordion-toggle" (click)="open.set(!open())">
        <span>{{ title() }}</span>
        <mc-icon [name]="open() ? 'chevronDown' : 'chevronRight'" [size]="16" />
      </button>
    </h3>
    @if (open()) {
      <div class="content"><ng-content /></div>
    }
  `,
  styles: `
    :host { display: block; border-block-end: 1px solid var(--mc-border); }
    h3 { margin: 0; }
    button { inline-size: 100%; display: flex; justify-content: space-between; align-items: center; background: none; border: 0; color: var(--mc-text); font: inherit; font-weight: var(--mc-fw-medium); padding: var(--mc-space-3) 0; cursor: pointer; text-align: start; }
    .content { padding-block-end: var(--mc-space-3); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AccordionItem {
  readonly title = input.required<string>();
  readonly open = model(false);
}

export interface TimelineItem {
  at: string;
  title: string;
  description?: string | null;
  tone?: 'success' | 'warning' | 'danger' | 'info' | 'neutral';
}

/** A vertical timeline (Recent Activity, messages). */
@Component({
  selector: 'mc-timeline',
  template: `
    <ol>
      @for (item of items(); track $index) {
        <li [attr.data-tone]="item.tone ?? 'neutral'" data-testid="timeline-item">
          <span class="dot" aria-hidden="true"></span>
          <div>
            <strong>{{ item.title }}</strong>
            @if (item.description) {
              <p>{{ item.description }}</p>
            }
            <time [attr.datetime]="item.at">{{ item.at }}</time>
          </div>
        </li>
      }
    </ol>
  `,
  styles: `
    ol { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: var(--mc-space-3); }
    li { display: grid; grid-template-columns: 12px 1fr; gap: var(--mc-space-2); }
    .dot { inline-size: 8px; block-size: 8px; border-radius: 50%; margin-block-start: 6px; background: var(--mc-neutral); }
    li[data-tone='success'] .dot { background: var(--mc-success); }
    li[data-tone='warning'] .dot { background: var(--mc-warning); }
    li[data-tone='danger'] .dot { background: var(--mc-danger); }
    li[data-tone='info'] .dot { background: var(--mc-info); }
    p { margin: 2px 0; color: var(--mc-text-secondary); font-size: var(--mc-fs-sm); }
    time { color: var(--mc-text-faint); font-size: var(--mc-fs-xs); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Timeline {
  readonly items = input.required<TimelineItem[]>();
}
