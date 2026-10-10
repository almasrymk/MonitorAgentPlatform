import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, Directive, TemplateRef, computed, contentChildren, inject, input, model } from '@angular/core';

import { Icon } from './icon';
import { Skeleton } from './skeleton';

export interface Column {
  key: string;
  label: string;
  sortable?: boolean;
  width?: string;
}

export interface CellContext<T> {
  $implicit: T;
}

/** `<ng-template mcCell="name" let-row>...</ng-template>` renders a column's cells. */
@Directive({ selector: 'ng-template[mcCell]' })
export class CellDef {
  readonly mcCell = input.required<string>();
  readonly template = inject<TemplateRef<CellContext<unknown>>>(TemplateRef);
}

/**
 * Table with sortable headers (server sort, `-key` = descending), sticky header and loading/empty states.
 * Paging is a separate `mc-pagination`. Virtual scrolling above 100 rows arrives with the device lists (M3).
 */
@Component({
  selector: 'mc-data-table',
  imports: [NgTemplateOutlet, Icon, Skeleton],
  template: `
    <!-- Focusable so a table wider than its card scrolls with the keyboard (WCAG 2.1.1). -->
    <div class="scroller" tabindex="0">
      <table>
        <thead>
          <tr>
            @for (column of columns(); track column.key) {
              <th scope="col" [style.width]="column.width" [attr.aria-sort]="ariaSort(column)">
                @if (column.sortable) {
                  <button type="button" class="sort" (click)="toggleSort(column)">
                    {{ column.label }}
                    @if (sortKey() === column.key) {
                      <mc-icon [name]="descending() ? 'arrowDown' : 'arrowUp'" [size]="12" />
                    }
                  </button>
                } @else {
                  {{ column.label }}
                }
              </th>
            }
          </tr>
        </thead>
        <tbody>
          @if (loading()) {
            @for (r of skeletonRows; track r) {
              <tr><td [attr.colspan]="columns().length"><mc-skeleton [height]="14" /></td></tr>
            }
          } @else {
            @for (row of rows(); track trackBy()(row)) {
              <tr data-testid="table-row">
                @for (column of columns(); track column.key) {
                  <td>
                    @if (cellTemplate(column.key); as template) {
                      <ng-container *ngTemplateOutlet="template; context: { $implicit: row }" />
                    } @else {
                      {{ text(row, column.key) }}
                    }
                  </td>
                }
              </tr>
            } @empty {
              <tr><td class="empty" [attr.colspan]="columns().length">{{ emptyText() }}</td></tr>
            }
          }
        </tbody>
      </table>
    </div>
  `,
  styles: `
    :host { display: block; }
    .scroller { overflow: auto; max-block-size: var(--mc-table-max-height, none); }
    table { inline-size: 100%; border-collapse: collapse; font-size: var(--mc-fs-md); }
    thead th { position: sticky; inset-block-start: 0; background: var(--mc-bg-card-raised); color: var(--mc-text-muted); font-weight: var(--mc-fw-medium); text-align: start; padding: var(--mc-space-2) var(--mc-space-3); white-space: nowrap; }
    tbody td { padding: var(--mc-space-3); border-block-end: 1px solid var(--mc-border); vertical-align: middle; }
    /* Dashboard widgets (07 section 5): one line per row, long text cut with an ellipsis. */
    :host(.compact) tbody td { padding-block: var(--mc-space-2); white-space: nowrap; max-inline-size: 280px; overflow: hidden; text-overflow: ellipsis; }
    tbody tr:hover td { background: var(--mc-bg-hover); }
    .sort { display: inline-flex; align-items: center; gap: 4px; background: none; border: 0; color: inherit; font: inherit; cursor: pointer; padding: 0; }
    .empty { text-align: center; color: var(--mc-text-muted); padding: var(--mc-space-6); }
  `,
  host: { '[class.compact]': 'compact()' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DataTable<T extends object> {
  readonly columns = input.required<Column[]>();
  readonly rows = input.required<T[]>();
  readonly loading = input(false);
  readonly emptyText = input('');
  /** One line per row (dashboard widgets). */
  readonly compact = input(false);
  readonly trackBy = input<(row: T) => unknown>((row: T) => (row as { id?: unknown }).id ?? row);
  /** Current sort (`name` or `-name`). */
  readonly sort = model<string | null>(null);

  private readonly cells = contentChildren(CellDef);
  protected readonly skeletonRows = [1, 2, 3, 4, 5];
  protected readonly sortKey = computed(() => this.sort()?.replace(/^-/, '') ?? null);
  protected readonly descending = computed(() => this.sort()?.startsWith('-') ?? false);

  protected cellTemplate(key: string): TemplateRef<CellContext<unknown>> | null {
    return this.cells().find((c) => c.mcCell() === key)?.template ?? null;
  }

  protected text(row: T, key: string): string {
    const value = (row as Record<string, unknown>)[key];
    return value === null || value === undefined ? '' : String(value);
  }

  toggleSort(column: Column): void {
    if (!column.sortable) {
      return;
    }
    this.sort.set(this.sortKey() === column.key && !this.descending() ? `-${column.key}` : column.key);
  }

  protected ariaSort(column: Column): string | null {
    if (this.sortKey() !== column.key) {
      return column.sortable ? 'none' : null;
    }
    return this.descending() ? 'descending' : 'ascending';
  }
}
