import { test, expect } from '@playwright/test';

const repository = 'https://github.com/feecat/LazerRave';
const apiPattern = 'https://api.github.com/repos/feecat/LazerRave/releases*';
const filename = 'LazerRave-0.1.0-beta.1-win-x64.zip';
const downloadUrl = `${repository}/releases/download/v0.1.0-beta.1/${filename}`;
const release = {
  tag_name: 'v0.1.0-beta.1', html_url: `${repository}/releases/tag/v0.1.0-beta.1`,
  published_at: '2026-10-09T11:00:00Z', draft: false, prerelease: true,
  assets: [{ name: filename, browser_download_url: downloadUrl, size: 183710000, digest: 'sha256:' + 'a'.repeat(64) },
    { name: filename + '.sha256', browser_download_url: downloadUrl + '.sha256', size: 108 }],
};

test.beforeEach(async ({ page }) => {
  await page.route('**/api/**', route => route.fulfill({ json: new URL(route.request().url()).pathname === '/api/me' ? null : [] }));
});

test('Homepage pulses and falling notes animate; GitHub and download links work on desktop and mobile', async ({ page }, testInfo) => {
  await page.emulateMedia({ reducedMotion: 'no-preference' });
  await page.goto('/');
  const logo = page.locator('.rhythm-logo');
  await expect(logo).toBeVisible();
  const firstTransform = await logo.evaluate(node => getComputedStyle(node).transform);
  await expect.poll(() => logo.evaluate(node => getComputedStyle(node).transform)).not.toBe(firstTransform);
  await expect(page.locator('.falling-note')).toHaveCount(14);
  expect(await page.locator('.falling-note').first().evaluate(node => getComputedStyle(node).animationName)).toBe('rhythm-fall');
  await expect(page.getByRole('navigation').getByRole('link', { name: 'GitHub' })).toHaveAttribute('href', repository);
  await expect(page.getByRole('link', { name: 'Download LazerRave', exact: true })).toHaveAttribute('href', '/download');
  await page.screenshot({ path: testInfo.outputPath('homepage-desktop.png') });
  await page.emulateMedia({ reducedMotion: 'reduce' });
  expect(await logo.evaluate(node => getComputedStyle(node).animationName)).toBe('none');
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(logo).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('Downloads include Beta assets, checksum and published history, excluding draft and source-only releases', async ({ page }, testInfo) => {
  const old = { ...release, tag_name: 'v0.0.9-beta.1', html_url: `${repository}/releases/tag/v0.0.9-beta.1`,
    published_at: '2026-10-08T11:00:00Z', assets: [{ name: 'LazerRave-0.0.9-beta.1-win-x64.zip',
      browser_download_url: `${repository}/releases/download/v0.0.9-beta.1/LazerRave-0.0.9-beta.1-win-x64.zip`, size: 123456 }] };
  await page.route(apiPattern, route => route.fulfill({ json: [old, { ...release, draft: true, tag_name: 'v99.0.0' },
    { ...release, assets: [], tag_name: 'v98.0.0' }, release] }));
  await page.goto('/download');
  await expect(page.getByRole('heading', { name: 'Download LazerRave' })).toBeVisible();
  await expect(page.locator('.download-version')).toHaveText('0.1.0-beta.1');
  await expect(page.locator('.release-badge')).toHaveText('BETA');
  await expect(page.getByRole('link', { name: 'Download for Windows', exact: true })).toHaveAttribute('href', downloadUrl);
  await expect(page.getByRole('link', { name: 'SHA-256 checksum' })).toHaveAttribute('href', downloadUrl + '.sha256');
  await expect(page.locator('.release-history')).toContainText('0.0.9-beta.1');
  await expect(page.locator('.download-layout')).not.toContainText('99.0.0');
  await page.screenshot({ path: testInfo.outputPath('download-desktop.png') });
  await page.getByLabel('Language', { exact: true }).selectOption('zh-CN');
  await expect(page.getByRole('link', { name: '下载 Windows 版本', exact: true })).toHaveAttribute('href', downloadUrl);
  await page.setViewportSize({ width: 390, height: 844 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.screenshot({ path: testInfo.outputPath('download-mobile.png'), fullPage: true });
  await page.reload();
  await expect(page.getByRole('heading', { name: '下载 LazerRave', exact: true })).toBeVisible();
});

test('No-release and API failure states have usable GitHub fallbacks and retry without requiring login', async ({ page }) => {
  let unavailable = false;
  await page.route(apiPattern, route => route.fulfill({ status: unavailable ? 429 : 200, json: unavailable ? { message: 'Rate limited' } : [] }));
  await page.goto('/download');
  await expect(page.getByRole('heading', { name: 'No downloadable builds yet' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'All releases' })).toHaveAttribute('href', repository + '/releases');
  await expect(page.getByRole('link', { name: 'Download for Windows' })).toHaveCount(0);
  unavailable = true;
  await page.reload();
  await expect(page.getByRole('heading', { name: 'Releases are temporarily unavailable' })).toBeVisible();
  unavailable = false;
  await page.getByRole('button', { name: 'Try again' }).click();
  await expect(page.getByRole('heading', { name: 'No downloadable builds yet' })).toBeVisible();
});
