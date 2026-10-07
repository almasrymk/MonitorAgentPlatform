import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';

import { LocationsApi } from '../../core/api/api.services';
import { LocationCard, Paged } from '../../core/api/models';
import { toProblem } from '../../core/api/problem';
import { AuthService } from '../../core/auth/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { Button } from '../../shared/ui/button';
import { Drawer } from '../../shared/ui/drawer';
import { PageHeader } from '../../shared/ui/headers';
import { Icon } from '../../shared/ui/icon';
import { Pagination } from '../../shared/ui/pagination';
import { Skeleton } from '../../shared/ui/skeleton';
import { EmptyState, ErrorState } from '../../shared/ui/states';
import { LocationCard as LocationCardView } from '../../shared/ui/location-card';

interface LocationForm {
  id: string | null;
  version: string;
  name: string;
  code: string;
  country: string;
  city: string;
  addressLine: string;
  timeZone: string;
  contactName: string;
  contactEmail: string;
  contactPhone: string;
}

const TIME_ZONES = ['Africa/Cairo', 'Asia/Dubai', 'Asia/Riyadh', 'Asia/Qatar', 'Asia/Kuwait', 'Asia/Muscat', 'Asia/Bahrain', 'Asia/Amman', 'Europe/London', 'UTC'];

/** Locations (07 section 5.4): location cards with device counts and health; a card opens the location overview. */
@Component({
  selector: 'mc-locations-page',
  imports: [FormsModule, PageHeader, Button, Icon, Drawer, LocationCardView, Skeleton, Pagination, EmptyState, ErrorState],
  templateUrl: './locations.page.html',
  styleUrl: './locations.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LocationsPage {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(LocationsApi);
  private readonly auth = inject(AuthService);

  protected readonly timeZones = TIME_ZONES;
  protected readonly page = signal(1);
  protected readonly result = signal<Paged<LocationCard> | null>(null);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly busy = signal(false);
  protected readonly form = signal<LocationForm | null>(null);
  protected readonly formError = signal<string | null>(null);
  protected readonly canManage = computed(() => this.auth.hasPermission('locations.manage'));

  constructor() {
    void this.load();
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.failed.set(false);
    try {
      this.result.set(await firstValueFrom(this.api.list({ page: this.page(), pageSize: 24 })));
    } catch {
      this.failed.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  protected openNew(): void {
    this.formError.set(null);
    this.form.set({ id: null, version: '', name: '', code: '', country: '', city: '', addressLine: '', timeZone: 'Africa/Cairo', contactName: '', contactEmail: '', contactPhone: '' });
  }

  async edit(card: LocationCard): Promise<void> {
    const location = await firstValueFrom(this.api.get(card.id));
    this.formError.set(null);
    this.form.set({
      id: location.id,
      version: location.version,
      name: location.name,
      code: location.code,
      country: location.country ?? '',
      city: location.city ?? '',
      addressLine: location.addressLine ?? '',
      timeZone: location.timeZone,
      contactName: location.contactName ?? '',
      contactEmail: location.contactEmail ?? '',
      contactPhone: location.contactPhone ?? '',
    });
  }

  async save(): Promise<void> {
    const f = this.form();
    if (!f) {
      return;
    }
    this.busy.set(true);
    this.formError.set(null);
    const body = {
      name: f.name, code: f.code, country: f.country || null, city: f.city || null, addressLine: f.addressLine || null,
      timeZone: f.timeZone, contactName: f.contactName || null, contactEmail: f.contactEmail || null, contactPhone: f.contactPhone || null,
    };
    try {
      if (f.id) {
        await firstValueFrom(this.api.update(f.id, body, f.version));
      } else {
        await firstValueFrom(this.api.create(body));
      }
      this.form.set(null);
      await this.load();
    } catch (e) {
      const problem = toProblem(e);
      const fieldError = problem.errors ? Object.entries(problem.errors).map(([k, v]) => `${k}: ${v[0]}`)[0] : undefined;
      this.formError.set(fieldError ?? this.i18n.errorMessage(problem.code));
    } finally {
      this.busy.set(false);
    }
  }
}
