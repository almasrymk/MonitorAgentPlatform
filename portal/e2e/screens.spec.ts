import { Page, expect, test } from '@playwright/test';

import { accounts, signedIn } from './helpers';

/**
 * Screens for the review against docs/design (MC-1007), written to docs/screens/. Not part of the normal run:
 * `SCREENS=1 npx playwright test e2e/screens.spec.ts`.
 */
test.skip(!process.env['SCREENS'], 'Set SCREENS=1 to capture the review screenshots.');
test.use({ viewport: { width: 1600, height: 1000 } });

async function shot(page: Page, url: string, name: string): Promise<void> {
  await page.goto(url);
  await expect(page.locator('main h1').first()).toBeVisible();
  await page.waitForLoadState('networkidle');
  await page.waitForTimeout(800);
  await page.screenshot({ path: `../docs/screens/${name}.png`, fullPage: true });
}

test('platform screens', async ({ page }) => {
  test.setTimeout(120_000);
  await signedIn(page, accounts.platformAdmin, /\/admin\/dashboard$/);
  await shot(page, '/admin/dashboard', '01-platform-dashboard');
  await shot(page, '/admin/customers', '02-customers');
  await shot(page, '/admin/reports', '03-platform-reports');
});

test('customer screens', async ({ page }) => {
  test.setTimeout(180_000);
  await signedIn(page, accounts.acmeAdmin, /\/app\/overview$/);
  await shot(page, '/app/overview', '10-customer-dashboard');
  await page.goto('/app/locations');
  await page.getByText('Cairo HQ').first().click();
  await expect(page).toHaveURL(/\/overview$/);
  await shot(page, page.url().replace(/^https?:\/\/[^/]+/, ''), '11-location-overview');
  await shot(page, '/app/devices', '12-devices');
  await page.goto('/app/devices?search=WEB-SRV-01');
  await page.getByTestId('device-card').first().getByTestId('open-console').click();
  await expect(page.getByTestId('device-title')).toBeVisible();
  await shot(page, page.url().replace(/^https?:\/\/[^/]+/, ''), '13-device-overview');
  await shot(page, '/app/subscription', '14-subscription');
  await shot(page, '/app/users', '15-users');
  await shot(page, '/app/reports', '16-reports');
  await shot(page, '/app/archive', '17-archive');
  await shot(page, '/app/settings', '18-settings');
  await shot(page, '/app/notifications', '19-notifications');
});
