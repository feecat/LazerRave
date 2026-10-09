import { defineConfig } from '@playwright/test';
import { fileURLToPath, URL } from 'node:url';

const baseURL = process.env.LAZERRAVE_CLOUD_TEST_URL;
if (!baseURL || !['127.0.0.1', 'localhost'].includes(new URL(baseURL).hostname)) throw new Error('Use an isolated loopback cloud server for browser checks.');
export default defineConfig({
  testDir: './tests', testMatch: 'ui.spec.ts', workers: 1, retries: 0,
  outputDir: fileURLToPath(new URL('../../../out/reports/cloud-test/browser', import.meta.url)),
  use: { baseURL, channel: 'msedge', headless: true, viewport: { width: 1440, height: 1000 } },
  reporter: 'list',
});
