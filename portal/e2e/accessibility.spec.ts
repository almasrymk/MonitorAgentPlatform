import AxeBuilder from '@axe-core/playwright';
import { Page, expect, test } from '@playwright/test';

import { accounts, signedIn } from './helpers';

/** Serious and critical axe violations of WCAG 2.1 A/AA on the current page (MC-1002). */
async function violations(page: Page): Promise<string[]> {
  await page.waitForLoadState('networkidle');
  const result = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze();
  return result.violations
    .filter((v) => v.impact === 'serious' || v.impact === 'critical')
    .map((v) => `${v.id} (${v.impact}): ${v.help} - ${v.nodes.slice(0, 3).map((n) => n.target.join(' ')).join(' | ')}`);
}

async function check(page: Page, url: string): Promise<void> {
  await page.goto(url);
  await expect(page.locator('main h1').first()).toBeVisible();
  expect(await violations(page), url).toEqual([]);
}

const customerScreens = ['/app/overview', '/app/locations', '/app/devices', '/app/subscription', '/app/reports', '/app/users', '/app/archive', '/app/settings', '/app/notifications'];
const platformScreens = ['/admin/dashboard', '/admin/customers', '/admin/plans', '/admin/notifications', '/admin/archive', '/admin/reports', '/admin/users', '/admin/settings', '/admin/audit'];

/** Puts the portal in the given language (the choice is saved on the account, so an earlier run may have changed it). */
async function useLanguage(page: Page, language: 'en' | 'ar'): Promise<void> {
  const html = page.locator('html');
  if ((await html.getAttribute('lang')) !== language) {
    await page.getByTestId('user-menu').click();
    await page.getByTestId('language-switch').click();
  }
  await expect(html).toHaveAttribute('lang', language);
  await expect(html).toHaveAttribute('dir', language === 'ar' ? 'rtl' : 'ltr');
}

test.describe('Accessibility', () => {
  // Each test runs axe on many screens.
  test.describe.configure({ timeout: 180_000 });

  test('the sign-in page has no serious violations', async ({ page }) => {
    await page.goto('/login');
    expect(await violations(page)).toEqual([]);
  });

  for (const language of ['en', 'ar'] as const) {
    test(`customer screens have no serious violations (${language})`, async ({ page }) => {
      await signedIn(page, accounts.acmeAdmin, /\/app\/overview$/);
      await useLanguage(page, language);
      for (const url of customerScreens) {
        await check(page, url);
      }
    });
  }

  test('platform screens have no serious violations', async ({ page }) => {
    await signedIn(page, accounts.platformAdmin, /\/admin\/dashboard$/);
    await useLanguage(page, 'en');
    for (const url of platformScreens) {
      await check(page, url);
    }
  });

  test('the device screen has no serious violations and its tabs work with the keyboard', async ({ page }) => {
    await signedIn(page, accounts.acmeAdmin, /\/app\/overview$/);
    await useLanguage(page, 'en');
    await page.goto('/app/devices?search=WEB-SRV-01');
    await page.getByTestId('device-card').first().getByTestId('open-console').click();
    await expect(page.getByTestId('device-title')).toBeVisible();
    expect(await violations(page)).toEqual([]);

    const settings = page.getByTestId('nav-settings');
    await settings.focus();
    await page.keyboard.press('Enter');
    await expect(page).toHaveURL(/\/settings$/);
  });
});
