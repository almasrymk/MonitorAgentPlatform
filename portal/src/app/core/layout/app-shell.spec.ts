import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { I18nService } from '../i18n/i18n.service';
import { AppShell } from './app-shell';

describe('AppShell', () => {
  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [AppShell],
      providers: [provideRouter([])],
    }).compileComponents();
  });

  it('renders the top bar, the sidebar and the content area', async () => {
    const fixture = TestBed.createComponent(AppShell);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;

    expect(host.querySelector('[data-testid="topbar"]')).not.toBeNull();
    expect(host.querySelector('[data-testid="sidebar"]')).not.toBeNull();
    expect(host.querySelector('[data-testid="content"]')).not.toBeNull();
  });

  it('shows the product name and the build version in the sidebar footer', async () => {
    const fixture = TestBed.createComponent(AppShell);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;

    expect(host.querySelector('.brand-name')?.textContent).toContain('Monitor Agent Platform');
    expect(host.querySelector('[data-testid="version"]')?.textContent).toMatch(/^Monitor Agent Platform v\S+/);
  });

  it('translates the frame when the language changes', async () => {
    const fixture = TestBed.createComponent(AppShell);
    await TestBed.inject(I18nService).use('ar');
    fixture.detectChanges();
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;

    expect(host.querySelector('[data-testid="sidebar"]')?.getAttribute('aria-label')).toBe('القائمة الرئيسية');
  });
});
