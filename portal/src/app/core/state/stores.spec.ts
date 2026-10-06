import { TestBed } from '@angular/core/testing';

import { ToastService } from '../ui/toast.service';
import { DateRangeStore } from './date-range.store';
import { ScopeStore } from './scope.store';

describe('stores', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({});
  });

  it('ScopeStore keeps the workspace in sessionStorage', () => {
    const store = TestBed.inject(ScopeStore);
    store.enterWorkspace({ id: 'a', name: 'Alpha' });
    expect(JSON.parse(sessionStorage.getItem('mc.workspace')!)).toEqual({ id: 'a', name: 'Alpha' });
    store.leaveWorkspace();
    expect(store.workspace()).toBeNull();
    expect(sessionStorage.getItem('mc.workspace')).toBeNull();
  });

  it('ScopeStore restores the workspace after a reload', () => {
    sessionStorage.setItem('mc.workspace', JSON.stringify({ id: 'b', name: 'Beta' }));
    expect(TestBed.inject(ScopeStore).workspace()).toEqual({ id: 'b', name: 'Beta' });
  });

  it('ScopeStore ignores a corrupt value', () => {
    sessionStorage.setItem('mc.workspace', '{oops');
    expect(TestBed.inject(ScopeStore).workspace()).toBeNull();
  });

  it('DateRangeStore defaults to seven days', () => {
    const store = TestBed.inject(DateRangeStore);
    expect(store.range()).toBe('7d');
    expect(store.options).toContain('30d');
  });

  it('ToastService keeps at most five toasts and expires them', () => {
    vi.useFakeTimers();
    const toasts = TestBed.inject(ToastService);
    for (let i = 0; i < 7; i++) {
      toasts.info(`m${i}`);
    }
    expect(toasts.toasts().length).toBe(5);
    vi.advanceTimersByTime(6000);
    expect(toasts.toasts().length).toBe(0);
    vi.useRealTimers();
  });
});
