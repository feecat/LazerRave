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

test('Visual effects preserve navigation, readable type, real score links and reduced motion', async ({ page }) => {
  await page.route('**/api/me', route => route.fulfill({ json: null }));
  await page.route('**/api/scores/recent', route => route.fulfill({ json: [{
    id: '00000000-0000-0000-0000-000000000001', chartId: '00000000-0000-0000-0000-000000000002',
    title: 'Halcyon', artist: 'xi', difficulty: 'HYPER', keys: 7, level: 10,
    uid: 1, displayName: 'Score Player', avatarUrl: null, exScore: 1500, scoreMax: 2000,
    clear: 'full-combo', createdAt: '2026-10-10T08:09:00Z',
  }] }));
  await page.goto('/');
  const card = page.locator('.feature').first();
  await expect(page.locator('.feature-icon')).toHaveCount(3);
  await expect(page.locator('.clear-full-combo')).toHaveCSS('animation-name', 'full-combo-shimmer');
  await card.hover();
  await expect.poll(() => card.evaluate(element => element.style.getPropertyValue('--spotlight-x'))).not.toBe('');
  await expect(card).toHaveAttribute('href', '/rankings');
  await expect(page.getByRole('link', { name: '1,500', exact: true })).toHaveAttribute('href', '/scores/00000000-0000-0000-0000-000000000001');
  expect(await page.locator('.nav-links a').first().evaluate(element => parseFloat(getComputedStyle(element).fontSize))).toBeGreaterThanOrEqual(17);
  expect(await page.locator('.table-secondary').first().evaluate(element => parseFloat(getComputedStyle(element).fontSize))).toBeGreaterThanOrEqual(15);
  await page.route('**/api/packs/catalog?**', route => route.fulfill({ json: { items: [], total: 0, pageSize: 50 } }));
  await page.getByRole('navigation').getByRole('link', { name: 'Song packs' }).click();
  await expect(page.locator('.nav-links a.active')).toContainText('Song packs');
  expect(await page.locator('.nav-links a.active').evaluate(element => getComputedStyle(element, '::after').content)).toBe('none');
  await page.goto('/');
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await expect(page.locator('.clear-full-combo')).toHaveCSS('animation-name', 'none');
  await expect(page.locator('.rhythm-logo')).toHaveCSS('transform', 'none');
  expect(await page.locator('.rhythm-lanes').evaluate(element => getComputedStyle(element).transform)).toMatch(/^matrix\(1, 0, 0, 1,/);
  for (const width of [1024, 768, 390, 320]) {
    await page.setViewportSize({ width, height: 844 });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  }
});

test('Styled public pages retain catalog links and fit desktop, tablet and mobile layouts', async ({ page }, testInfo) => {
  test.setTimeout(60000);
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.route('https://api.github.com/**', route => route.fulfill({ json: [] }));
  await page.route('**/api/**', route => {
    const path = new URL(route.request().url()).pathname;
    const data = path === '/api/me' ? null : path === '/api/packs/catalog' ? {
      items: [{ id: 'pack-1', title: 'Fixture pack', description: '', sha256: '', sizeBytes: 1024, createdAt: '2026-10-10T08:00:00Z', chartCount: 12, keys: [7, 14], minimumLevel: 1, maximumLevel: 12 }], total: 1, page: 1, pageSize: 50,
    } : path === '/api/songs' ? {
      items: [{ songKey: 'fixture-song', title: 'Fixture song', artist: 'Fixture artist', chartCount: 3, keys: [7, 14], minimumLevel: 1, maximumLevel: 12, approved: true, playCount: 12, playerCount: 3, lastPlayedAt: '2026-10-10T08:00:00Z' }], total: 1, page: 1, pageSize: 50,
    } : path === '/api/tables' ? [{ id: 1, name: 'Fixture table', symbol: '★', description: 'A published table', sourceUrl: null, published: true, chartCount: 120, levelCount: 12 }] : [];
    return route.fulfill({ json: data });
  });
  for (const locale of ['en', 'zh-CN']) {
    await page.goto('/');
    await page.locator('.language-switch select').selectOption(locale);
    for (const width of [1440, 768, 390, 320]) {
      await page.setViewportSize({ width, height: 1000 });
      for (const path of ['/', '/packs', '/rankings', '/tables', '/download', '/login', '/register']) {
        await page.goto(path);
        await expect(page.locator('main h1, main h2').first()).toBeVisible();
        if (path === '/packs') await expect(page.getByRole('link', { name: 'Fixture pack', exact: true })).toHaveAttribute('href', '/packs/pack-1');
        if (path === '/rankings') await expect(page.getByRole('link', { name: 'Fixture song', exact: true })).toHaveAttribute('href', '/rankings/songs/fixture-song');
        if (path === '/tables') await expect(page.locator('.difficulty-card')).toHaveAttribute('href', '/tables/1');
        if (path === '/download') await expect(page.locator('.download-card')).toBeVisible();
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${locale} ${path} at ${width}px`).toBe(true);
        if (width === 1440 || width === 390) await page.screenshot({ path: testInfo.outputPath(`${locale}-${path.slice(1) || 'home'}-${width}.png`), fullPage: true });
      }
    }
  }
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
