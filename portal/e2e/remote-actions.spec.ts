import { Page, expect, test } from '@playwright/test';

import { accounts, signedIn } from './helpers';

// M11 (MC-1103): the demo seed switches remote actions on for the fixed WEB-SRV-01 of Cairo HQ only.
async function openDevice(page: Page, name: string): Promise<void> {
  await page.getByTestId('nav-locations').click();
  await page.getByTestId('location-card').filter({ hasText: 'Cairo HQ' }).click();
  await page.getByTestId('nav-devices').click();
  await page.getByTestId('device-search').locator('input').fill(name);
  await page.getByTestId('device-search').locator('input').press('Enter');
  await page.getByTestId('device-card').filter({ hasText: name }).first().getByTestId('open-console').click();
  await expect(page.getByTestId('device-title')).toHaveText(name);
}

test.describe('Remote actions', () => {
  test('an administrator sends a signed command with a reason and sees it in the history', async ({ page }) => {
    await signedIn(page, accounts.acmeAdmin, /\/app\/overview$/);
    await openDevice(page, 'WEB-SRV-01');

    await page.getByTestId('remote-actions').click();
    await page.getByTestId('remote-refresh-inventory').click();
    await expect(page.getByTestId('confirm')).toBeDisabled();
    await page.getByTestId('reason').fill('E2E check of the inventory');
    await page.getByTestId('confirm').click();
    await expect(page.getByText('Command sent: Refresh inventory.')).toBeVisible();

    await page.getByTestId('remote-actions').click();
    await page.getByTestId('remote-history').click();
    const history = page.getByTestId('remote-history-list');
    await expect(history).toContainText('Refresh inventory');
    await expect(history).toContainText('E2E check of the inventory');
  });

  test('the button is absent without the permission or the device setting', async ({ page }) => {
    await signedIn(page, accounts.acmeAdmin, /\/app\/overview$/);
    await openDevice(page, 'DESK-01');
    await expect(page.getByTestId('device-header')).toBeVisible();
    await expect(page.getByTestId('remote-actions')).toHaveCount(0);

    await page.context().clearCookies();
    await page.evaluate(() => {
      sessionStorage.clear();
      localStorage.clear();
    });
    await signedIn(page, accounts.acmeViewer, /\/app\/overview$/);
    await openDevice(page, 'WEB-SRV-01');
    await expect(page.getByTestId('remote-actions')).toHaveCount(0);
  });
});
