import { expect, test } from '@playwright/test';

import { accounts, signedIn } from './helpers';

// M8: thresholds and monitor points are edited in the portal; each change raises the configuration version.
test.describe('Central configuration', () => {
  test.beforeEach(async ({ page }) => {
    await signedIn(page, accounts.acmeAdmin, /\/app\/overview$/);
    await page.getByTestId('nav-devices').click();
    await page.getByTestId('device-search').locator('input').fill('FILE-SRV-01');
    await page.getByTestId('device-search').locator('input').press('Enter');
    await page.getByTestId('device-card').filter({ hasText: 'FILE-SRV-01' }).first().getByTestId('open-console').click();
    await expect(page.getByTestId('device-title')).toHaveText('FILE-SRV-01');
  });

  test('a threshold change saves a new target version', async ({ page }) => {
    await page.getByTestId('nav-settings').click();
    const target = page.getByTestId('target-version');
    await expect(target).not.toHaveText('');
    const before = Number((await target.textContent())?.trim().replace('—', '0') || '0');

    await page.getByTestId('cpu-critical').fill('91');
    await page.getByTestId('save-configuration').click();

    // A device without a configuration starts at version 1, so the first save gives 2.
    await expect(target).toHaveText(String(before === 0 ? 2 : before + 1));
    await expect(page.getByTestId('config-status')).toBeVisible();
    await expect(page.getByTestId('cpu-critical')).toHaveValue('91');
  });

  test('a monitor point is added and removed', async ({ page }) => {
    await page.getByTestId('nav-monitor-points').click();
    await page.getByTestId('add-point').click();
    await page.getByTestId('point-type').selectOption('Ping');
    await page.getByTestId('point-name').fill('E2E Gateway');
    await page.getByTestId('point-target').fill('198.51.100.1');
    await page.getByTestId('save-point').click();

    const row = page.getByTestId('points-table').locator('tbody tr').filter({ hasText: 'E2E Gateway' });
    await expect(row).toHaveCount(1);
    await row.getByRole('button', { name: 'Delete' }).click();
    await expect(row).toHaveCount(0);
  });
});
