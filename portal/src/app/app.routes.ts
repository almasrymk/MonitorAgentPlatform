import { Routes } from '@angular/router';

import { areaGuard, authGuard, homeRedirectGuard, permissionGuard, workspaceGuard } from './core/auth/guards';
import { AppShell } from './core/layout/app-shell';
import { isDevMode } from '@angular/core';

const soon = (breadcrumb: string, milestone: string, permission?: string) => ({
  loadComponent: () => import('./features/shared/coming-soon.page').then((m) => m.ComingSoonPage),
  data: { breadcrumb, milestone, permission },
  canActivate: [permissionGuard],
});

/** Customer area routes, shared by `/app` (tenant roles) and `/admin/customers/:tenantId` (workspace). */
const customerRoutes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'overview' },
  {
    path: 'overview',
    loadComponent: () => import('./features/dashboard/customer-dashboard.page').then((m) => m.CustomerDashboardPage),
    data: { breadcrumb: 'nav.dashboard', permission: 'dashboard.read' },
    canActivate: [permissionGuard],
  },
  {
    path: 'locations',
    data: { breadcrumb: 'nav.locations' },
    children: [
      {
        path: '',
        loadComponent: () => import('./features/locations/locations.page').then((m) => m.LocationsPage),
        data: { permission: 'locations.read' },
        canActivate: [permissionGuard],
      },
      {
        path: ':id',
        loadComponent: () => import('./features/locations/location.page').then((m) => m.LocationPage),
        data: { breadcrumb: ':location', permission: 'locations.read' },
        canActivate: [permissionGuard],
        children: [
          { path: '', pathMatch: 'full', redirectTo: 'overview' },
          {
            path: 'overview',
            loadComponent: () => import('./features/locations/location-overview.page').then((m) => m.LocationOverviewPage),
            data: { breadcrumb: 'locations.tabOverview', permission: 'dashboard.read' },
            canActivate: [permissionGuard],
          },
          {
            path: 'devices',
            loadComponent: () => import('./features/locations/location-devices.page').then((m) => m.LocationDevicesPage),
            data: { breadcrumb: 'locations.tabDevices', permission: 'devices.read' },
            canActivate: [permissionGuard],
          },
        ],
      },
    ],
  },
  {
    path: 'devices',
    data: { breadcrumb: 'nav.devices' },
    children: [
      {
        path: '',
        loadComponent: () => import('./features/devices/devices.page').then((m) => m.DevicesPage),
        data: { permission: 'devices.read' },
        canActivate: [permissionGuard],
      },
      {
        path: ':deviceId',
        loadComponent: () => import('./features/devices/device/device.page').then((m) => m.DevicePage),
        data: { breadcrumb: ':device', permission: 'devices.read' },
        canActivate: [permissionGuard],
        children: [
          { path: '', pathMatch: 'full', redirectTo: 'overview' },
          { path: 'overview', loadComponent: () => import('./features/devices/device/device-overview.page').then((m) => m.DeviceOverviewPage), data: { breadcrumb: 'device.tab.overview' } },
          { path: 'monitor-points', ...soon('device.tab.monitorPoints', 'M8', 'devices.read') },
          { path: 'applications', loadComponent: () => import('./features/devices/device/device-applications.page').then((m) => m.DeviceApplicationsPage), data: { breadcrumb: 'device.tab.applications' } },
          { path: 'reports', ...soon('device.tab.reports', 'M9', 'reports.read') },
          { path: 'settings', ...soon('device.tab.settings', 'M8', 'devices.read') },
          { path: 'about', loadComponent: () => import('./features/devices/device/device-about.page').then((m) => m.DeviceAboutPage), data: { breadcrumb: 'device.tab.about' } },
        ],
      },
    ],
  },
  {
    path: 'subscription',
    loadComponent: () => import('./features/subscription/subscription.page').then((m) => m.SubscriptionPage),
    data: { breadcrumb: 'nav.subscriptions', permission: 'subscription.read' },
    canActivate: [permissionGuard],
  },
  { path: 'reports', ...soon('nav.reports', 'M9', 'reports.read') },
  {
    path: 'users',
    loadComponent: () => import('./features/users/users.page').then((m) => m.UsersPage),
    data: { breadcrumb: 'nav.users', permission: 'users.manage' },
    canActivate: [permissionGuard],
  },
  { path: 'archive', ...soon('nav.archive', 'M9', 'archive.read') },
  { path: 'settings', ...soon('nav.settings', 'M6', 'settings.manage') },
  { path: 'notifications', ...soon('nav.notifications', 'M6', 'notifications.read') },
];

export const routes: Routes = [
  { path: 'login', loadComponent: () => import('./features/auth/login.page').then((m) => m.LoginPage) },
  { path: 'accept-invitation', loadComponent: () => import('./features/auth/accept-invitation.page').then((m) => m.AcceptInvitationPage) },
  {
    path: 'admin',
    component: AppShell,
    canActivate: [authGuard, areaGuard('platform')],
    data: { breadcrumb: 'nav.platform' },
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      { path: 'dashboard', ...soon('nav.dashboard', 'M3', 'platform.dashboard.read') },
      {
        path: 'customers',
        data: { breadcrumb: 'nav.customers' },
        children: [
          {
            path: '',
            loadComponent: () => import('./features/platform/customers.page').then((m) => m.CustomersPage),
            data: { permission: 'platform.tenants.read' },
            canActivate: [permissionGuard],
          },
          { path: ':tenantId', canActivate: [workspaceGuard], data: { breadcrumb: ':workspace' }, children: customerRoutes },
        ],
      },
      {
        path: 'plans',
        loadComponent: () => import('./features/platform/plans.page').then((m) => m.PlansPage),
        data: { breadcrumb: 'nav.plans', permission: 'platform.plans.read' },
        canActivate: [permissionGuard],
      },
      { path: 'notifications', ...soon('nav.notifications', 'M6', 'platform.dashboard.read') },
      { path: 'archive', ...soon('nav.archive', 'M9', 'platform.tenants.read') },
      { path: 'reports', ...soon('nav.reports', 'M9', 'platform.dashboard.read') },
      {
        path: 'users',
        loadComponent: () => import('./features/users/platform-users.page').then((m) => m.PlatformUsersPage),
        data: { breadcrumb: 'nav.usersRoles', permission: 'platform.users.manage' },
        canActivate: [permissionGuard],
      },
      { path: 'settings', ...soon('nav.settings', 'M9', 'platform.settings.manage') },
    ],
  },
  {
    path: 'app',
    component: AppShell,
    canActivate: [authGuard, areaGuard('tenant')],
    children: customerRoutes,
  },
  ...(isDevMode()
    ? [{ path: 'dev/components', component: AppShell, canActivate: [authGuard], children: [{ path: '', loadComponent: () => import('./features/dev/components.page').then((m) => m.ComponentsPage) }] }]
    : []),
  { path: '', pathMatch: 'full', canActivate: [homeRedirectGuard], children: [] },
  { path: '**', redirectTo: '' },
];
