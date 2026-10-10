import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { AuthResult, DeviceCommand } from '../../../core/api/models';
import { AuthService } from '../../../core/auth/auth.service';
import { render } from '../../../testing/render';
import { RemoteActions } from './remote-actions';

const settle = async () => {
  for (let i = 0; i < 5; i++) {
    await Promise.resolve();
  }
};

describe('RemoteActions', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    sessionStorage.clear();
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
  });

  async function signIn(permissions: string[]): Promise<void> {
    const done = TestBed.inject(AuthService).login('u@x.test', 'pw');
    const body: AuthResult = {
      accessToken: 'a', refreshToken: 'r', expiresAt: '',
      user: { id: '1', fullName: 'Admin User', email: 'u@x.test', role: 'CustomerAdmin', tenantId: 't', tenantName: null, permissions, language: 'en', locationIds: [] },
    };
    http.expectOne('/api/v1/auth/login').flush(body);
    await done;
  }

  async function show(available: boolean | null) {
    const view = await render(RemoteActions, { deviceId: 'd1', deviceName: 'WEB-SRV-01' });
    if (available !== null) {
      http.expectOne('/api/v1/devices/d1/remote-actions').flush({ available, reason: available ? null : 'setting' });
    }
    await settle();
    view.fixture.detectChanges();
    return view;
  }

  it('is absent without the permission and does not ask the API', async () => {
    await signIn(['devices.read']);
    const { host } = await show(null);
    http.expectNone('/api/v1/devices/d1/remote-actions');
    expect(host.querySelector('[data-testid="remote-actions"]')).toBeNull();
  });

  it('is absent when the feature, licence or device setting does not allow it', async () => {
    await signIn(['devices.manage']);
    const { host } = await show(false);
    expect(host.querySelector('[data-testid="remote-actions"]')).toBeNull();
  });

  it('sends a service command with its name and reason', async () => {
    await signIn(['devices.manage']);
    const { host, fixture } = await show(true);
    (host.querySelector('[data-testid="remote-actions"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    (host.querySelector('[data-testid="remote-service-restart"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    const confirm = () => host.querySelector('[data-testid="confirm"]') as HTMLButtonElement;
    expect(confirm().disabled).toBe(true);
    const type = (selector: string, value: string) => {
      const field = host.querySelector(selector) as HTMLInputElement;
      field.value = value;
      field.dispatchEvent(new Event('input'));
    };
    type('[data-testid="reason"]', 'IIS stopped answering');
    type('[data-testid="remote-service"]', 'bad;name');
    fixture.detectChanges();
    expect(confirm().disabled).toBe(true);
    type('[data-testid="remote-service"]', 'W3SVC');
    fixture.detectChanges();
    expect(confirm().disabled).toBe(false);

    confirm().click();
    const request = http.expectOne('/api/v1/devices/d1/commands');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ type: 'service-restart', service: 'W3SVC', reason: 'IIS stopped answering' });
    request.flush({ id: 'c1' }, { status: 202, statusText: 'Accepted' });
    await settle();
    fixture.detectChanges();
    expect(host.querySelector('[data-testid="confirm"]')).toBeNull();
  });

  it('shows the command history with status and result', async () => {
    await signIn(['devices.manage']);
    const { host, fixture } = await show(true);
    (host.querySelector('[data-testid="remote-actions"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    (host.querySelector('[data-testid="remote-history"]') as HTMLButtonElement).click();
    const item: DeviceCommand = {
      id: 'c1', type: 'refresh-inventory', service: null, reason: 'Fresh data please', requestedByName: 'Admin User', requestedAt: '2026-10-10T08:00:00Z',
      expiresAt: '2026-10-10T08:05:00Z', status: 'Succeeded', sentAt: '2026-10-10T08:00:01Z', completedAt: '2026-10-10T08:00:02Z', output: 'inventory sent',
    };
    http.expectOne((r) => r.url === '/api/v1/devices/d1/commands').flush({ items: [item], total: 1, page: 1, pageSize: 20 });
    await settle();
    fixture.detectChanges();

    const list = host.querySelector('[data-testid="remote-history-list"]')?.textContent ?? '';
    expect(list).toContain('Refresh inventory');
    expect(list).toContain('Succeeded');
    expect(list).toContain('inventory sent');
    expect(list).toContain('By Admin User');
  });
});
