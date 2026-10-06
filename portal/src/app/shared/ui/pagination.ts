import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';

import { I18nService } from '../../core/i18n/i18n.service';
import { Icon } from './icon';

@Component({
  selector: 'mc-pagination',
  imports: [Icon],
  template: `
    <span class="summary">{{ summary() }}</span>
    <button type="button" class="nav" [disabled]="page() <= 1" [attr.aria-label]="i18n.t('common.previous')" (click)="pageChange.emit(page() - 1)">
      <mc-icon name="chevronLeft" [size]="16" />
    </button>
    <span class="current">{{ i18n.t('common.pageOf', { page: page(), pages: pages() }) }}</span>
    <button type="button" class="nav" [disabled]="page() >= pages()" [attr.aria-label]="i18n.t('common.next')" (click)="pageChange.emit(page() + 1)">
      <mc-icon name="chevronRight" [size]="16" />
    </button>
  `,
  styles: `
    :host { display: flex; align-items: center; justify-content: flex-end; gap: var(--mc-space-3); color: var(--mc-text-muted); font-size: var(--mc-fs-sm); padding-block: var(--mc-space-3); }
    .summary { margin-inline-end: auto; }
    .nav { display: inline-flex; padding: 4px; background: var(--mc-bg-card-raised); border: 1px solid var(--mc-border); border-radius: var(--mc-radius-sm); color: var(--mc-text); cursor: pointer; }
    .nav:disabled { opacity: 0.4; cursor: default; }
    :host-context([dir='rtl']) mc-icon { transform: scaleX(-1); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Pagination {
  protected readonly i18n = inject(I18nService);
  readonly page = input.required<number>();
  readonly pageSize = input.required<number>();
  readonly total = input.required<number>();
  readonly pageChange = output<number>();

  readonly pages = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize())));
  protected readonly summary = computed(() => {
    const from = this.total() === 0 ? 0 : (this.page() - 1) * this.pageSize() + 1;
    const to = Math.min(this.total(), this.page() * this.pageSize());
    return this.i18n.t('common.showing', { from, to, total: this.total() });
  });
}
