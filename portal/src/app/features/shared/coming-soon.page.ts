import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';

import { I18nService } from '../../core/i18n/i18n.service';
import { PageHeader } from '../../shared/ui/headers';
import { EmptyState } from '../../shared/ui/states';

/** A menu entry whose screen arrives in a later milestone (route data `title`, `milestone`). */
@Component({
  selector: 'mc-coming-soon-page',
  imports: [PageHeader, EmptyState],
  template: `
    <mc-page-header [title]="i18n.t(title)" />
    <mc-empty-state icon="info" [title]="i18n.t('comingSoon.title')" [message]="i18n.t('comingSoon.message', { milestone: milestone })" />
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ComingSoonPage {
  protected readonly i18n = inject(I18nService);
  private readonly data = inject(ActivatedRoute).snapshot.data;
  protected readonly title = (this.data['breadcrumb'] as string | undefined) ?? 'app.name';
  protected readonly milestone = (this.data['milestone'] as string | undefined) ?? '';
}
