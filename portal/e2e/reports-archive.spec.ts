import { readFileSync } from 'node:fs';

import { Page, expect, test } from '@playwright/test';

import { accounts, signedIn } from './helpers';

/** Opens every sidebar item and checks that each shows a working screen (no placeholder, no load error). */
async function everyMenuItemWorks(page: Page, items: string[]): Promise<void> {
  for (const item of items) {
    await page.getByTestId(`nav-${item}`).click();
    await expect(page.locator('main h1').first(), item).toBeVisible();
    await expect(page.getByText('This screen is not available yet'), item).toHaveCount(0);
    await expect(page.getByTestId('retry'), item).toHaveCount(0);
  }
}

// M9 acceptance: every menu item opens a working screen; reports, archive and settings work end to end on the demo seed.
test.describe('Reports, archive and settings', () => {
  test('every platform menu item opens a working screen', async ({ page }) => {
    await signedIn(page, accounts.platformAdmin, /\/admin\/dashboard$/);
    await everyMenuItemWorks(page, ['dashboard', 'customers', 'plans', 'notifications', 'archive', 'reports', 'users', 'settings']);
  });

  test('every customer menu item, location tab and device tab opens a working screen', async ({ page }) => {
    await signedIn(page, accounts.acmeAdmin, /\/app\/overview$/);
    await everyMenuItemWorks(page, ['overview', 'locations', 'devices', 'subscription', 'reports', 'users', 'archive', 'settings']);

    await page.goto('/app/locations');
    await page.getByText('Cairo HQ').first().click();
    for (const tab of ['overview', 'devices', 'notifications', 'archive', 'reports', 'settings']) {
      await page.getByTestId(`tab-${tab}`).click();
      await expect(page.getByTestId('retry'), tab).toHaveCount(0);
    }
    await expect(page.getByTestId('location-name')).toHaveValue('Cairo HQ');
  });

  test('a customer generates a CSV report and downloads it', async ({ page }) => {
    await signedIn(page, accounts.acmeViewer, /\/app\/overview$/);
    await page.getByTestId('nav-reports').click();
    await page.getByTestId('type-location-summary').click();
    await page.getByTestId('generate-report').click();

    const item = page.getByTestId('recent-reports').locator('li').filter({ hasText: 'Location Summary' }).first();
    await expect(item.getByTestId('download-report')).toBeVisible({ timeout: 20_000 });
    const [download] = await Promise.all([page.waitForEvent('download'), item.getByTestId('download-report').click()]);
    expect(download.suggestedFilename()).toMatch(/^location-summary-.*\.csv$/);
    const csv = readFileSync((await download.path())!, 'utf8');
    expect(csv).toContain('Cairo HQ');
  });

  test('the customer archive keeps notes and files, without internal items', async ({ page }) => {
    await signedIn(page, accounts.acmeAdmin, /\/app\/overview$/);
    await page.getByTestId('nav-archive').click();
    await expect(page.getByTestId('company-details')).toContainText('Acme Corporation');
    await expect(page.getByRole('tab', { name: 'Remote Access' })).toHaveCount(0);

    await page.getByRole('tab', { name: 'Notes' }).click();
    await expect(page.locator('[data-testid^="note-"]').filter({ hasText: 'Renewal discussion' })).toHaveCount(0);
    await page.getByTestId('note-body').fill('E2E: call the IT director next week');
    await page.getByTestId('save-note').click();
    await expect(page.getByText('E2E: call the IT director next week')).toBeVisible();

    await page.getByRole('tab', { name: 'Files & Attachments' }).click();
    await page.getByTestId('upload-input').setInputFiles({ name: 'e2e-notes.txt', mimeType: 'text/plain', buffer: Buffer.from('uploaded by the e2e suite') });
    const row = page.getByTestId('files').locator('li').filter({ hasText: 'e2e-notes.txt' });
    await expect(row).toHaveCount(1);
    const [download] = await Promise.all([page.waitForEvent('download'), row.getByRole('button', { name: /Download/ }).click()]);
    expect(readFileSync((await download.path())!, 'utf8')).toBe('uploaded by the e2e suite');
    await row.getByRole('button', { name: /Delete/ }).click();
    await expect(row).toHaveCount(0);

    await page.getByTestId('upload-input').setInputFiles({ name: 'tool.exe', mimeType: 'application/octet-stream', buffer: Buffer.from('MZ') });
    await expect(page.getByText('This file type is not allowed', { exact: false })).toBeVisible();
  });

  test('platform staff open a customer archive and reveal a remote access entry', async ({ page }) => {
    await signedIn(page, accounts.platformAdmin, /\/admin\/dashboard$/);
    await page.getByTestId('nav-archive').click();
    await page.getByTestId('open-archive-ACME').click();
    await page.getByTestId('reason').fill('E2E: checking the remote access details');
    await page.getByTestId('confirm').click();
    await expect(page).toHaveURL(/\/admin\/customers\/[^/]+\/archive$/);

    await page.getByRole('tab', { name: 'Remote Access' }).click();
    const table = page.getByTestId('remote-access');
    const desk = table.locator('tr').filter({ hasText: 'Front desk PC' });
    await expect(desk).toContainText('12••••89');
    await desk.locator('[data-testid^="reveal-"]').click();
    await expect(desk).toContainText('123 456 789');
  });

  test('the top-bar search opens the filtered device list', async ({ page }) => {
    await signedIn(page, accounts.acmeAdmin, /\/app\/overview$/);
    await page.getByTestId('topbar-search').fill('FILE-SRV-01');
    await page.getByTestId('topbar-search').press('Enter');
    await expect(page).toHaveURL(/\/app\/devices\?search=FILE-SRV-01$/);
    await expect(page.getByTestId('device-card').first()).toContainText('FILE-SRV-01');
  });
});
