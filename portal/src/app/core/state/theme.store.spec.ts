import { TestBed } from '@angular/core/testing';

import { ThemeStore } from './theme.store';

describe('ThemeStore', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({});
  });

  it('defaults to the dark theme', () => {
    const store = TestBed.inject(ThemeStore);

    store.apply();

    expect(store.theme()).toBe('dark');
    expect(document.documentElement.dataset['theme']).toBe('dark');
  });

  it('restores a saved light theme', () => {
    localStorage.setItem('mc.theme', 'light');
    const store = TestBed.inject(ThemeStore);

    store.apply();

    expect(document.documentElement.dataset['theme']).toBe('light');
  });

  it('ignores unknown saved values', () => {
    localStorage.setItem('mc.theme', 'neon');
    const store = TestBed.inject(ThemeStore);

    store.apply();

    expect(store.theme()).toBe('dark');
  });
});
