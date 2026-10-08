import { ChildProcess, spawn, spawnSync } from 'node:child_process';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';

import { expect, test } from '@playwright/test';

import { accounts, signedIn } from './helpers';

// M6 acceptance: an issue raised by the simulator appears as an alert on the device, notifies once (bell and feed)
// and disappears from the open alerts when it is cleared. Needs the gRPC gateway on http://localhost:5301.
test.describe('Alerts and notifications', () => {
  test.setTimeout(360_000);
  let simulator: ChildProcess | null = null;

  test.afterEach(async ({ request }) => {
    if (simulator?.pid && process.platform === 'win32') {
      spawnSync('taskkill', ['/pid', String(simulator.pid), '/T', '/F']);
    } else {
      simulator?.kill('SIGINT');
    }
    simulator = null;
    const login = await request.post('/api/v1/auth/login', { data: accounts.acmeAdmin });
    const headers = { Authorization: `Bearer ${(await login.json()).accessToken}` };
    const devices = await (await request.get('/api/v1/devices?search=SIM-ACME&pageSize=200', { headers })).json();
    for (const device of devices.items as { id: string }[]) {
      await request.post(`/api/v1/devices/${device.id}/retire`, { headers });
    }
  });

  test('a simulated issue becomes an alert with one notification and resolves when cleared', async ({ page, request }) => {
    const login = await request.post('/api/v1/auth/login', { data: accounts.acmeAdmin });
    const headers = { Authorization: `Bearer ${(await login.json()).accessToken}` };
    const simulatedNotifications = async () =>
      ((await (await request.get('/api/v1/notifications?pageSize=200&from=2020-01-01T00:00:00Z', { headers })).json()).items as { title: string }[]).filter((n) => n.title === 'Simulated cpu issue').length;
    const earlier = await simulatedNotifications();
    const root = resolve(__dirname, '..', '..');
    const runId = Date.now().toString(36);
    const store = join(tmpdir(), `simulator-${runId}.json`);
    simulator = spawn(
      'dotnet',
      ['run', '--project', 'tools/MonitorCloud.DeviceSimulator', '--', 'run', '--devices', '1', '--seconds', '150', '--gateway', 'http://localhost:5301', '--run-id', runId, '--store', store,
        '--issue', 'cpu', '--issue-after', '45', '--clear-after', '45'],
      { cwd: root, stdio: 'ignore' },
    );

    await signedIn(page, accounts.acmeAdmin, /\/app\/overview$/);
    await page.getByTestId('nav-devices').click();
    await page.getByTestId('device-search').locator('input').fill('SIM-ACME-01');
    const card = page.getByTestId('device-card').filter({ hasText: 'SIM-ACME-01' });
    await expect(async () => {
      await page.getByTestId('device-search').locator('input').press('Enter');
      await expect(card).toHaveCount(1, { timeout: 2_000 });
    }).toPass({ timeout: 150_000, intervals: [3_000] });
    const before = Number((await page.getByTestId('bell-count').textContent({ timeout: 2_000 }).catch(() => '0')) ?? '0');
    await card.getByTestId('open-console').click();

    // Raised: the Messages & Issues board shows it and the device turns Critical.
    const board = page.getByTestId('messages-board');
    await expect(board).toContainText('Simulated cpu issue', { timeout: 120_000 });
    await expect(page.getByTestId('device-header-status')).toHaveText(/Critical/, { timeout: 30_000 });
    await expect(page.getByTestId('bell-count')).toHaveText(String(before + 1), { timeout: 30_000 });

    // Cleared: resolved on the board, health back to Healthy.
    await expect(board.locator('li.resolved')).toContainText('Simulated cpu issue', { timeout: 90_000 });
    await expect(page.getByTestId('device-header-status')).toHaveText(/Healthy/, { timeout: 30_000 });

    // The feed holds exactly one new notification for it (earlier runs left their own).
    await page.getByTestId('bell').click();
    await expect(page).toHaveURL(/\/app\/notifications$/);
    await expect(page.getByTestId('notifications').getByText('Simulated cpu issue').first()).toBeVisible();
    expect(await simulatedNotifications()).toBe(earlier + 1);
  });

  test('marking all notifications as read clears the bell', async ({ page }) => {
    await signedIn(page, accounts.acmeAdmin, /\/app\/overview$/);
    await page.getByTestId('bell').click();
    await expect(page.getByTestId('notifications').locator('tbody tr')).not.toHaveCount(0);

    await page.getByTestId('mark-all-read').click();

    await expect(page.getByTestId('bell-count')).toHaveCount(0);
  });
});

test.describe('Platform Admin Dashboard', () => {
  test('shows the KPI tiles, incident charts, recent alerts and activity of the seed', async ({ page }) => {
    await signedIn(page, accounts.platformAdmin, /\/admin\/dashboard$/);

    await expect(page.getByTestId('platform-tiles').locator('mc-kpi-tile')).toHaveCount(8);
    await expect(page.getByTestId('tile-customers')).toContainText('48');
    await expect(page.getByTestId('incident-trend')).toBeVisible();
    await expect(page.getByTestId('resolved')).toContainText(/Resolved: \d+/);
    await expect(page.getByTestId('top-customers')).toContainText('Gulf Engineering');
    await expect(page.getByTestId('recent-alerts').locator('tbody tr')).toHaveCount(6);
    await expect(page.getByTestId('recent-activity')).toContainText('User login');
  });
});

test.describe('Alert settings', () => {
  test('a recipient is added and removed', async ({ page }) => {
    await signedIn(page, accounts.acmeAdmin, /\/app\/overview$/);
    await page.goto('/app/settings');
    await page.getByTestId('tab-alerts').click();
    await expect(page.getByTestId('alert-channels')).toBeVisible();
    await expect(page.getByTestId('switch-sms')).toBeDisabled();

    await page.getByTestId('add-recipient').click();
    await page.getByTestId('recipient-name').fill('E2E Desk');
    await page.getByTestId('recipient-email').fill('e2e.desk@acme.test');
    await page.getByTestId('save-recipient').click();
    await expect(page.getByTestId('recipients')).toContainText('e2e.desk@acme.test');

    await page.getByTestId('delete-e2e.desk@acme.test').click();
    await expect(page.getByTestId('recipients')).not.toContainText('e2e.desk@acme.test');
  });
});
