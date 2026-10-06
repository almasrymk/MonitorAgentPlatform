import { DOCUMENT } from '@angular/common';
import { Injectable, inject, signal } from '@angular/core';

export type Theme = 'dark' | 'light';

const STORAGE_KEY = 'mc.theme';

/** Dark is the default and the theme of the designs. */
@Injectable({ providedIn: 'root' })
export class ThemeStore {
  private readonly document = inject(DOCUMENT);
  readonly theme = signal<Theme>('dark');

  apply(): void {
    let saved: string | null = null;
    try {
      saved = this.document.defaultView?.localStorage.getItem(STORAGE_KEY) ?? null;
    } catch {
      saved = null;
    }
    this.set(saved === 'light' ? 'light' : 'dark');
  }

  set(theme: Theme): void {
    this.theme.set(theme);
    this.document.documentElement.dataset['theme'] = theme;
    try {
      this.document.defaultView?.localStorage.setItem(STORAGE_KEY, theme);
    } catch {
      // ignore
    }
  }
}
