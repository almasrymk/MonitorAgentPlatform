import { HttpContextToken } from '@angular/common/http';

/** The request handles its own errors: no toast. */
export const SKIP_ERROR_TOAST = new HttpContextToken<boolean>(() => false);

/** Anonymous auth calls: no bearer token, no refresh-and-retry on 401. */
export const SKIP_AUTH_REFRESH = new HttpContextToken<boolean>(() => false);
