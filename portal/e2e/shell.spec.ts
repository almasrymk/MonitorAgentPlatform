import { expect, test } from '@playwright/test';

test.describe('App shell', () => {
  test('shows the empty shell with the dark theme', async ({ page }) => {
    await page.goto('/');

    await expect(page.getByTestId('topbar')).toBeVisible();
    await expect(page.getByTestId('sidebar')).toBeVisible();
    await expect(page.getByTestId('version')).toHaveText(/^Monitor Agent Platform v/);
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
    const background = await page.evaluate(() => getComputedStyle(document.body).backgroundColor);
    expect(background).toBe('rgb(11, 15, 21)');
  });

  test('uses the self-hosted Inter font', async ({ page }) => {
    await page.goto('/');

    const family = await page.evaluate(() => getComputedStyle(document.body).fontFamily);
    expect(family).toContain('Inter');
    const external = [] as string[];
    page.on('request', (r) => {
      if (!r.url().startsWith('http://localhost')) {
        external.push(r.url());
      }
    });
    await page.reload();
    expect(external).toEqual([]);
  });

  test('switches to right-to-left for Arabic', async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem('mc.lang', 'ar'));
    await page.goto('/');

    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await expect(page.getByTestId('sidebar')).toHaveAttribute('aria-label', 'القائمة الرئيسية');
    const sidebar = await page.getByTestId('sidebar').boundingBox();
    const content = await page.getByTestId('content').boundingBox();
    expect(sidebar!.x).toBeGreaterThan(content!.x);
  });
});
