import { DOCUMENT } from '@angular/common';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, catchError, finalize, firstValueFrom, map, of, shareReplay, tap } from 'rxjs';

import { AuthApi } from '../api/api.services';
import { AuthResult, UserProfile } from '../api/models';
import { I18nService, Language } from '../i18n/i18n.service';
import { ScopeStore } from '../state/scope.store';

const REFRESH_KEY = 'mc.refresh';
const PLATFORM_ROLES = ['PlatformAdmin', 'PlatformSupport'];

/**
 * The signed-in user (07 section 3). The access token lives in memory only; the refresh token in sessionStorage,
 * so a reload refreshes silently.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly api = inject(AuthApi);
  private readonly router = inject(Router);
  private readonly i18n = inject(I18nService);
  private readonly scope = inject(ScopeStore);
  private readonly storage = inject(DOCUMENT).defaultView?.sessionStorage;

  private readonly token = signal<string | null>(null);
  private refreshInFlight: Observable<string | null> | null = null;

  readonly user = signal<UserProfile | null>(null);
  readonly isSignedIn = computed(() => this.user() !== null);
  readonly isPlatform = computed(() => PLATFORM_ROLES.includes(this.user()?.role ?? ''));
  readonly permissions = computed(() => new Set(this.user()?.permissions ?? []));

  get accessToken(): string | null {
    return this.token();
  }

  hasPermission(permission: string): boolean {
    return this.permissions().has(permission);
  }

  /** Home route of the signed-in user. */
  home(): string {
    return this.isPlatform() ? '/admin/dashboard' : '/app/overview';
  }

  async login(email: string, password: string): Promise<void> {
    const result = await firstValueFrom(this.api.login(email, password));
    await this.accept(result);
  }

  /** Restores the session after a reload, using the stored refresh token. */
  async restore(): Promise<boolean> {
    if (this.user()) {
      return true;
    }
    return (await firstValueFrom(this.refresh())) !== null;
  }

  /** Single refresh shared by every caller that hits a 401 at the same time; emits null when signed out. */
  refresh(): Observable<string | null> {
    const stored = this.readRefreshToken();
    if (!stored) {
      return of(null);
    }
    this.refreshInFlight ??= this.api.refresh(stored).pipe(
      tap((result) => void this.accept(result)),
      map((result) => result.accessToken as string | null),
      catchError(() => {
        this.clear();
        return of(null);
      }),
      finalize(() => (this.refreshInFlight = null)),
      shareReplay(1),
    );
    return this.refreshInFlight;
  }
  async logout(): Promise<void> {
    const stored = this.readRefreshToken();
    if (stored) {
      try {
        await firstValueFrom(this.api.logout(stored));
      } catch {
        // Signing out locally is enough when the server is unreachable.
      }
    }
    this.clear();
    await this.router.navigateByUrl('/login');
  }

  async setLanguage(language: Language): Promise<void> {
    await this.i18n.use(language);
    if (this.user()) {
      this.user.update((u) => (u ? { ...u, language } : u));
      try {
        await firstValueFrom(this.api.setLanguage(language));
      } catch {
        // The preference stays local until the next successful call.
      }
    }
  }

  /** Clears the session (used on sign-out and on a failed refresh). */
  clear(): void {
    this.token.set(null);
    this.user.set(null);
    this.scope.leaveWorkspace();
    try {
      this.storage?.removeItem(REFRESH_KEY);
    } catch {
      // ignore
    }
  }

  private async accept(result: AuthResult): Promise<void> {
    this.token.set(result.accessToken);
    this.user.set(result.user);
    try {
      this.storage?.setItem(REFRESH_KEY, result.refreshToken);
    } catch {
      // ignore
    }
    const language = result.user.language === 'ar' ? 'ar' : 'en';
    if (language !== this.i18n.language()) {
      await this.i18n.use(language);
    }
  }

  private readRefreshToken(): string | null {
    try {
      return this.storage?.getItem(REFRESH_KEY) ?? null;
    } catch {
      return null;
    }
  }
}
