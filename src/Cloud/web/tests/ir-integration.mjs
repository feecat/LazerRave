import assert from 'node:assert/strict';
import { createHash, randomUUID } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { readFileSync } from 'node:fs';

const base = process.env.LAZERRAVE_CLOUD_TEST_URL;
if (!base || !['127.0.0.1', 'localhost'].includes(new URL(base).hostname) || !process.env.ConnectionStrings__Postgres?.includes('lazerrave_test'))
  throw new Error('Use an isolated loopback server and lazerrave_test database.');
let checks = 0;
async function request(path, { token, method = 'GET', body, expected = 200 } = {}) {
  const form = body instanceof FormData;
  const response = await fetch(base + '/api' + path, { method, headers: { 'X-LazerRave': '1', ...(body && !form ? { 'Content-Type': 'application/json' } : {}), ...(token ? { Authorization: 'Bearer ' + token } : {}) }, body: body ? form ? body : JSON.stringify(body) : undefined });
  assert.equal(response.status, expected, `${path}: ${await response.clone().text()}`); checks++;
  const text = await response.text(); return text ? JSON.parse(text) : null;
}
const prefix = 'ir' + Date.now(); const password = 'isolated-ir-test-password';
const players = [];
for (let i = 0; i < 3; i++) {
  const username = prefix + '_' + i;
  const user = await request('/auth/register', { method: 'POST', body: { username, email: username + '@example.com', password } });
  const credentials = await request('/auth/token', { method: 'POST', body: { username, password } });
  players.push({ ...user, token: credentials.token });
}
const [owner, member, admin] = players;
execFileSync(process.env.LAZERRAVE_CLOUD_TEST_DOTNET, [process.env.LAZERRAVE_CLOUD_TEST_SERVER, '--grant-admin', admin.username], { windowsHide: true, stdio: 'pipe' });
function metadata(name) {
  const bytes = Buffer.from('#TITLE ' + name + '\n#BPM 150\n#PLAYER 1\n#PLAYLEVEL 12\n#00111:01\n');
  return { sha256: createHash('sha256').update(bytes).digest('hex'), md5: createHash('md5').update(bytes).digest('hex'), title: name, artist: 'Fixture', difficulty: 'ANOTHER', keys: 7, level: 12, bpm: 150, lengthMs: 123000 };
}
const chart = metadata('Private title ' + prefix);
await request('/charts/register', { method: 'POST', body: chart, expected: 401 });
await request('/charts/register', { token: owner.token, method: 'POST', body: { ...chart, visibility: 'hidden' }, expected: 400 });
await request('/charts/register', { token: owner.token, method: 'POST', body: { ...chart, sha256: 'invalid' }, expected: 400 });
const privateBoard = await request('/charts/register', { token: owner.token, method: 'POST', body: { ...chart, visibility: 'restricted' } });
assert.equal(privateBoard.visibility, 'restricted'); assert.notEqual(privateBoard.id, privateBoard.chartId);
assert.equal((await request('/charts/register', { token: owner.token, method: 'POST', body: { ...chart, visibility: 'restricted', title: 'Ignored replacement' } })).id, privateBoard.id);
assert.equal((await request('/charts/' + privateBoard.id, { token: owner.token })).title, chart.title);
function score(board, changes = {}) {
  return { chartId: board.chartId, boardId: board.id, clientRunId: randomUUID(), ruleset: 'openlr2-v1', arrangement: 'off', gauge: 'normal', perfect: 10, great: 5, good: 3, bad: 2, poor: 1, maxCombo: 10, clear: 'normal', scoreMax: 50, normalScore: 12345, inputType: 'keyboard', ...changes };
}
const privateInput = score(privateBoard);
const privateScore = await request('/scores', { token: owner.token, method: 'POST', body: privateInput });
assert.equal((await request('/scores', { token: owner.token, method: 'POST', body: privateInput })).id, privateScore.id);
assert.equal(privateScore.verified, false);
for (const path of ['/charts/' + privateBoard.id, '/rankings/' + privateBoard.id, '/rankings/' + privateBoard.id + '/summary', '/scores/' + privateScore.id]) {
  await request(path, { expected: 404 });
  await request(path, { token: member.token, expected: 404 });
  await request(path, { token: admin.token, expected: 404 });
}
await request('/scores', { token: member.token, method: 'POST', body: score(privateBoard), expected: 404 });
await request('/charts/resolve?sha256=' + chart.sha256, { expected: 404 });
assert.ok(!(await request('/charts')).some(row => row.id === privateBoard.id));
assert.ok(!(await request('/admin/charts', { token: admin.token })).some(row => row.id === privateBoard.id));
assert.equal((await request('/players/' + owner.uid)).scores.length, 0);
assert.equal((await request('/players/' + owner.uid + '/records')).length, 0);
assert.equal((await request('/players/' + owner.uid + '/records', { token: owner.token })).length, 1);
assert.ok((await request('/charts?mine=true', { token: owner.token })).some(row => row.id === privateBoard.id));
await request('/charts/' + privateBoard.id + '/members', { token: member.token, expected: 404 });
await request('/charts/' + privateBoard.id + '/members', { token: owner.token, method: 'POST', body: { uid: member.uid }, expected: 204 });
assert.equal((await request('/charts/' + privateBoard.id + '/members', { token: owner.token }))[0].uid, member.uid);
assert.equal((await request('/scores/' + privateScore.id, { token: member.token })).id, privateScore.id);
const memberPrivate = await request('/scores', { token: member.token, method: 'POST', body: score(privateBoard, { perfect: 12 }) });
assert.equal((await request('/rankings/' + privateBoard.id, { token: member.token })).length, 2);
assert.ok((await request('/charts?mine=true', { token: member.token })).some(row => row.id === privateBoard.id));
await request('/charts/' + privateBoard.id + '/visibility', { token: owner.token, method: 'PUT', body: { visibility: 'public', confirmPublication: true }, expected: 400 });
await request('/charts/' + privateBoard.id + '/visibility', { token: admin.token, method: 'PUT', body: { visibility: 'public', confirmPublication: true }, expected: 400 });

