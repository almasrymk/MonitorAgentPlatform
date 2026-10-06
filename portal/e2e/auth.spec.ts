import { expect, test } from '@playwright/test';

import { accounts, signIn, signedIn } from './helpers';

test.describe('Sign-in and access', () => {
  // e2e flow 6
  test('a suspended customer cannot sign in', async ({ page }) => {
    await signIn(page, accounts.oasisAdmin);

    await expect(page.getByTestId('login-error')).toHaveText('This customer account is suspended.');
    await expect(page).toHaveURL(/\/login$/);
  });

  test('wrong password shows the generic message', async ({ page }) => {
    await signIn(page, { email: accounts.acmeAdmin.email, password: 'Wrong-Pass#1' });

    await expect(page.getByTestId('login-error')).toHaveText('The e-mail or password is incorrect.');
  });

  // e2e flow 5
  test('a report viewer has no Users or Settings and the direct URL is blocked', async ({ page }) => {
    await signedIn(page, accounts.acmeViewer, /\/app\/overview$/);

    await expect(page.getByTestId('nav-locations')).toBeVisible();
    await expect(page.getByTestId('nav-users')).toHaveCount(0);
    await expect(page.getByTestId('nav-settings')).toHaveCount(0);

    await page.goto('/app/users');
    await expect(page).toHaveURL(/\/app\/overview$/);
    await page.goto('/app/settings');
    await expect(page).toHaveURL(/\/app\/overview$/);
  });

  test('a customer user cannot reach the platform area', async ({ page }) => {
    await signedIn(page, accounts.acmeAdmin, /\/app\/overview$/);

    await page.goto('/admin/customers');

    await expect(page).toHaveURL(/\/app\/overview$/);
  });

  test('the session survives a reload and sign-out ends it', async ({ page }) => {
    await signedIn(page, accounts.acmeAdmin, /\/app\/overview$/);
    await page.reload();
    await expect(page.getByTestId('sidebar')).toBeVisible();

    await page.getByTestId('user-menu').click();
    await page.getByTestId('sign-out').click();
    await expect(page).toHaveURL(/\/login$/);
    await page.goto('/app/users');
    await expect(page).toHaveURL(/\/login/);
  });
});

test.describe('Workspace', () => {
  test('platform admin opens the Acme workspace with a reason and sees the banner', async ({ page }) => {
    await signedIn(page, accounts.platformAdmin, /\/admin\/dashboard$/);
    await page.getByTestId('nav-customers').click();
    await page.getByTestId('customer-search').locator('input').fill('Acme');

    const card = page.getByTestId('customer-card').filter({ hasText: 'Acme Corporation' });
    await expect(card).toHaveCount(1);
    await card.getByTestId('open-workspace').click();
    await expect(page.getByTestId('confirm')).toBeDisabled();
    await page.getByTestId('reason').fill('Checking the setup for the customer');
    await page.getByTestId('confirm').click();

    await expect(page).toHaveURL(/\/admin\/customers\/[0-9a-f-]+\/overview$/);
    await expect(page.getByTestId('workspace-banner')).toContainText('Viewing Acme Corporation as platform staff');

    await page.getByTestId('nav-locations').click();
    await expect(page.getByTestId('location-card').filter({ hasText: 'Cairo HQ' })).toHaveCount(1);

    await page.getByTestId('leave-workspace').click();
    await expect(page).toHaveURL(/\/admin\/customers$/);
    await expect(page.getByTestId('workspace-banner')).toHaveCount(0);
  });
});

test.describe('Arabic', () => {
  test('switching language turns the layout right-to-left', async ({ page }) => {
    await page.goto('/login');
    await page.getByTestId('login-language').click();

    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await expect(page.getByTestId('sign-in')).toHaveText('تسجيل الدخول');
    await page.getByTestId('login-language').click();
    await expect(page.locator('html')).toHaveAttribute('dir', 'ltr');
  });
});
