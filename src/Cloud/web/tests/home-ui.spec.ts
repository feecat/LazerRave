import { test, expect } from '@playwright/test';

test('Home shows uploaded scores and keeps the translucent navigation visible while scrolling', async ({ page }) => {
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.route('**/api/me', route => route.fulfill({ json: null }));
  await page.route('**/api/scores/recent', route => route.fulfill({ json: [{
    id: '00000000-0000-0000-0000-000000000001', chartId: '00000000-0000-0000-0000-000000000002',
    title: 'Halcyon', artist: 'xi', difficulty: 'HYPER', keys: 7, level: 10,
    uid: 1, displayName: 'Score Player', avatarUrl: null, exScore: 1500, scoreMax: 2000,
    clear: 'hard', createdAt: '2026-10-10T08:09:00Z',
  }] }));
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Latest scores', exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Recently added' })).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Halcyon', exact: true })).toHaveAttribute('href', '/rankings/00000000-0000-0000-0000-000000000002');
  await expect(page.locator('.recent-score-table .ir-player-cell')).toHaveAttribute('href', '/players/id/1');
  await expect(page.locator('.recent-score-table .ir-player-cell')).toContainText('Score Player');
  await expect(page.getByRole('link', { name: '1,500', exact: true })).toHaveAttribute('href', '/scores/00000000-0000-0000-0000-000000000001');
  await expect(page.locator('.recent-score-table time')).toHaveAttribute('datetime', '2026-10-10T08:09:00Z');
  await page.evaluate(() => scrollTo(0, 800));
  const header = page.locator('.site-header');
  expect((await header.boundingBox())!.y).toBe(0);
  expect(await header.evaluate(element => getComputedStyle(element).backdropFilter)).toContain('blur');
  await page.getByLabel('Language', { exact: true }).selectOption('zh-CN');
  await expect(page.getByRole('heading', { name: '最新成绩', exact: true })).toBeVisible();
  await expect(page.getByRole('columnheader', { name: '上传时间', exact: true })).toBeVisible();
  await page.setViewportSize({ width: 390, height: 844 });
  await page.evaluate(() => scrollTo(0, 700));
  expect((await header.boundingBox())!.y).toBe(0);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  expect(errors).toEqual([]);
});

test('Home distinguishes an empty public feed from an upload-feed error', async ({ page }) => {
  await page.route('**/api/me', route => route.fulfill({ json: null }));
  await page.route('**/api/scores/recent', route => route.fulfill({ json: [] }));
  await page.goto('/');
  await expect(page.getByText('No public scores yet.', { exact: true })).toBeVisible();
  await page.route('**/api/scores/recent', route => route.fulfill({ status: 503, json: { error: 'Request failed.' } }));
  await page.reload();
  await expect(page.getByRole('alert')).toContainText('Request failed.');
  await expect(page.getByText('No public scores yet.', { exact: true })).toHaveCount(0);
});