const community = await request('/charts/register', { token: member.token, method: 'POST', body: { ...chart, title: 'Community title ' + prefix, visibility: 'unlisted' } });
assert.equal(community.chartId, privateBoard.chartId); assert.notEqual(community.id, privateBoard.id);
assert.equal(community.title, 'Community title ' + prefix);
assert.equal((await request('/charts/resolve?sha256=' + chart.sha256)).id, community.id);
assert.equal((await request('/rankings/' + community.id)).length, 0);
assert.ok(!(await request('/charts')).some(row => row.id === community.id));
const publicScore = await request('/scores', { token: owner.token, method: 'POST', body: { ...privateInput, boardId: community.id } });
assert.notEqual(publicScore.id, privateScore.id);
const tieScore = await request('/scores', { token: member.token, method: 'POST', body: score(community, { bad: 0, poor: 0, maxCombo: 18, clear: 'hard', gauge: 'hard' }) });
let board = await request('/rankings/' + community.id);
assert.equal(board.length, 2); assert.equal(board[0].rank, 1); assert.equal(board[1].rank, 1);
assert.equal(board[0].id, publicScore.id); assert.equal(board[1].id, tieScore.id);
const myPosition = await request('/rankings/' + community.id + '/me', { token: member.token });
assert.equal(myPosition.rank, 1); assert.equal(myPosition.position, 2);
assert.equal((await request('/rankings/' + community.id + '/history', { token: owner.token })).length, 1);
assert.equal((await request('/players/' + owner.uid)).scores.length, 0);
await request('/charts/' + community.id + '/visibility', { token: member.token, method: 'PUT', body: { visibility: 'public' }, expected: 400 });
await request('/charts/' + community.id + '/visibility', { token: owner.token, method: 'PUT', body: { visibility: 'public', confirmPublication: true }, expected: 404 });
await request('/charts/' + community.id + '/visibility', { token: member.token, method: 'PUT', body: { visibility: 'public', confirmPublication: true }, expected: 204 });
assert.ok((await request('/charts?keys=7&minimum=12&maximum=12')).some(row => row.id === community.id));
assert.equal((await request('/players/' + owner.uid)).scores.length, 1);
assert.equal((await request('/players/' + member.uid + '/records')).length, 1);
const detail = await request('/scores/' + publicScore.id);
assert.ok(!Object.hasOwn(detail, 'reviewNote'));
assert.equal(detail.title, community.title); assert.equal(detail.chartId, community.id); assert.equal(detail.replayAvailable, false);
assert.equal((await request('/rankings/' + community.id + '?verified=true')).length, 0);
const comparison = await request('/players/' + owner.uid + '/compare?against=' + member.uid);
assert.equal(comparison.length, 1); assert.equal(comparison[0].difference, 0); assert.equal(comparison[0].chartId, community.id);
await request('/players/' + owner.uid + '/compare?against=' + owner.uid, { expected: 400 });

