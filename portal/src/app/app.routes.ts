import { Routes } from '@angular/router';

import { AppShell } from './core/layout/app-shell';

export const routes: Routes = [
  {
    path: '',
    component: AppShell,
    children: [],
  },
  { path: '**', redirectTo: '' },
];
