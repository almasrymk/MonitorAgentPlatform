import { registerLocaleData } from '@angular/common';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import localeArEg from '@angular/common/locales/ar-EG';
import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';

import { routes } from './app.routes';
import { authInterceptor, correlationInterceptor, errorInterceptor } from './core/auth/interceptors';
import { I18nService } from './core/i18n/i18n.service';
import { ThemeStore } from './core/state/theme.store';

// The date pipe formats Arabic screens with 'ar-EG' (07 section 6); Angular ships only en-US built in.
registerLocaleData(localeArEg, 'ar-EG');

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withFetch(), withInterceptors([correlationInterceptor, errorInterceptor, authInterceptor])),
    provideAppInitializer(() => {
      inject(ThemeStore).apply();
      return inject(I18nService).init();
    }),
  ],
};
