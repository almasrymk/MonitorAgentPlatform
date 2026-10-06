import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { TenantsApi } from '../api/api.services';
import { Button } from '../../shared/ui/button';
import { Icon } from '../../shared/ui/icon';
import { I18nService } from '../i18n/i18n.service';
import { ScopeStore } from '../state/scope.store';

/** Persistent banner while platform staff work inside a customer's workspace (03 section 5). */
@Component({
  selector: 'mc-workspace-banner',
  imports: [Button, Icon],
  template: `
    @if (scope.workspace(); as workspace) {
      <div class="banner" role="status" data-testid="workspace-banner">
        <mc-icon name="shield" [size]="16" />
        <span>{{ i18n.t('workspace.banner', { customer: workspace.name }) }}</span>
        <button type="button" mcButton="ghost" size="sm" data-testid="leave-workspace" (click)="leave(workspace.id)">{{ i18n.t('workspace.leave') }}</button>
      </div>
    }
  `,
  styles: `
    .banner { display: flex; align-items: center; gap: var(--mc-space-2); padding: var(--mc-space-2) var(--mc-space-5); background: var(--mc-info-soft); color: var(--mc-info); border-block-end: 1px solid var(--mc-border); }
    .banner span { flex: 1; color: var(--mc-text); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WorkspaceBanner {
  protected readonly i18n = inject(I18nService);
  protected readonly scope = inject(ScopeStore);
  private readonly tenants = inject(TenantsApi);
  private readonly router = inject(Router);

  async leave(tenantId: string): Promise<void> {
    try {
      await firstValueFrom(this.tenants.closeWorkspace(tenantId));
    } finally {
      this.scope.leaveWorkspace();
      await this.router.navigateByUrl('/admin/customers');
    }
  }
}