const table = await request('/admin/tables', { token: admin.token, method: 'POST', body: { name: prefix, symbol: '★', description: '', entries: [{ md5: chart.md5, level: '1', title: 'Table fallback', artist: '', url: null }, { md5: 'e'.repeat(32), level: '1', title: 'Unregistered', artist: '', url: null }] } });
await request('/admin/tables/' + table.id + '/publication', { token: admin.token, method: 'PUT', body: { published: true }, expected: 204 });
const entries = await request('/tables/' + table.id + '/entries');
assert.equal(entries[0].title, community.title); assert.equal(entries[0].chartId, community.id); assert.equal(entries[0].bestClear, null);
assert.equal((await request('/tables/' + table.id + '/entries', { token: member.token }))[0].bestClear, 'hard');
const progress = (await request('/tables/' + table.id, { token: member.token })).levels[0];
assert.deepEqual([progress.chartCount, progress.played, progress.cleared, progress.hard, progress.fullCombo], [2, 1, 1, 1, 0]);
assert.equal((await request('/tables/' + table.id)).levels[0].played, 0);
await request('/charts/' + privateBoard.id + '/members/' + member.uid, { token: owner.token, method: 'DELETE', expected: 204 });
await request('/scores/' + memberPrivate.id, { token: member.token, expected: 404 });
await request('/rankings/' + privateBoard.id + '/history', { token: member.token, expected: 404 });
assert.ok(!(await request('/charts?mine=true', { token: member.token })).some(row => row.id === privateBoard.id));

await request('/admin/scores/' + tieScore.id + '/review', { token: owner.token, method: 'PUT', body: { withdrawn: true, reason: 'test' }, expected: 403 });
await request('/admin/scores/' + tieScore.id + '/review', { token: admin.token, method: 'PUT', body: { withdrawn: true, reason: '' }, expected: 400 });
await request('/admin/scores/' + tieScore.id + '/review', { token: admin.token, method: 'PUT', body: { withdrawn: true, reason: 'Isolated review fixture' }, expected: 204 });
await request('/scores/' + tieScore.id, { expected: 404 });
assert.equal((await request('/rankings/' + community.id)).length, 1);
assert.equal((await request('/rankings/' + community.id + '/summary')).players, 1);
assert.equal((await request('/players/' + owner.uid + '/compare?against=' + member.uid)).length, 0);
await request('/admin/scores/' + tieScore.id + '/review', { token: admin.token, method: 'PUT', body: { withdrawn: false, reason: 'Fixture restored' }, expected: 204 });
assert.equal((await request('/rankings/' + community.id)).length, 2);
await request('/charts/' + community.id + '/visibility', { token: admin.token, method: 'PUT', body: { visibility: 'hidden' }, expected: 204 });
await request('/charts/' + community.id, { token: admin.token, expected: 404 });
await request('/charts/register', { token: member.token, method: 'POST', body: { ...chart, visibility: 'unlisted' }, expected: 404 });
assert.equal((await request('/players/' + owner.uid)).scores.length, 0);
assert.equal((await request('/tables/' + table.id + '/entries'))[0].chartId, null);
assert.equal((await request('/tables/' + table.id, { token: member.token })).levels[0].played, 0);
await request('/charts/' + community.id + '/visibility', { token: admin.token, method: 'PUT', body: { visibility: 'public', confirmPublication: true }, expected: 204 });

