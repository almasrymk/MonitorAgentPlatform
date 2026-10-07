import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { AuthResult } from '../api/models';
import { AuthService } from '../auth/auth.service';
import { DeviceStateEvent, LiveService, SummaryEvent } from './live.service';

const hub = vi.hoisted(() => {
  const handlers = new Map<string, (arg: unknown) => void>();
  let reconnected: (() => void) | null = null;
  const connection = {
    invoke: vi.fn().mockResolvedValue(undefined),
    start: vi.fn().mockResolvedValue(undefined),
    stop: vi.fn().mockResolvedValue(undefined),
    on: vi.fn((name: string, handler: (arg: unknown) => void) => handlers.set(name, handler)),
    onreconnected: vi.fn((handler: () => void) => (reconnected = handler)),
    onreconnecting: vi.fn(),
    onclose: vi.fn(),
  };
  const builder = { withUrl: vi.fn(), withAutomaticReconnect: vi.fn(), configureLogging: vi.fn(), build: vi.fn(() => connection) };
  builder.withUrl.mockReturnValue(builder);
  builder.withAutomaticReconnect.mockReturnValue(builder);
  builder.configureLogging.mockReturnValue(builder);
  return { handlers, connection, builder, reconnect: () => reconnected?.() };
});

vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: class {
    constructor() {
      return hub.builder;
    }
  },
  LogLevel: { None: 6 },
}));

// The SignalR client is a dynamic import: wait for macrotasks, not only microtasks.
const settle = async () => {
  for (let i = 0; i < 5; i++) {
    await new Promise((resolve) => setTimeout(resolve, 0));
  }
};

describe('LiveService', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    vi.clearAllMocks();
    hub.handlers.clear();
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
  });

  async function signIn(): Promise<void> {
    const auth = TestBed.inject(AuthService);
    const done = auth.login('admin@acme.test', 'pw');
    const body: AuthResult = { accessToken: 'token-1', refreshToken: 'r', expiresAt: '', user: { id: '1', fullName: 'A', email: 'a', role: 'Administrator', tenantId: 't1', tenantName: 'Acme', permissions: [], language: 'en', locationIds: [] } };
    http.expectOne('/api/v1/auth/login').flush(body);
    await done;
  }

  it('does not connect without a signed-in user', async () => {
    const live = TestBed.inject(LiveService);
    live.subscribe({ kind: 'tenant' });
    await settle();
    expect(hub.connection.start).not.toHaveBeenCalled();
  });

  it('connects on the first subscription, joins groups once and leaves them when released', async () => {
    await signIn();
    const live = TestBed.inject(LiveService);

    const first = live.subscribe({ kind: 'location', id: 'l1' });
    const second = live.subscribe({ kind: 'location', id: 'l1' });
    live.subscribe({ kind: 'tenant' });
    live.subscribe({ kind: 'platform' });
    await settle();

    expect(hub.builder.withUrl).toHaveBeenCalledWith('/hubs/live', expect.objectContaining({ accessTokenFactory: expect.any(Function) }));
    const factory = (hub.builder.withUrl.mock.calls[0][1] as { accessTokenFactory: () => string }).accessTokenFactory;
    expect(factory()).toBe('token-1');
    expect(hub.connection.start).toHaveBeenCalledTimes(1);
    expect(hub.connection.invoke).toHaveBeenCalledWith('SubscribeLocation', 'l1');
    expect(hub.connection.invoke).toHaveBeenCalledWith('SubscribeTenant', null);
    expect(hub.connection.invoke).toHaveBeenCalledWith('SubscribePlatform');
    expect(hub.connection.invoke.mock.calls.filter((c) => c[0] === 'SubscribeLocation').length).toBe(1);
    expect(live.connected()).toBe(true);

    first();
    first();
    expect(hub.connection.invoke).not.toHaveBeenCalledWith('UnsubscribeLocation', 'l1');
    second();
    await settle();
    expect(hub.connection.invoke).toHaveBeenCalledWith('UnsubscribeLocation', 'l1');
  });

  it('forwards hub events and joins again after a reconnect', async () => {
    await signIn();
    const live = TestBed.inject(LiveService);
    const states: DeviceStateEvent[] = [];
    const summaries: SummaryEvent[] = [];
    live.deviceState$.subscribe((e) => states.push(e));
    live.summary$.subscribe((e) => summaries.push(e));
    live.subscribe({ kind: 'device', id: 'd1' });
    await settle();

    hub.handlers.get('deviceStateChanged')!({ deviceId: 'd1', connection: 'Offline' });
    hub.handlers.get('summaryChanged')!({ scope: 'tenant', id: 't1' });
    expect(states[0].connection).toBe('Offline');
    expect(summaries[0].scope).toBe('tenant');

    hub.connection.invoke.mockClear();
    hub.reconnect();
    await settle();
    expect(hub.connection.invoke).toHaveBeenCalledWith('SubscribeDevice', 'd1');
    expect(LiveService.key({ kind: 'device', id: 'd1' })).toBe('device:d1');
    expect(LiveService.key({ kind: 'tenant' })).toBe('tenant');
  });

  it('keeps working when a subscription is refused or the connection fails', async () => {
    await signIn();
    hub.connection.invoke.mockRejectedValueOnce(new Error('AUTH_FORBIDDEN'));
    const live = TestBed.inject(LiveService);
    live.subscribe({ kind: 'location', id: 'other' });
    await settle();
    expect(live.connected()).toBe(true);

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    await signIn();
    hub.connection.start.mockRejectedValueOnce(new Error('offline'));
    const failing = TestBed.inject(LiveService);
    failing.subscribe({ kind: 'tenant' });
    await settle();
    expect(failing.connected()).toBe(false);
  });

  it('closes the connection on sign-out', async () => {
    await signIn();
    const live = TestBed.inject(LiveService);
    live.subscribe({ kind: 'tenant' });
    await settle();

    TestBed.inject(AuthService).user.set(null);
    TestBed.tick();
    await settle();

    expect(hub.connection.stop).toHaveBeenCalled();
    expect(live.connected()).toBe(false);
  });
});
