import { Injectable, signal } from '@angular/core';

export type DateRange = '24h' | '7d' | '30d' | '90d';

/** The top-bar date range drives every widget of a screen (07 section 1, correction 4). */
@Injectable({ providedIn: 'root' })
export class DateRangeStore {
  readonly range = signal<DateRange>('7d');
  readonly options: DateRange[] = ['24h', '7d', '30d', '90d'];
}
