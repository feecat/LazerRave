import { useEffect, useState, type FormEvent } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { api, send } from './api';
import { Alert, Avatar, Empty, Heading, SignInPrompt, useQuery } from './components';
import { useAuth } from './state';
import { useI18n } from './i18n';
import { IrOverview, IrScoreTable, playTime } from './ir';
import type { Chart, Score } from './types';

function useFilters() {
  const [params, setParams] = useSearchParams();
  function set(name: string, value: string) {
    const next = new URLSearchParams(params);
    if (value) next.set(name, value); else next.delete(name);
    if (name !== 'page') next.delete('page');
    setParams(next, { replace: name !== 'page' });
  }
  return { params, set, page: Math.max(1, Math.min(10000, Number(params.get('page')) || 1)) };
}
function Pagination({ page, next, change }: { page: number; next: boolean; change: (value: string) => void }) {
  const { t } = useI18n();
  return <div className="pagination"><button disabled={page === 1} onClick={() => change(String(page - 1))}>{t('Previous')}</button><span>{page}</span><button disabled={!next} onClick={() => change(String(page + 1))}>{t('Next')}</button></div>;
}
interface Comparison { chartId: string; title: string; keys: number; level: number; scoreId: string; rivalScoreId: string; exScore: number; rivalExScore: number; difference: number; minMisses: number; rivalMinMisses: number; lamp: number; rivalLamp: number }
export function RivalCompare({ uid }: { uid: number }) {
  const { t, locale } = useI18n(); const { user } = useAuth();
  const [against, setAgainst] = useState<number | null>(user && user.uid !== uid ? user.uid : null);
  const [page, setPage] = useState(1);
  const rows = useQuery<Comparison[]>(against ? `/players/${uid}/compare?against=${against}&page=${page}` : null);
  const lamps = ['FAILED', 'ASSIST', 'EASY', 'NORMAL', 'HARD', 'FULL COMBO', 'PERFECT'];
  function compare(e: FormEvent<HTMLFormElement>) { e.preventDefault(); const data = new FormData(e.currentTarget); setAgainst(Number(data.get('against'))); setPage(1); }
  return <section className="panel ir-history"><h2>{t('Compare players')}</h2><form className="filter-bar" onSubmit={compare}><label>{t('Compare with UID')}<input name="against" type="number" min={1} max={9007199254740991} required defaultValue={against ?? ''} /></label><button className="button secondary">{t('Compare')}</button></form><p className="ranking-note">{t('Only public charts played by both players are compared.')}</p><Alert message={rows.error} />{against && <><div className="table-wrap"><table><thead><tr><th>{t('Title')}</th><th>UID {uid}</th><th>UID {against}</th><th>{t('EX difference')}</th><th>{t('Best clear')}</th><th>{t('Minimum BP')}</th></tr></thead><tbody>{rows.data?.map(row => <tr key={row.chartId}><td><Link to={'/rankings/' + row.chartId}>{row.title}</Link><small className="table-secondary">{row.keys}Key · Lv. {row.level}</small></td><td><Link to={'/scores/' + row.scoreId}>{row.exScore.toLocaleString(locale)}</Link></td><td><Link to={'/scores/' + row.rivalScoreId}>{row.rivalExScore.toLocaleString(locale)}</Link></td><td>{row.difference > 0 ? '+' : ''}{row.difference.toLocaleString(locale)}</td><td>{t(lamps[row.lamp])} / {t(lamps[row.rivalLamp])}</td><td>{row.minMisses} / {row.rivalMinMisses}</td></tr>)}</tbody></table></div>{!rows.data?.length && <Empty>{t(rows.loading ? 'Loading scores…' : 'No common public records.')}</Empty>}<Pagination page={page} next={(rows.data?.length ?? 0) === 50} change={value => setPage(Number(value))} /></>}</section>;
}
export function Rankings() {
  const { chartId } = useParams();
  return chartId ? <ChartRanking key={chartId} id={chartId} /> : <ChartDirectory />;
}
export function IrAdministration() {
  const { t } = useI18n(); const [page, setPage] = useState(1); const [notice, setNotice] = useState('');
  const charts = useQuery<Chart[]>('/admin/charts?page=' + page);
  async function visibility(chart: Chart, value: string) {
    if (value === 'public' && !window.confirm(t('Confirm publication of chart metadata and community scores.'))) return;
    try { await send(`/charts/${chart.id}/visibility`, 'PUT', { visibility: value, confirmPublication: value === 'public' }); charts.refresh(); }
    catch (error) { setNotice((error as Error).message); }
  }
  return <section className="panel"><h2>{t('Chart visibility')}</h2><Alert message={notice || charts.error} /><div className="table-wrap"><table><thead><tr><th>{t('Title')}</th><th>{t('Visibility')}</th><th>{t('Actions')}</th></tr></thead><tbody>{charts.data?.map(chart => <tr key={chart.id}><td>{chart.visibility === 'hidden' ? chart.title : <Link to={'/rankings/' + chart.id}>{chart.title}</Link>}<small className="table-secondary">{chart.keys}Key · {chart.difficulty} · Lv. {chart.level}</small></td><td>{t(chart.visibility)}</td><td><div className="ir-directory-actions">{['public', 'unlisted', 'hidden'].map(value => <button key={value} disabled={chart.visibility === value} onClick={() => visibility(chart, value)}>{t(value)}</button>)}</div></td></tr>)}</tbody></table></div><Pagination page={page} next={(charts.data?.length ?? 0) === 50} change={value => setPage(Number(value))} /></section>;
}
function ChartDirectory() {
  const { t, locale } = useI18n();
  const { user } = useAuth();
  const { params, set, page } = useFilters();
  const query = new URLSearchParams({ q: params.get('q') ?? '', keys: params.get('keys') ?? '7', page: String(page), minimum: params.get('minimum') ?? '0', maximum: params.get('maximum') ?? '999', sort: params.get('sort') ?? 'newest', difficulty: params.get('difficulty') ?? '' });
  if (!query.get('keys') || query.get('keys') === 'all') query.delete('keys');
  const [debounced, setDebounced] = useState(query.toString());
  const search = query.toString();
  useEffect(() => { const timeout = setTimeout(() => setDebounced(search), 250); return () => clearTimeout(timeout); }, [search]);
  const charts = useQuery<Chart[]>('/charts?' + debounced);
  return <><Heading eyebrow="INTERNET RANKING" title={t('Charts')}>{t('Compare each player’s best EX SCORE for the same chart.')}</Heading>
    <div className="ir-directory-actions">{user && <Link className="button secondary" to="/rankings/mine">{t('My rankings')}</Link>}<Link to="/tables">{t('Difficulty tables')}</Link></div>
    <section className="panel"><div className="filter-bar ir-directory-filters"><label>{t('Search charts')}<input value={params.get('q') ?? ''} maxLength={100} placeholder={t('Search title or artist…')} onChange={e => set('q', e.target.value)} /></label>
      <label>{t('Key mode')}<select aria-label={t('Key mode')} value={params.get('keys') ?? '7'} onChange={e => set('keys', e.target.value)}><option value="all">{t('All key modes')}</option>{[5, 7, 9, 10, 14].map(key => <option key={key} value={key}>{key}Key</option>)}</select></label>
      <label>{t('Sort by')}<select aria-label={t('Sort by')} value={params.get('sort') ?? 'newest'} onChange={e => set('sort', e.target.value)}>{[['newest', 'Newest charts'], ['recent', 'Latest plays'], ['plays', 'Most played'], ['level-asc', 'Level: low to high'], ['level-desc', 'Level: high to low'], ['title', 'Title']].map(([value, label]) => <option key={value} value={value}>{t(label)}</option>)}</select></label>
      <label>{t('Difficulty')}<select aria-label={t('Difficulty')} value={params.get('difficulty') ?? ''} onChange={e => set('difficulty', e.target.value)}><option value="">{t('All difficulties')}</option>{['BEGINNER', 'NORMAL', 'HYPER', 'ANOTHER', 'INSANE', 'UNKNOWN'].map(value => <option key={value}>{value}</option>)}</select></label>
      <label>{t('Minimum level')}<input type="number" min={0} max={999} value={params.get('minimum') ?? ''} onChange={e => set('minimum', e.target.value)} /></label><label>{t('Maximum level')}<input type="number" min={0} max={999} value={params.get('maximum') ?? ''} onChange={e => set('maximum', e.target.value)} /></label></div>
      <Alert message={charts.error} />{charts.loading ? <Empty>{t('Loading charts…')}</Empty> : charts.data?.length ? <div className="ir-chart-grid">{charts.data.map(chart => <Link className="ir-chart-card" key={chart.id} to={'/rankings/' + chart.id}><span className="key-badge">{chart.keys}K</span><div><h2>{chart.title}</h2><p>{chart.artist}</p><small>{chart.difficulty || t('Unspecified difficulty')} · Lv. {chart.level}</small><small className="table-secondary">{t('Plays')}: {(chart.playCount ?? 0).toLocaleString(locale)} · {t('Players')}: {(chart.playerCount ?? 0).toLocaleString(locale)}</small>{chart.lastPlayedAt && <small className="table-secondary">{t('Latest play')}: {new Date(chart.lastPlayedAt).toLocaleString(locale)}</small>}</div><span className="badge">{t(chart.approved ? 'Curated' : 'Registered')}</span></Link>)}</div> : <Empty>{t('No matching charts.')}</Empty>}
      <Pagination page={page} next={(charts.data?.length ?? 0) === 50} change={value => set('page', value)} /></section></>;
}
function ChartRanking({ id }: { id: string }) {
  const { t, locale } = useI18n();
  const { user } = useAuth();
  const { params, set, page } = useFilters();
  const sort = params.get('sort') ?? 'score';
  const arrangement = params.get('arrangement') ?? 'all', gauge = params.get('gauge') ?? 'all', verified = params.get('verified') === 'true';
  const filters = new URLSearchParams({ arrangement, gauge, verified: String(verified) }).toString();
  const chart = useQuery<Chart>('/charts/' + id);
  const board = useQuery<Score[]>(chart.data ? `/rankings/${id}?${filters}&page=${page}&sort=${sort}` : null);
  const own = useQuery<Score>(chart.data && user ? `/rankings/${id}/me?${filters}` : null);
  const history = useQuery<Score[]>(chart.data && user ? `/rankings/${id}/history` : null);
  const [notice, setNotice] = useState('');
  if (!chart.data) return chart.loading ? <Empty>{t('Loading chart…')}</Empty> : <Alert message={chart.error} />;
  const info = chart.data;
  async function share() { try { await navigator.clipboard.writeText(location.href); setNotice('Link copied'); } catch { setNotice('Copy the page address to share this ranking.'); } }
  return <><div className="ir-directory-actions"><Link to="/rankings">← {t('Charts')}</Link>{user && <Link to="/rankings/mine">{t('My rankings')}</Link>}</div>
    <header className="ir-chart-header"><span className="eyebrow">INTERNET RANKING · {t(info.visibility ?? 'public')}</span><h1>{info.title}</h1><p>{info.artist}</p><div className="ir-chart-meta"><span className="key-badge">{info.keys}K</span><span>{info.difficulty || t('Unspecified difficulty')}</span><span>Lv. {info.level}</span>{!!info.bpm && <span>{info.bpm.toLocaleString(locale)} BPM</span>}{!!info.lengthMs && <span>{Math.floor(info.lengthMs / 60000)}:{String(Math.floor(info.lengthMs / 1000) % 60).padStart(2, '0')}</span>}<span className="badge">{t(info.approved ? 'Curated' : 'Registered')}</span></div><div className="ir-directory-actions"><button className="button small secondary" onClick={share}>{t('Copy ranking link')}</button>{info.packId && <Link to={'/packs/' + info.packId}>{t('Song pack')}</Link>}</div></header>
    <Alert message={notice} /><details className="chart-identity"><summary>{t('Chart identity')}</summary><code>MD5 {info.md5}</code><code>SHA-256 {info.sha256}</code></details>
    <section className="panel ranking-board"><div className="filter-bar"><label>{t('Sort by')}<select aria-label={t('Sort by')} value={sort} onChange={e => set('sort', e.target.value)}>{[['score', 'EX SCORE'], ['recent', 'Latest plays'], ['plays', 'Most played']].map(([value, label]) => <option key={value} value={value}>{t(label)}</option>)}</select></label><label>{t('Arrangement')}<select aria-label={t('Arrangement')} value={arrangement} onChange={e => set('arrangement', e.target.value)}><option value="all">{t('All arrangements')}</option>{['off', 'mirror', 'random', 's-random', 'scatter', 'converge'].map(value => <option key={value}>{value}</option>)}</select></label><label>{t('Gauge')}<select aria-label={t('Gauge')} value={gauge} onChange={e => set('gauge', e.target.value)}><option value="all">{t('All gauges')}</option>{['normal', 'hard', 'death', 'easy', 'p-attack', 'g-attack'].map(value => <option key={value}>{value}</option>)}</select></label><label>{t('Records')}<select aria-label={t('Records')} value={String(verified)} onChange={e => set('verified', e.target.value)}><option value="false">{t('All submissions')}</option><option value="true">{t('Verified only')}</option></select></label></div>
      {own.data && <div className="ir-own-position"><strong>{t('Your position')} #{own.data.rank}</strong><span>EX {own.data.exScore.toLocaleString(locale)}</span><button disabled={sort !== 'score'} onClick={() => set('page', String(Math.floor(((own.data!.position ?? own.data!.rank) - 1) / 50) + 1))}>{t('Show position')}</button><Link to={'/scores/' + own.data.id}>{t('Score details')}</Link></div>}
      <IrOverview chartId={id} arrangement={arrangement} gauge={gauge} verified={verified} /><Alert message={board.error || own.error} />
      {board.loading ? <Empty>{t('Loading scores…')}</Empty> : board.data?.length ? <IrScoreTable scores={board.data} currentUid={user?.uid} /> : <Empty>{t('No scores for these filters yet.')}</Empty>}
      <Pagination page={page} next={(board.data?.length ?? 0) === 50} change={value => set('page', value)} /><p className="ranking-note">{t('Submitted records are reported by clients. Verified records require server replay validation.')}</p>
    </section>
    {user && history.data?.length ? <section className="panel ir-history"><h2>{t('Your recent plays')}</h2><div className="score-list">{history.data.slice(0, 20).map(score => <Link key={score.id} to={'/scores/' + score.id}><span>{playTime(score, locale)}</span><strong>EX {score.exScore.toLocaleString(locale)}</strong><span className={'clear-lamp clear-' + score.clear}>{score.clear.toUpperCase()}</span></Link>)}</div></section> : null}
    {(info.ownerId === user?.id || user?.role === 'admin') && <BoardManagement chart={info} refresh={chart.refresh} />}</>;
}
export function ScorePage() {
  const { id } = useParams(); const { t, locale } = useI18n(); const { user } = useAuth();
  const record = useQuery<Score>('/scores/' + id); const [notice, setNotice] = useState('');
  if (!record.data) return record.loading ? <Empty>{t('Loading scores…')}</Empty> : <Alert message={record.error} />;
  const score = record.data;
  const grade = score.scoreMax ? ['F', 'F', 'E', 'D', 'C', 'B', 'A', 'AA', 'AAA'][Math.min(8, Math.floor(score.exScore * 9 / score.scoreMax))] : '—';
  async function moderate(e: FormEvent<HTMLFormElement>) { e.preventDefault(); const data = new FormData(e.currentTarget); try { await send(`/admin/scores/${id}/review`, 'PUT', { withdrawn: true, reason: data.get('reason') }); setNotice('Score withdrawn'); record.refresh(); } catch (error) { setNotice((error as Error).message); } }
  return <><Link to={'/rankings/' + score.chartId}>← {t('Internet Ranking')}</Link><Heading eyebrow={t('Score details')} title={score.title ?? ''} />
    <section className="panel ir-score-page"><div className="ir-score-hero"><Avatar user={{ displayName: score.displayName, avatarUrl: score.avatarUrl ?? null }} large /><div><Link to={'/players/id/' + score.uid}>{score.displayName}</Link><h2>{score.exScore.toLocaleString(locale)} <small>EX SCORE</small> · {grade}</h2><span className={'clear-lamp clear-' + score.clear}>{score.clear.toUpperCase()}</span> <span className="badge">{t(score.verified ? 'Verified' : 'Submitted')}</span></div></div>
      <dl className="ir-judgements">{(['perfect', 'great', 'good', 'bad', 'poor'] as const).map((key, index) => <div key={key}><dt>{['PGREAT', 'GREAT', 'GOOD', 'BAD', 'POOR'][index]}</dt><dd>{score[key].toLocaleString(locale)}</dd></div>)}</dl>
      <dl className="ir-score-details"><div><dt>SCORE</dt><dd>{score.normalScore?.toLocaleString(locale) ?? '—'}</dd></div><div><dt>{t('Rate')}</dt><dd>{score.scoreMax ? (score.exScore * 100 / score.scoreMax).toFixed(2) + '%' : '—'}</dd></div><div><dt>COMBO</dt><dd>{score.maxCombo.toLocaleString(locale)}</dd></div><div><dt>BP</dt><dd>{score.bad + score.poor}</dd></div><div><dt>{t('Options')}</dt><dd>{score.arrangement.toUpperCase()} · {score.gauge.toUpperCase()}</dd></div><div><dt>{t('Input')}</dt><dd>{t(score.inputType)}</dd></div><div><dt>{t('Played at')}</dt><dd>{playTime(score, locale)}</dd></div><div><dt>{t('Uploaded at')}</dt><dd>{new Date(score.createdAt).toLocaleString(locale)}</dd></div><div><dt>{t('Ruleset')}</dt><dd>{score.ruleset}</dd></div></dl>
      {score.comment && <p>{score.comment}</p>}<p className="ranking-note">{t('Submitted records are reported by clients. Verified records require server replay validation.')}</p></section>
    {user?.role === 'admin' && <form className="panel ir-history" onSubmit={moderate}><h2>{t('Moderation')}</h2><label>{t('Reason')}<input name="reason" required maxLength={400} /></label><button className="button secondary">{t('Withdraw score')}</button><Alert message={notice} /></form>}</>;
}
export function MyRankings() {
  const { user } = useAuth(); const { t } = useI18n(); const { set, page } = useFilters(); const navigate = useNavigate();
  const charts = useQuery<Chart[]>(user ? '/charts?mine=true&page=' + page : null); const [notice, setNotice] = useState(''); const [busy, setBusy] = useState(false);
  async function register(e: FormEvent<HTMLFormElement>) { e.preventDefault(); const data = new FormData(e.currentTarget); setBusy(true); setNotice(''); try { const chart = await send<Chart>('/charts/register', 'POST', { sha256: data.get('sha256'), md5: data.get('md5'), title: data.get('title'), artist: data.get('artist'), difficulty: data.get('difficulty'), keys: Number(data.get('keys')), level: Number(data.get('level')), visibility: data.get('visibility') }); charts.refresh(); navigate('/rankings/' + chart.id); } catch (error) { setNotice((error as Error).message); } finally { setBusy(false); } }
  if (!user) return <SignInPrompt />;
  return <><Heading eyebrow="INTERNET RANKING" title={t('My rankings')} /><Alert message={charts.error || notice} /><section className="panel"><div className="chart-list">{charts.data?.map(chart => <Link key={chart.id} to={'/rankings/' + chart.id}><span className="key-badge">{chart.keys}K</span><span><strong>{chart.title}</strong><small>{chart.difficulty} · Lv. {chart.level}</small></span><span className="badge">{t(chart.visibility)}</span></Link>)}</div><Pagination page={page} next={(charts.data?.length ?? 0) === 50} change={value => set('page', value)} /></section>
    <form className="panel ir-registration" onSubmit={register}><h2>{t('Register a chart')}</h2><p className="muted">{t('Only chart identity and metadata are registered. Audio, videos and ZIP files are not uploaded.')}</p><div className="ir-registration-grid">{[['title', 'Title', 200], ['artist', 'Artist', 200], ['difficulty', 'Difficulty', 120], ['sha256', 'SHA-256', 64], ['md5', 'MD5', 32]].map(([name, label, max]) => <label key={name}>{t(String(label))}<input name={String(name)} required={name === 'title' || name === 'sha256' || name === 'md5'} maxLength={Number(max)} pattern={name === 'sha256' ? '[0-9a-fA-F]{64}' : name === 'md5' ? '[0-9a-fA-F]{32}' : undefined} /></label>)}<label>{t('Key mode')}<select name="keys" aria-label={t('Key mode')}>{[7, 5, 9, 10, 14].map(key => <option key={key}>{key}</option>)}</select></label><label>{t('Level')}<input name="level" type="number" required min={0} max={999} defaultValue={0} /></label><label>{t('Visibility')}<select aria-label={t('Visibility')} name="visibility" defaultValue="public"><option value="public">{t('public')}</option><option value="unlisted">{t('unlisted')}</option><option value="restricted">{t('restricted')}</option></select></label></div><p className="ranking-note">{t('Public rankings appear automatically. Private rankings require member access.')}</p><button className="button primary" disabled={busy}>{t('Register a chart')}</button></form></>;
}
function BoardManagement({ chart, refresh }: { chart: Chart; refresh: () => void }) {
  const { t } = useI18n(); const { user } = useAuth(); const [notice, setNotice] = useState('');
  const members = useQuery<{ uid: number; displayName: string }[]>(chart.visibility === 'restricted' ? `/charts/${chart.id}/members` : null);
  async function member(e: FormEvent<HTMLFormElement>) { e.preventDefault(); const data = new FormData(e.currentTarget); try { await send(`/charts/${chart.id}/members`, 'POST', { uid: Number(data.get('uid')) }); members.refresh(); setNotice('Member added'); } catch (error) { setNotice((error as Error).message); } }
  async function remove(uid: number) { try { await api(`/charts/${chart.id}/members/${uid}`, { method: 'DELETE' }); members.refresh(); } catch (error) { setNotice((error as Error).message); } }
  async function visibility(e: FormEvent<HTMLFormElement>) { e.preventDefault(); const data = new FormData(e.currentTarget); try { await send(`/charts/${chart.id}/visibility`, 'PUT', { visibility: data.get('visibility'), confirmPublication: data.get('confirm') === 'on' }); refresh(); } catch (error) { setNotice((error as Error).message); } }
  return <details className="panel ir-history"><summary>{t('Ranking access')}</summary><Alert message={notice || members.error} />{chart.visibility === 'restricted' ? <><form className="filter-bar" onSubmit={member}><label>UID<input name="uid" type="number" required min={1} max={9007199254740991} /></label><button className="button secondary">{t('Add member')}</button></form><div className="admin-list">{members.data?.map(member => <div key={member.uid}><span>{member.displayName} · UID {member.uid}</span><button onClick={() => remove(member.uid)}>{t('Remove')}</button></div>)}</div><p>{t('Private rankings stay private. Create a separate community ranking to publish scores.')}</p></> : (chart.visibility === 'unlisted' || user?.role === 'admin') && <form onSubmit={visibility}><label>{t('Visibility')}<select aria-label={t('Visibility')} name="visibility"><option value="public">{t('public')}</option>{user?.role === 'admin' && <><option value="unlisted">{t('unlisted')}</option><option value="hidden">{t('Hidden')}</option></>}</select></label><label className="ir-consent"><input type="checkbox" name="confirm" />{t('Confirm publication of chart metadata and community scores.')}</label><button className="button secondary">{t('Save changes')}</button></form>}</details>;
}
export function PlayerRecords({ uid }: { uid: number }) {
  const { t, locale } = useI18n(); const [page, setPage] = useState(1); const [recent, setRecent] = useState(false);
  const records = useQuery<Score[]>(`/players/${uid}/records?page=${page}&recent=${recent}`);
  return <section className="panel ir-history"><div className="section-title"><h2>{t('Play records')}</h2><div className="tabs"><button className={!recent ? 'active' : ''} onClick={() => { setRecent(false); setPage(1); }}>{t('Personal bests')}</button><button className={recent ? 'active' : ''} onClick={() => { setRecent(true); setPage(1); }}>{t('Recent scores')}</button></div></div><Alert message={records.error} />{records.data?.length ? <div className="table-wrap"><table><thead><tr><th>{t('Title')}</th><th>EX SCORE</th><th>{t('Best clear')}</th><th>{t('BP / min BP')}</th><th>{t('Played at')}</th></tr></thead><tbody>{records.data.map(score => <tr key={score.id}><td><Link to={'/rankings/' + score.chartId}>{score.title}</Link><small className="table-secondary">{score.keys}Key · Lv. {score.level} · {t(score.visibility ?? 'public')}</small></td><td><Link to={'/scores/' + score.id}>{score.exScore.toLocaleString(locale)}</Link></td><td><span className={'clear-lamp clear-' + score.bestClear}>{score.bestClear.toUpperCase()}</span></td><td>{score.misses} / {score.minMisses}</td><td>{playTime(score, locale)}</td></tr>)}</tbody></table></div> : <Empty>{t(records.loading ? 'Loading scores…' : 'No scores yet.')}</Empty>}<Pagination page={page} next={(records.data?.length ?? 0) === 50} change={value => setPage(Number(value))} /></section>;
}
