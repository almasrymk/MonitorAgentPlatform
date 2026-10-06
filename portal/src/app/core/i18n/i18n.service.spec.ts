import { TestBed } from '@angular/core/testing';

import { I18nService } from './i18n.service';

describe('I18nService', () => {
  let service: I18nService;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({});
    service = TestBed.inject(I18nService);
  });

  it('translates English keys by default', () => {
    expect(service.t('app.name')).toBe('Monitor Agent Platform');
  });

  it('replaces placeholders', () => {
    expect(service.t('app.version', { version: '1.2.3' })).toBe('Monitor Agent Platform v1.2.3');
  });

  it('returns the key when it is unknown', () => {
    expect(service.t('does.not.exist')).toBe('does.not.exist');
  });

  it('switches to Arabic, sets RTL on the document and remembers the choice', async () => {
    await service.use('ar');

    expect(service.language()).toBe('ar');
    expect(document.documentElement.dir).toBe('rtl');
    expect(document.documentElement.lang).toBe('ar');
    expect(service.t('shell.navigation')).toBe('القائمة الرئيسية');
    expect(localStorage.getItem('mc.lang')).toBe('ar');
  });

  it('restores the saved language on init', async () => {
    localStorage.setItem('mc.lang', 'ar');

    await service.init();

    expect(service.direction()).toBe('rtl');
  });

  it('switches back to LTR for English', async () => {
    await service.use('ar');
    await service.use('en');

    expect(document.documentElement.dir).toBe('ltr');
  });

  it('keeps unknown placeholders untouched', () => {
    expect(service.t('app.version', { other: 'x' })).toBe('Monitor Agent Platform v{version}');
  });
});
