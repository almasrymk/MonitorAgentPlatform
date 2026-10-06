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
