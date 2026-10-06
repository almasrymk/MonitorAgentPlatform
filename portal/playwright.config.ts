import { defineConfig, devices } from '@playwright/test';

// End-to-end tests (09 section 7) against the built portal and the API with the demo seed.
// Screenshots use 1672 x 941, dark theme, animations disabled.
const external = process.env['E2E_BASE_URL'];

export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env['CI'],
  retries: process.env['CI'] ? 1 : 0,
  reporter: process.env['CI'] ? [['html', { open: 'never' }], ['list']] : 'list',
  use: {
    baseURL: external ?? 'http://localhost:4300',
    viewport: { width: 1672, height: 941 },
    colorScheme: 'dark',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  expect: {
    toHaveScreenshot: { maxDiffPixelRatio: 0.002, animations: 'disabled' },
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'], viewport: { width: 1672, height: 941 } } }],
  webServer: external
    ? undefined
    : [
        ...(process.env['E2E_API_RUNNING'] ? [] : [{
          command: 'dotnet run --project ../src/MonitorCloud.Api --launch-profile http',
          url: 'http://localhost:5300/health/ready',
          reuseExistingServer: !process.env['CI'],
          timeout: 240_000,
        }]),
        {
          command: 'npm start',
          url: 'http://localhost:4300',
          reuseExistingServer: !process.env['CI'],
          timeout: 180_000,
        },
      ],
});
