import { Page, expect } from '@playwright/test';

/** Demo accounts of 08-seed-data.md. */
export const accounts = {
  platformAdmin: { email: 'admin@monitor.local', password: 'Admin@12345' },
  acmeAdmin: { email: 'admin@acme.test', password: 'Demo@12345' },
  acmeViewer: { email: 'viewer@acme.test', password: 'Demo@12345' },
  oasisAdmin: { email: 'admin@oasis.test', password: 'Demo@12345' },
};

export async function signIn(page: Page, account: { email: string; password: string }): Promise<void> {
  await page.goto('/login');
  await page.getByTestId('email').fill(account.email);
  await page.getByTestId('password').fill(account.password);
  await page.getByTestId('sign-in').click();
}

export async function signedIn(page: Page, account: { email: string; password: string }, home: RegExp): Promise<void> {
  await signIn(page, account);
  await expect(page).toHaveURL(home);
}
