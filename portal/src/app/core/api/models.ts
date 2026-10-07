import type { components } from './schema';

type Schemas = components['schemas'];

export type UserProfile = Schemas['UserProfileDto'];
export type AuthResult = Schemas['AuthResultDto'];
export type TenantCard = Schemas['TenantCardDto'];
export type Tenant = Schemas['TenantDto'];
export type TenantsSummary = Schemas['TenantsSummaryDto'];
export type LocationCard = Schemas['LocationCardDto'];
export type Location = Schemas['LocationDto'];
export type LocationRequest = Schemas['LocationRequest'];
export type UserListItem = Schemas['UserListItemDto'];
export type Role = Schemas['RoleDto'];
export type AuditRecord = Schemas['AuditRecordDto'];
export type DeviceListItem = Schemas['DeviceListItemDto'];
export type DeviceDetails = Schemas['DeviceDto'];
export type DevicesSummary = Schemas['DevicesSummaryDto'];
export type EnrollmentCodeCreated = Schemas['EnrollmentCodeCreatedDto'];
export type EnrollmentCode = Schemas['EnrollmentCodeDto'];
export type KpiTileData = Schemas['KpiTileDto'];
export type HealthBreakdown = Schemas['HealthBreakdownDto'];
export type NamedCount = Schemas['NamedCountDto'];
export type ProblemDevice = Schemas['ProblemDeviceDto'];
export type LocationStatusRow = Schemas['LocationStatusRowDto'];
export type TenantDashboard = Schemas['TenantDashboardDto'];
export type LocationDashboard = Schemas['LocationDashboardDto'];
export type PlatformDashboard = Schemas['PlatformDashboardDto'];

export interface Paged<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

/** RFC 7807 problem with the platform's `code` and `traceId` (06 section 6). */
export interface Problem {
  status: number;
  code: string;
  title?: string;
  detail?: string;
  traceId?: string;
  errors?: Record<string, string[]>;
  retryAfterSeconds?: number;
  feature?: string;
}

/** Query parameters without empty values. */
export function query(params: Record<string, string | number | boolean | null | undefined>): Record<string, string> {
  const result: Record<string, string> = {};
  for (const [key, value] of Object.entries(params)) {
    if (value !== null && value !== undefined && value !== '') {
      result[key] = String(value);
    }
  }
  return result;
}
