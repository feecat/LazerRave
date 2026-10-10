import { test, expect } from '@playwright/test';

const chart = { id: '11111111-1111-1111-1111-111111111111', title: 'Ranking fixture', artist: 'Fixture', difficulty: 'ANOTHER', keys: 7, level: 12, md5: 'a'.repeat(32), sha256: 'b'.repeat(64), packId: '22222222-2222-2222-2222-222222222222' };
const score = { id: '33333333-3333-3333-3333-333333333333', rank: 1, uid: 42, username: 'fixture', displayName: 'Fixture Player', exScore: 25, scoreMax: 50, normalScore: 12345, misses: 3, minMisses: 0, maxCombo: 10, clear: 'normal', bestClear: 'hard', letterRank: 'C', perfect: 10, great: 5, good: 3, bad: 2, poor: 1, arrangement: 'off', gauge: 'normal', inputType: 'keyboard', comment: '', verified: false, createdAt: '2026-10-01T12:00:00Z' };

test('IR content distinguishes the scoring play from independent records and follows filters in both languages', async ({ page }) => {
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.route('**/api/**', route => {
    const url = new URL(route.request().url());
    let json: unknown = null;
    if (url.pathname === '/api/charts') json = [chart];
    else if (url.pathname === '/api/charts/' + chart.id) json = chart;
    else if (url.pathname.endsWith('/summary')) {
      const hard = url.searchParams.get('gauge') === 'hard';
      json = { submissions: hard ? 1 : 3, players: hard ? 1 : 2, clearedPlayers: hard ? 1 : 2, failed: 0, assist: 0, easy: 0, normal: hard ? 0 : 1, hard: 1, fullCombo: 0, perfect: 0 };
    } else if (url.pathname === '/api/rankings/' + chart.id) json = [score];
    return route.fulfill({ json });
  });
  await page.goto('/rankings/' + chart.id);
  const overview = page.getByRole('region', { name: 'Ranking overview' });
  await expect(overview.getByText('100.00%', { exact: true })).toBeVisible();
  await expect(overview.locator('dl > div').filter({ hasText: 'Players' }).locator('dd')).toHaveText('2');
  await expect(page.getByRole('columnheader', { name: 'Best clear' })).toBeVisible();
  const button = page.getByRole('button', { name: 'Score details', exact: true });
  await expect(button).toHaveAttribute('aria-expanded', 'false');
  await button.focus(); await page.keyboard.press('Enter');
  await expect(page.getByText('12,345', { exact: true })).toBeVisible();
  const details = page.locator('.ir-detail-row');
  await expect(details.getByText('NORMAL', { exact: true })).toBeVisible();
  await expect(details.getByText('10 / 5 / 3 / 2 / 1', { exact: true })).toBeVisible();
  await expect(details.getByText('EX SCORE, judgements and options describe', { exact: false })).toBeVisible();
  await page.getByText('Chart identity', { exact: true }).click();
  await expect(page.getByText('MD5 ' + chart.md5, { exact: true })).toBeVisible();
  await page.getByLabel('Gauge', { exact: true }).selectOption('hard');
  await expect(overview.locator('dl > div').filter({ hasText: 'Players' }).locator('dd')).toHaveText('1');
  await page.getByLabel('Language', { exact: true }).selectOption('zh-CN');
  await expect(page.getByRole('columnheader', { name: '最佳通关' })).toBeVisible();
  await expect(page.getByRole('region', { name: '排行统计' })).toBeVisible();
  await page.getByRole('button', { name: '收起详情', exact: true }).click();
  await expect(details).toHaveCount(0);
  await page.setViewportSize({ width: 390, height: 844 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  expect(errors).toEqual([]);
});
