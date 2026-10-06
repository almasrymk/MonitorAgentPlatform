import { DOCUMENT } from '@angular/common';
import { Injectable, computed, inject, signal } from '@angular/core';

import en from './en.json';

export type Language = 'en' | 'ar';
export type Dictionary = Record<string, string>;

const STORAGE_KEY = 'mc.lang';

/**
 * Translations with signals. English is bundled; Arabic is loaded on demand. Sets `lang` and `dir` on <html>
 * so logical CSS properties flip the layout for RTL.
 */
@Injectable({ providedIn: 'root' })
export class I18nService {
  private readonly document = inject(DOCUMENT);
  private readonly dictionaries = signal<Partial<Record<Language, Dictionary>>>({ en });

  readonly language = signal<Language>('en');
  readonly direction = computed(() => (this.language() === 'ar' ? 'rtl' : 'ltr'));
  private readonly active = computed<Dictionary>(() => this.dictionaries()[this.language()] ?? en);

  /** Restores the saved language (or the browser's) and applies it. */
  async init(): Promise<void> {
    await this.use(this.initialLanguage());
  }

  async use(language: Language): Promise<void> {
    if (!this.dictionaries()[language]) {
      const loaded = await I18nService.load(language);
      this.dictionaries.update((d) => ({ ...d, [language]: loaded }));
    }
    this.language.set(language);
    const html = this.document.documentElement;
    html.lang = language;
    html.dir = this.direction();
    try {
      this.document.defaultView?.localStorage.setItem(STORAGE_KEY, language);
    } catch {
      // Storage can be unavailable (private mode); the choice then lasts for the session only.
    }
  }

  /** Translates a key, replacing `{name}` placeholders. Missing keys fall back to English, then to the key. */
  t(key: string, params?: Record<string, string | number>): string {
    const template = this.active()[key] ?? (en as Dictionary)[key] ?? key;
    if (!params) {
      return template;
    }
    return template.replace(/\{(\w+)\}/g, (match, name: string) => (name in params ? String(params[name]) : match));
  }

  has(key: string): boolean {
    return key in this.active() || key in (en as Dictionary);
  }

  /** Message for an API error code (`error.{CODE}`), or the generic one. */
  errorMessage(code: string): string {
    const key = `error.${code}`;
    return this.has(key) ? this.t(key) : this.t('error.ERROR');
  }

  private initialLanguage(): Language {
    let saved: string | null = null;
    try {
      saved = this.document.defaultView?.localStorage.getItem(STORAGE_KEY) ?? null;
    } catch {
      saved = null;
    }
    if (saved === 'en' || saved === 'ar') {
      return saved;
    }
    return this.document.defaultView?.navigator.language?.startsWith('ar') ? 'ar' : 'en';
  }

  private static async load(language: Language): Promise<Dictionary> {
    if (language === 'ar') {
      return (await import('./ar.json')).default;
    }
    return en;
  }
}
