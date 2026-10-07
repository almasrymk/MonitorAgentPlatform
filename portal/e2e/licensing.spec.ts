import { expect, test } from '@playwright/test';

import { accounts, signedIn } from './helpers';

test.describe('Licensing', () => {
  test('a customer administrator sees the plan, seats and renewal', async ({ page }) => {
    await signedIn(page, accounts.acmeAdmin, /\/app\/overview$/);
    await page.getByTestId('nav-subscription').click();

    await expect(page.getByTestId('current-plan')).toContainText('Enterprise');
    await expect(page.getByTestId('current-plan')).toContainText('290 of 500 devices used');
    await expect(page.getByTestId('renewal')).toContainText('days remaining');
  });

  test('platform staff see read-only plans and customer cards with plan and licence usage', async ({ page }) => {
    await signedIn(page, accounts.platformAdmin, /\/admin\/dashboard$/);
    await page.getByTestId('nav-plans').click();
    await expect(page.getByTestId('table-row')).toHaveCount(4);

    await page.getByTestId('nav-customers').click();
    await expect(page.getByTestId('kpi-value').nth(2)).toHaveText('6');
    const acme = page.getByTestId('customer-card').filter({ hasText: 'Acme Corporation' });
    await expect(acme.getByTestId('plan-name')).toHaveText('Enterprise');
    await expect(acme.getByTestId('usage-text')).toHaveText('290 / 500');

    await page.getByTestId('plan-filter').locator('select').selectOption('STARTER');
    await expect(page.getByTestId('customer-card').filter({ hasText: 'Oasis Hospitality' })).toHaveCount(1);
    await expect(page.getByTestId('customer-card').filter({ hasText: 'Acme Corporation' })).toHaveCount(0);
  });
});