const differentSha = { ...chart, sha256: 'f'.repeat(64), title: 'Different SHA same MD5' };
const separate = await request('/charts/register', { token: member.token, method: 'POST', body: differentSha });
assert.notEqual(separate.chartId, community.chartId); assert.equal((await request('/rankings/' + separate.id)).length, 0);
await request('/charts/register', { token: member.token, method: 'POST', body: { ...chart, md5: 'd'.repeat(32) }, expected: 409 });
await request('/scores', { token: member.token, method: 'POST', body: score(separate, { chartId: community.chartId }), expected: 404 });
await request('/charts?minimum=20&maximum=10', { expected: 400 });
await request('/admin/charts', { token: owner.token, expected: 403 });

const realtime = new HubConnectionBuilder().withUrl(base + '/hubs/realtime', { accessTokenFactory: () => owner.token }).configureLogging(LogLevel.None).build();
try {
  await realtime.start();
  const room = await realtime.invoke('CreateRoom', 'IR registration fixture');
  const local = metadata('Room selected chart ' + prefix);
  const selected = await realtime.invoke('SelectLocalChart', { sha256: local.sha256, title: local.title, artist: local.artist, keys: local.keys, level: local.level }, room.version);
  const registered = await request('/charts/register', { token: owner.token, method: 'POST', body: local });
  assert.equal(registered.chartId, selected.chart.id); assert.equal(registered.md5, local.md5);
  assert.equal(registered.visibility, 'public');
  if (process.env.LAZERRAVE_TEST_PACK_CHART) {
    const identity = JSON.parse(process.env.LAZERRAVE_TEST_PACK_CHART);
    const privateTitle = 'Never expose this private title ' + prefix;
    const privatePack = await request('/charts/register', { token: owner.token, method: 'POST', body: { ...metadata(privateTitle), ...identity, visibility: 'restricted' } });
    const upload = new FormData(); upload.set('title', 'Scope fixture pack'); upload.set('description', ''); upload.set('file', new Blob([readFileSync(process.env.LAZERRAVE_CLOUD_TEST_ZIP)], { type: 'application/zip' }), 'fixture.zip');
    const pack = await request('/admin/packs', { token: admin.token, method: 'POST', body: upload });
    await request('/charts/' + privatePack.chartId + '/visibility', { token: admin.token, method: 'PUT', body: { visibility: 'hidden' }, expected: 204 });
    await request('/admin/packs/' + pack.id + '/publication', { token: admin.token, method: 'PUT', body: { published: true }, expected: 204 });
    const catalog = await request('/packs/' + pack.id);
    const publishedChart = catalog.charts.find(row => row.id === privatePack.chartId);
    assert.ok(publishedChart); assert.notEqual(publishedChart.title, privateTitle);
    await request('/charts/' + privatePack.chartId, { expected: 404 });
    const roomWithPack = await realtime.invoke('SelectChart', privatePack.chartId, pack.id, selected.version);
    assert.equal(roomWithPack.chart.title, publishedChart.title);
    assert.equal((await request('/charts/' + privatePack.id, { token: owner.token })).title, privateTitle);
    await request('/charts/' + privatePack.chartId + '/visibility', { token: admin.token, method: 'PUT', body: { visibility: 'unlisted' }, expected: 204 });
  }
} finally { await realtime.stop(); }

