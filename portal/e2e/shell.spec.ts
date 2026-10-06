import { expect, test } from '@playwright/test';

import { accounts, signedIn } from './helpers';

test.describe('App shell', () => {
  test('anonymous visitors land on the sign-in page with the dark theme', async ({ page }) => {
    await page.goto('/');

    await expect(page).toHaveURL(/\/login$/);
    await expect(page.getByTestId('login-form')).toBeVisible();
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
    const background = await page.evaluate(() => getComputedStyle(document.body).backgroundColor);
    expect(background).toBe('rgb(11, 15, 21)');
  });

  test('uses the self-hosted fonts only', async ({ page }) => {
    const external: string[] = [];
    page.on('request', (r) => {
      if (!r.url().startsWith('http://localhost')) {
        external.push(r.url());
      }
    });
    await page.goto('/login');

    const family = await page.evaluate(() => getComputedStyle(document.body).fontFamily);
    expect(family).toContain('Inter');
    expect(external).toEqual([]);
  });

  test('signed-in shell shows sidebar, version and user menu', async ({ page }) => {
    await signedIn(page, accounts.platformAdmin, /\/admin\/dashboard$/);

    await expect(page.getByTestId('sidebar')).toBeVisible();
    await expect(page.getByTestId('version')).toHaveText(/^Monitor Agent Platform v/);
    await page.getByTestId('user-menu').click();
    await expect(page.getByTestId('sign-out')).toBeVisible();
  });
});
