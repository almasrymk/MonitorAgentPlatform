import { Injectable, signal } from '@angular/core';

/** Names for dynamic breadcrumb keys such as `:location`, set by the page that knows them. */
@Injectable({ providedIn: 'root' })
export class BreadcrumbLabels {
  readonly values = signal<Record<string, string>>({});

  set(key: string, value: string): void {
    this.values.update((values) => (values[key] === value ? values : { ...values, [key]: value }));
  }
}