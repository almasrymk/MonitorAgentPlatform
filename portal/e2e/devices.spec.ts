import { expect, test } from '@playwright/test';

import { accounts, signedIn } from './helpers';

// M3 on the demo seed (08 section 3): numbers come from the seed, never from a mockup.
test.describe('Devices and dashboards', () => {
  test('the customer dashboard shows the Acme tiles, locations and licence summary', async ({ page }) => {
    await signedIn(page, accounts.acmeAdmin, /\/app\/overview$/);

    await expect(page.getByTestId('tile-locations').getByTestId('kpi-value')).toHaveText('3');
    await expect(page.getByTestId('tile-devices').getByTestId('kpi-value')).toHaveText('316');
    await expect(page.getByTestId('tile-unlicensed').getByTestId('kpi-value')).toHaveText('26');
    await expect(page.getByTestId('location-card')).toHaveCount(3);
    await expect(page.getByTestId('status-by-location')).toContainText('Cairo HQ');
    await expect(page.getByTestId('license-summary')).toContainText('290 / 500');
  });

  test('locations show their device counts and open the overview and devices tabs', async ({ page }) => {
    await signedIn(page, accounts.acmeAdmin, /\/app\/overview$/);
    await page.getByTestId('nav-locations').click();

    const cairo = page.getByTestId('location-card').filter({ hasText: 'Cairo HQ' });
    await expect(cairo.getByTestId('location-devices')).toHaveText('142');
    await cairo.click();

    await expect(page).toHaveURL(/\/app\/locations\/[0-9a-f-]+\/overview$/);
    await expect(page.getByTestId('tile-devices').getByTestId('kpi-value')).toHaveText('142');
    await expect(page.getByTestId('tile-online').getByTestId('kpi-value')).toHaveText('134');
    await expect(page.getByTestId('devices-by-os')).toContainText('Windows');
    await expect(page.getByTestId('resources')).toContainText('Across 134 online devices');

    await page.getByTestId('tab-devices').click();
    await expect(page.getByTestId('tile-offline').getByTestId('kpi-value')).toHaveText('8');
    const web = page.getByTestId('device-card').filter({ hasText: 'WEB-SRV-01' });
    await expect(web).toHaveCount(1);
    await expect(web.getByTestId('device-status')).toHaveText('Critical');

    await page.getByTestId('device-search').locator('input').fill('DEV-MAC-01');
    await page.getByTestId('device-search').locator('input').press('Enter');
    await expect(page.getByTestId('device-card')).toHaveCount(1);
    await expect(page.getByTestId('device-card').getByTestId('device-license')).toHaveText('Unlicensed');

    await page.getByTestId('view-list').click();
    await expect(page.getByTestId('device-table')).toContainText('DEV-MAC-01');
  });

  test('Add Device creates an enrollment code with install commands', async ({ page }) => {
    await signedIn(page, accounts.acmeAdmin, /\/app\/overview$/);
    await page.getByTestId('nav-locations').click();
    await page.getByTestId('location-card').filter({ hasText: 'Dubai Office' }).click();
    await page.getByTestId('tab-devices').click();

    await page.getByTestId('add-device').click();
    await page.getByTestId('create-code').click();
    await expect(page.getByTestId('enrollment-code')).toHaveText(/^LOC-[A-Z2-9]{6}-[A-Z2-9]{6}$/);
    await expect(page.getByTestId('install-command')).toContainText('msiexec');
    await page.getByTestId('tab-linux').click();
    await expect(page.getByTestId('install-command')).toContainText('curl');
  });

  test('all devices can be filtered by location and status', async ({ page }) => {
    await signedIn(page, accounts.acmeAdmin, /\/app\/overview$/);
    await page.getByTestId('nav-devices').click();

    await expect(page.getByTestId('tile-devices').getByTestId('kpi-value')).toHaveText('316');
    await page.getByTestId('status-filter').locator('select').selectOption('critical');
    await expect(page.getByTestId('tile-critical').getByTestId('kpi-value')).toHaveText('15');
    await expect(page.getByTestId('device-card')).toHaveCount(15);
  });

  test('a report viewer sees devices without management actions', async ({ page }) => {
    await signedIn(page, accounts.acmeViewer, /\/app\/overview$/);
    await page.getByTestId('nav-devices').click();
    await expect(page.getByTestId('device-card').first()).toBeVisible();
    await expect(page.getByTestId('menu-trigger')).toHaveCount(0);
  });

  test('customer cards show devices and health; the health filter narrows them', async ({ page }) => {
    await signedIn(page, accounts.platformAdmin, /\/admin\/dashboard$/);
    await page.getByTestId('nav-customers').click();

    await page.getByTestId('customer-search').locator('input').fill('Gulf');
    const gulf = page.getByTestId('customer-card').filter({ hasText: 'Gulf Engineering' });
    await expect(gulf.getByTestId('customer-devices')).toHaveText('428');
    await page.getByTestId('customer-search').locator('input').fill('');
    await page.getByTestId('health-filter').locator('select').selectOption('critical');
    await expect(page.getByTestId('customer-card').filter({ hasText: 'Acme Corporation' })).toHaveCount(1);
  });
});
