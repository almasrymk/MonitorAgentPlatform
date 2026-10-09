import { Routes } from '@angular/router';

import { areaGuard, authGuard, homeRedirectGuard, permissionGuard, workspaceGuard } from './core/auth/guards';
import { AppShell } from './core/layout/app-shell';
import { isDevMode } from '@angular/core';

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
          {
            path: 'notifications',
            loadComponent: () => import('./features/notifications/notifications.page').then((m) => m.NotificationsPage),
            data: { breadcrumb: 'nav.notifications', permission: 'notifications.read' },
            canActivate: [permissionGuard],
          },
          {
            path: 'archive',
            loadComponent: () => import('./features/archive/archive.page').then((m) => m.LocationArchivePage),
            data: { breadcrumb: 'nav.archive', permission: 'archive.read' },
            canActivate: [permissionGuard],
          },
          {
            path: 'reports',
            loadComponent: () => import('./features/reports/reports.page').then((m) => m.LocationReportsPage),
            data: { breadcrumb: 'nav.reports', permission: 'reports.read' },
            canActivate: [permissionGuard],
          },
          {
            path: 'settings',
            loadComponent: () => import('./features/locations/location-settings.page').then((m) => m.LocationSettingsPage),
            data: { breadcrumb: 'nav.settings', permission: 'locations.read' },
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
          { path: 'monitor-points', loadComponent: () => import('./features/devices/device/device-monitor-points.page').then((m) => m.DeviceMonitorPointsPage), data: { breadcrumb: 'device.tab.monitorPoints' } },
          { path: 'applications', loadComponent: () => import('./features/devices/device/device-applications.page').then((m) => m.DeviceApplicationsPage), data: { breadcrumb: 'device.tab.applications' } },
          {
            path: 'reports',
            loadComponent: () => import('./features/reports/reports.page').then((m) => m.DeviceReportsPage),
            data: { breadcrumb: 'device.tab.reports', permission: 'reports.read' },
            canActivate: [permissionGuard],
          },
          { path: 'settings', loadComponent: () => import('./features/devices/device/device-settings.page').then((m) => m.DeviceSettingsPage), data: { breadcrumb: 'device.tab.settings' } },
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
  {
    path: 'reports',
    loadComponent: () => import('./features/reports/reports.page').then((m) => m.ReportsPage),
    data: { breadcrumb: 'nav.reports', permission: 'reports.read' },
    canActivate: [permissionGuard],
  },
  {
    path: 'users',
    loadComponent: () => import('./features/users/users.page').then((m) => m.UsersPage),
    data: { breadcrumb: 'nav.users', permission: 'users.manage' },
    canActivate: [permissionGuard],
  },
  {
    path: 'archive',
    loadComponent: () => import('./features/archive/archive.page').then((m) => m.ArchivePage),
    data: { breadcrumb: 'nav.archive', permission: 'archive.read' },
    canActivate: [permissionGuard],
  },
  {
    path: 'audit',
    loadComponent: () => import('./features/audit/audit.page').then((m) => m.AuditPage),
    data: { breadcrumb: 'audit.title', permission: 'audit.read' },
    canActivate: [permissionGuard],
  },
  {
    path: 'settings',
    loadComponent: () => import('./features/settings/settings.page').then((m) => m.SettingsPage),
    data: { breadcrumb: 'nav.settings', permission: 'settings.manage' },
    canActivate: [permissionGuard],
  },
  {
    path: 'notifications',
    loadComponent: () => import('./features/notifications/notifications.page').then((m) => m.NotificationsPage),
    data: { breadcrumb: 'nav.notifications', permission: 'notifications.read' },
    canActivate: [permissionGuard],
  },
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
      {
        path: 'dashboard',
        loadComponent: () => import('./features/platform/platform-dashboard.page').then((m) => m.PlatformDashboardPage),
        data: { breadcrumb: 'nav.dashboard', permission: 'platform.dashboard.read' },
        canActivate: [permissionGuard],
      },
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
      {
        path: 'notifications',
        loadComponent: () => import('./features/notifications/notifications.page').then((m) => m.NotificationsPage),
        data: { breadcrumb: 'nav.notifications', permission: 'platform.dashboard.read' },
        canActivate: [permissionGuard],
      },
      {
        path: 'archive',
        loadComponent: () => import('./features/platform/platform-archive.page').then((m) => m.PlatformArchivePage),
        data: { breadcrumb: 'nav.archive', permission: 'platform.tenants.read' },
        canActivate: [permissionGuard],
      },
      {
        path: 'reports',
        loadComponent: () => import('./features/platform/platform-reports.page').then((m) => m.PlatformReportsPage),
        data: { breadcrumb: 'nav.reports', permission: 'platform.dashboard.read' },
        canActivate: [permissionGuard],
      },
      {
        path: 'audit',
        loadComponent: () => import('./features/audit/audit.page').then((m) => m.AuditPage),
        data: { breadcrumb: 'audit.title', permission: 'platform.audit.read', platform: true },
        canActivate: [permissionGuard],
      },
      {
        path: 'users',
        loadComponent: () => import('./features/users/platform-users.page').then((m) => m.PlatformUsersPage),
        data: { breadcrumb: 'nav.usersRoles', permission: 'platform.users.manage' },
        canActivate: [permissionGuard],
      },
      {
        path: 'settings',
        loadComponent: () => import('./features/platform/platform-settings.page').then((m) => m.PlatformSettingsPage),
        data: { breadcrumb: 'nav.settings', permission: 'platform.settings.manage' },
        canActivate: [permissionGuard],
      },
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
