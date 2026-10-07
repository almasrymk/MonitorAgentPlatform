import { ChildProcess, spawn, spawnSync } from 'node:child_process';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';

import { expect, test } from '@playwright/test';

import { accounts, signedIn } from './helpers';

// e2e flow 2 (09 section 7, M4 acceptance): simulated devices come online in Cairo HQ and go offline when the
// simulator stops (Goodbye), without a page reload. Needs the gRPC gateway on http://localhost:5301 (Development).
test.describe('Live device state', () => {
  test.setTimeout(360_000);
  let simulator: ChildProcess | null = null;

  // The simulated devices are retired afterwards so the seed numbers of the other flows stay as they were.
  test.afterEach(async ({ request }) => {
    // dotnet run starts the simulator as a child process: end the whole tree.
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

  test('simulated devices appear online and go offline without a reload', async ({ page }) => {
    const root = resolve(__dirname, '..', '..');
    // Fresh devices for every run (own store and fingerprints): re-enrolling a fingerprint is limited to 5 per hour.
    const runId = Date.now().toString(36);
    const store = join(tmpdir(), `simulator-${runId}.json`);
    simulator = spawn('dotnet', ['run', '--project', 'tools/MonitorCloud.DeviceSimulator', '--', 'run', '--devices', '3', '--seconds', '75', '--gateway', 'http://localhost:5301', '--run-id', runId, '--store', store], {
      cwd: root,
      stdio: 'ignore',
    });

    await signedIn(page, accounts.acmeAdmin, /\/app\/overview$/);
    await page.getByTestId('nav-locations').click();
    await page.getByTestId('location-card').filter({ hasText: 'Cairo HQ' }).click();
    await page.getByTestId('tab-devices').click();
    await page.getByTestId('device-search').locator('input').fill('SIM-ACME');
    await page.getByTestId('device-search').locator('input').press('Enter');

    // SIM-ACME-01..03 are the simulator's first three devices (earlier runs may have enrolled more).
    const cards = page.getByTestId('device-card').filter({ hasText: /SIM-ACME-0[1-3]/ });
    // Enrollment happens on the first run; the list is reloaded until the three devices exist.
    await expect(async () => {
      await page.getByTestId('device-search').locator('input').press('Enter');
      await expect(cards).toHaveCount(3, { timeout: 2_000 });
    }).toPass({ timeout: 150_000, intervals: [3_000] });

    const statuses = cards.getByTestId('device-status');
    await expect(statuses).toHaveText(['Healthy', 'Healthy', 'Healthy'], { timeout: 60_000 });

    // The simulator says Goodbye after 75 s; the cards turn Offline through the live hub, no reload.
    await expect(statuses).toHaveText(['Offline', 'Offline', 'Offline'], { timeout: 180_000 });
  });
});
