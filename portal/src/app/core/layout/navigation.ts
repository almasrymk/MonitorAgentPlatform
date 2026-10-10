/** Menu items of the contextual sidebar (07 section 3). `path` is relative to the area root. */
export interface NavItem {
  key: string;
  icon: string;
  path: string;
  permission?: string;
}

export const PLATFORM_MENU: NavItem[] = [
  { key: 'nav.dashboard', icon: 'dashboard', path: 'dashboard', permission: 'platform.dashboard.read' },
  { key: 'nav.customers', icon: 'customers', path: 'customers', permission: 'platform.tenants.read' },
  { key: 'nav.plans', icon: 'plans', path: 'plans', permission: 'platform.plans.read' },
  { key: 'nav.notifications', icon: 'bell', path: 'notifications', permission: 'platform.dashboard.read' },
  { key: 'nav.archive', icon: 'archive', path: 'archive', permission: 'platform.tenants.read' },
  { key: 'nav.reports', icon: 'reports', path: 'reports', permission: 'platform.dashboard.read' },
  { key: 'nav.usersRoles', icon: 'users', path: 'users', permission: 'platform.users.manage' },
  { key: 'nav.settings', icon: 'settings', path: 'settings', permission: 'platform.settings.manage' },
];

export const CUSTOMER_MENU: NavItem[] = [
  { key: 'nav.dashboard', icon: 'dashboard', path: 'overview', permission: 'dashboard.read' },
  { key: 'nav.locations', icon: 'location', path: 'locations', permission: 'locations.read' },
  { key: 'nav.devices', icon: 'devices', path: 'devices', permission: 'devices.read' },
  { key: 'nav.subscriptions', icon: 'subscription', path: 'subscription', permission: 'subscription.read' },
  { key: 'nav.reports', icon: 'reports', path: 'reports', permission: 'reports.read' },
  { key: 'nav.users', icon: 'users', path: 'users', permission: 'users.manage' },
  { key: 'nav.archive', icon: 'archive', path: 'archive', permission: 'archive.read' },
  { key: 'nav.settings', icon: 'settings', path: 'settings', permission: 'settings.manage' },
];

/** Inside a location (07 section 3): relative to `{area}/locations/{id}`. */
export const LOCATION_MENU: NavItem[] = [
  { key: 'locations.tabOverview', icon: 'dashboard', path: 'overview', permission: 'dashboard.read' },
  { key: 'locations.tabDevices', icon: 'devices', path: 'devices', permission: 'devices.read' },
  { key: 'nav.notifications', icon: 'bell', path: 'notifications', permission: 'notifications.read' },
  { key: 'nav.archive', icon: 'archive', path: 'archive', permission: 'archive.read' },
  { key: 'nav.reports', icon: 'reports', path: 'reports', permission: 'reports.read' },
  { key: 'nav.settings', icon: 'settings', path: 'settings', permission: 'locations.read' },
];

/** Inside a device (07 section 3): relative to `{area}/devices/{id}`. */
export const DEVICE_MENU: NavItem[] = [
  { key: 'device.tab.overview', icon: 'dashboard', path: 'overview', permission: 'devices.read' },
  { key: 'device.tab.monitorPoints', icon: 'globe', path: 'monitor-points', permission: 'devices.read' },
  { key: 'device.tab.applications', icon: 'list', path: 'applications', permission: 'devices.read' },
  { key: 'device.tab.reports', icon: 'reports', path: 'reports', permission: 'reports.read' },
  { key: 'device.tab.settings', icon: 'settings', path: 'settings', permission: 'devices.read' },
  { key: 'device.tab.about', icon: 'info', path: 'about', permission: 'devices.read' },
];