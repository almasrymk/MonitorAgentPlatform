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
  { path: 'overview', ...soon('nav.dashboard', 'M3', 'dashboard.read') },
  {
    path: 'locations',
    loadComponent: () => import('./features/locations/locations.page').then((m) => m.LocationsPage),
    data: { breadcrumb: 'nav.locations', permission: 'locations.read' },
    canActivate: [permissionGuard],
  },
  { path: 'devices', ...soon('nav.devices', 'M3', 'devices.read') },
  { path: 'subscription', ...soon('nav.subscriptions', 'M2', 'subscription.read') },
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
      { path: 'plans', ...soon('nav.plans', 'M2', 'platform.plans.read') },
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
