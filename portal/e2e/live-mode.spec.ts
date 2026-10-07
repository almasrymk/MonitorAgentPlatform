import { ChildProcess, spawn, spawnSync } from 'node:child_process';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';

import { expect, test } from '@playwright/test';

import { accounts, signedIn } from './helpers';

// e2e flow 3 (M5 acceptance): with the simulator, the device screen's gauges move every 2 s while it is open and the
// live samples stop when it is closed (counted as `liveSample` frames on the live hub).
test.describe('Live mode', () => {
  test.setTimeout(300_000);
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

  test('gauges move every 2 s while the device screen is open and stop when it closes', async ({ page }) => {
    const runId = `lm${Date.now().toString(36)}`;
    simulator = spawn(
      'dotnet',
      ['run', '--project', 'tools/MonitorCloud.DeviceSimulator', '--', 'run', '--devices', '1', '--seconds', '240', '--gateway', 'http://localhost:5301', '--run-id', runId, '--store', join(tmpdir(), `simulator-${runId}.json`)],
      { cwd: resolve(__dirname, '..', '..'), stdio: 'ignore' },
    );

    const samples: number[] = [];
    page.on('websocket', (ws) => ws.on('framereceived', (frame) => {
      if (String(frame.payload).includes('"target":"liveSample"')) {
        samples.push(Date.now());
      }
    }));

    await signedIn(page, accounts.acmeAdmin, /\/app\/overview$/);
    await page.getByTestId('nav-devices').click();
    const card = page.getByTestId('device-card').filter({ hasText: 'SIM-ACME-01' });
    await expect(async () => {
      await page.getByTestId('device-search').locator('input').fill('SIM-ACME-01');
      await page.getByTestId('device-search').locator('input').press('Enter');
      await expect(card.getByTestId('device-status')).toHaveText('Healthy', { timeout: 2_000 });
    }).toPass({ timeout: 150_000, intervals: [3_000] });

    await card.getByTestId('open-console').click();
    await expect(page.getByTestId('device-title')).toHaveText('SIM-ACME-01');

    // Live mode starts: at least four samples within 12 s, and the CPU gauge shows different values over time.
    await expect.poll(() => samples.length, { timeout: 30_000 }).toBeGreaterThanOrEqual(4);
    const gauge = page.getByTestId('gauge-cpu').getByTestId('ring-value');
    const seen = new Set<string>();
    for (let i = 0; i < 6; i++) {
      seen.add((await gauge.textContent()) ?? '');
      await page.waitForTimeout(2_000);
    }
    expect(seen.size).toBeGreaterThan(1);

    // Closing the screen leaves the device group: no further samples reach the browser.
    await page.getByTestId('nav-devices').click();
    await page.waitForTimeout(1_000);
    const afterClose = samples.length;
    await page.waitForTimeout(8_000);
    expect(samples.length).toBe(afterClose);
  });
});
