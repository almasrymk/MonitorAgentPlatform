import { ChangeDetectionStrategy, Component, inject } from '@angular/core';

import { ToastService } from '../../core/ui/toast.service';
import { Icon } from './icon';

@Component({
  selector: 'mc-toast-host',
  imports: [Icon],
  template: `
    <div class="stack" aria-live="polite">
      @for (toast of toasts.toasts(); track toast.id) {
        <div class="toast" [attr.data-kind]="toast.kind" role="status" data-testid="toast">
          <mc-icon [name]="toast.kind === 'error' ? 'alert' : toast.kind === 'success' ? 'check' : 'info'" [size]="16" />
          <span>{{ toast.message }}</span>
          <button type="button" aria-label="Dismiss" (click)="toasts.dismiss(toast.id)"><mc-icon name="close" [size]="14" /></button>
        </div>
      }
    </div>
  `,
  styles: `
    .stack { position: fixed; inset-block-end: var(--mc-space-5); inset-inline-end: var(--mc-space-5); display: flex; flex-direction: column; gap: var(--mc-space-2); z-index: var(--mc-z-toast); }
    .toast { display: flex; align-items: center; gap: var(--mc-space-2); min-inline-size: 280px; max-inline-size: 420px; padding: var(--mc-space-3) var(--mc-space-4); background: var(--mc-bg-card-raised); border: 1px solid var(--mc-border-strong); border-inline-start: 3px solid var(--mc-info); border-radius: var(--mc-radius-md); }
    .toast[data-kind='error'] { border-inline-start-color: var(--mc-danger); }
    .toast[data-kind='success'] { border-inline-start-color: var(--mc-success); }
    .toast span { flex: 1; }
    button { background: none; border: 0; color: var(--mc-text-muted); cursor: pointer; display: inline-flex; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ToastHost {
  protected readonly toasts = inject(ToastService);
}