if (process.env.LAZERRAVE_TEST_UPGRADE === '1') {
  const oldPublic = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1', oldHidden = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2';
  assert.equal((await request('/charts/' + oldPublic)).title, 'Legacy public');
  const oldBoard = await request('/rankings/' + oldPublic); assert.equal(oldBoard[0].exScore, 25);
  assert.equal(oldBoard[0].normalScore, 12000); assert.equal(oldBoard[0].verified, false);
  await request('/charts/' + oldHidden, { expected: 404 });
  await request('/charts/register', { token: owner.token, method: 'POST', body: { ...metadata('Legacy'), sha256: '2'.repeat(64), md5: '2'.repeat(32) }, expected: 404 });
  assert.equal((await request('/admin/charts', { token: admin.token })).find(row => row.id === oldHidden).visibility, 'hidden');
}
// Public registration, actual play time and independent directory/board sorts.
const low = await request('/charts/register', { token: owner.token, method: 'POST', body: { ...metadata('Upload policy low ' + prefix), level: 3, difficulty: 'NORMAL' } });
const high = await request('/charts/register', { token: owner.token, method: 'POST', body: { ...metadata('Upload policy high ' + prefix), level: 15 } });
assert.equal(low.visibility, 'public'); assert.equal(high.visibility, 'public');
const lowInput = score(low, { playedAt: '2025-01-01T12:34:56.1234567+08:00' });
const oldPlay = await request('/scores', { token: owner.token, method: 'POST', body: lowInput });
assert.equal(Date.parse(oldPlay.playedAt), Date.parse(lowInput.playedAt));
assert.ok(Date.parse(oldPlay.createdAt) > Date.parse(oldPlay.playedAt));
assert.equal((await request('/scores', { token: owner.token, method: 'POST', body: lowInput })).id, oldPlay.id);
assert.equal((await request('/scores/' + oldPlay.id)).playedAt, oldPlay.playedAt);
assert.equal((await request('/rankings/' + low.id))[0].playedAt, oldPlay.playedAt);
assert.equal((await request('/rankings/' + low.id + '/history', { token: owner.token }))[0].playedAt, oldPlay.playedAt);
assert.equal((await request('/players/' + owner.uid + '/records?recent=true')).find(row => row.id === oldPlay.id).playedAt, oldPlay.playedAt);
await request('/scores', { token: owner.token, method: 'POST', body: { ...lowInput, playedAt: '2025-02-01T00:00:00Z' }, expected: 409 });
await request('/scores', { token: owner.token, method: 'POST', body: score(low, { playedAt: '2099-01-01T00:00:00Z' }), expected: 400 });
const recent = new Date(Date.now() - 60000).toISOString();
await request('/scores', { token: owner.token, method: 'POST', body: score(high, { playedAt: recent }) });
await request('/scores', { token: owner.token, method: 'POST', body: score(high, { perfect: 8, great: 3, playedAt: recent }) });
await request('/scores', { token: member.token, method: 'POST', body: score(high, { perfect: 12, playedAt: '2024-01-01T00:00:00Z' }) });
const list = suffix => request('/charts?q=' + encodeURIComponent('Upload policy') + '&' + suffix);
assert.equal((await list('sort=level-asc'))[0].id, low.id);
assert.equal((await list('sort=level-desc'))[0].id, high.id);
assert.equal((await list('sort=newest'))[0].id, high.id);
assert.equal((await list('sort=recent'))[0].id, high.id);
const byPlays = await list('sort=plays'); assert.equal(byPlays[0].id, high.id); assert.equal(byPlays[0].playCount, 3); assert.equal(byPlays[0].playerCount, 2);
assert.deepEqual((await list('difficulty=NORMAL')).map(row => row.id), [low.id]);
assert.deepEqual((await list('minimum=10&maximum=20&keys=7')).map(row => row.id), [high.id]);
await request('/charts?sort=invalid', { expected: 400 });
const scoreOrder = await request('/rankings/' + high.id + '?sort=score'); assert.equal(scoreOrder[0].uid, member.uid);
const playOrder = await request('/rankings/' + high.id + '?sort=plays'); assert.equal(playOrder[0].uid, owner.uid); assert.equal(playOrder[0].playCount, 2); assert.equal(playOrder[0].rank, 2);
const dateOrder = await request('/rankings/' + high.id + '?sort=recent'); assert.equal(dateOrder[0].uid, owner.uid); assert.equal(dateOrder[0].rank, 2);
await request('/rankings/' + high.id + '?sort=invalid', { expected: 400 });
const unknown = await request('/scores', { token: owner.token, method: 'POST', body: score(low) }); assert.equal(unknown.playedAt, null);
const backfill = { ...score(low, { playedAt: '2023-01-01T00:00:00Z' }), clientRunId: unknown.clientRunId };
assert.equal(Date.parse((await request('/scores', { token: owner.token, method: 'POST', body: backfill })).playedAt), Date.parse(backfill.playedAt));
await request('/charts/' + low.id + '/visibility', { token: admin.token, method: 'PUT', body: { visibility: 'hidden' }, expected: 204 });
await request('/charts/register', { token: owner.token, method: 'POST', body: { ...metadata('Upload policy low ' + prefix), level: 3 }, expected: 404 });
assert.ok(!(await list('sort=plays')).some(row => row.id === low.id));

console.log(`Independent IR integration passed: ${checks} HTTP assertions, with privacy, migration, scope isolation, progress, ties and moderation checks.`);
