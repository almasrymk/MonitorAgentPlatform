import { DeviceListItem } from '../core/api/models';

/** A device list item for component tests. */
export const device = (overrides: Partial<DeviceListItem> = {}): DeviceListItem => ({
  id: 'd1', name: 'WEB-SRV-01', osFamily: 'Windows', osName: 'Windows Server 2019', localIp: '192.168.1.10', connection: 'Online', health: 'Critical',
  licenseState: 'Licensed', cpu: 91.5, ram: 70, disk: 40, lastSeenAt: new Date(Date.now() - 30_000).toISOString(), uptimeSeconds: 3 * 86400 + 4 * 3600,
  openAlerts: 3, openAlertSeverity: 'Critical', locationId: 'l1', locationName: 'Cairo HQ', ...overrides,
});
