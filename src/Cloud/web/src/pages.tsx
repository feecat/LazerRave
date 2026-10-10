import { useI18n } from './i18n';
import { useEffect, useState, type FormEvent } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { api, date, send, size } from './api';
import { Alert, Avatar, Empty, Heading, SignInPrompt, useQuery } from './components';
import { useAuth } from './state';
import type { Chart, Pack, Score, User } from './types';
import { DifficultyTableAdmin } from './tables';
import { IrOverview, IrScoreTable } from './ir';
import { RhythmVisual } from './RhythmVisual';
import { Icon } from './Icon';

export function Home() {
  const { t } = useI18n();
  const { data: packs, error } = useQuery<Pack[]>('/packs');
  return <>
    <section className="hero">
      <div className="hero-content">
        <span className="eyebrow">{t("THE NEXT CHART IS WAITING")}</span>
        <h1>{t("Find your rhythm.")}<br /><em>{t("Raise the bar.")}</em></h1>
        <p>{t("Your home for BMS. Discover song packs and chase your next EX SCORE.")}</p>
        <div className="hero-actions"><Link className="button primary" to="/download"><Icon name="download" />{t("Download LazerRave")}</Link><Link className="button secondary" to="/packs">{t("Explore song packs ")}<span>↗</span></Link></div>
      </div>
      <RhythmVisual />
    </section>
    <section className="feature-grid"><Link to="/rankings" className="feature"><span className="feature-symbol">01</span><h2>{t("Every point counts")}</h2><p>{t("Chart rankings, clear records, and your next personal best.")}</p><span>{t("See the rankings ↗")}</span></Link><Link to="/packs" className="feature"><span className="feature-symbol">02</span><h2>{t("A new discovery")}</h2><p>{t("Browse the catalog and download a pack for your next session.")}</p><span>{t("Browse song packs ↗")}</span></Link><Link to="/tables" className="feature"><span className="feature-symbol">03</span><h2>{t("Find your next challenge")}</h2><p>{t("Browse charts by difficulty level.")}</p><span>{t("Explore difficulty tables ↗")}</span></Link></section>
    <section><div className="section-title"><div><span className="eyebrow">{t("THE CATALOG")}</span><h2>{t("Recently added")}</h2></div><Link to="/packs">{t("View all packs →")}</Link></div><Alert message={error} />{packs?.length ? <div className="pack-grid">{packs.slice(0, 3).map(pack => <PackCard key={pack.id} pack={pack} />)}</div> : <Empty>{t("Song packs will appear here when published.")}</Empty>}</section>
  </>;
}

export function Authentication({ register = false }: { register?: boolean }) {
  const { t } = useI18n();
  const { user, setUser } = useAuth(); const navigate = useNavigate();
  const [error, setError] = useState(''); const [busy, setBusy] = useState(false);
  if (user) return <Empty>{t("You are signed in as ")}{user.displayName}. <Link to={'/players/' + user.username}>{t("Open your profile")}</Link></Empty>;
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); const form = new FormData(event.currentTarget); setBusy(true); setError('');
    try {
      const next = await send<User>('/auth/' + (register ? 'register' : 'login'), 'POST', { username: form.get('username'), password: form.get('password'), ...(register ? { email: form.get('email') } : {}) });
      setUser(next); navigate('/players/' + next.username);
    } catch (e) { setError((e as Error).message); } finally { setBusy(false); }
  }
  return <div className="auth-layout"><div className="auth-intro"><img src="/logo.svg" alt="LazerRave" /><span className="eyebrow">{t("LAZERRAVE COMMUNITY")}</span><h1>{register ? t('Your next chapter.') : t('Welcome back.')}</h1><p>{register ? t('Make a name. Find a challenge. Meet your next rivals.') : t('The next personal best is closer than you think.')}</p></div><form className="panel auth-form" onSubmit={submit}><h2>{register ? t('Create an account') : t('Sign in')}</h2><Alert message={error} /><label>{register ? t('Username') : t('Username or email')}<input required name="username" minLength={register ? 3 : 1} maxLength={register ? 24 : 254} pattern={register ? '[A-Za-z0-9_]+' : undefined} autoComplete="username" /></label>{register && <label>{t("Email")}<input required type="email" name="email" maxLength={254} autoComplete="email" /></label>}<label>{t("Password")}<input required type="password" name="password" minLength={register ? 12 : 1} maxLength={128} autoComplete={register ? 'new-password' : 'current-password'} /></label>{register && <small>{t("Use 12–128 characters. Usernames support letters, numbers and underscores.")}</small>}<button className="button primary" disabled={busy}>{busy ? t('Please wait…') : register ? t('Create account') : t('Sign in')}</button><p className="muted">{register ? t('Already have an account? ') : t('New to LazerRave? ')}<Link to={register ? '/login' : '/register'}>{register ? t('Sign in') : t('Join the community')}</Link></p></form></div>;
}

export function ChangePassword() {
  const { t } = useI18n();
  const { user, setUser } = useAuth();
  const [error, setError] = useState(''); const [busy, setBusy] = useState(false); const [changed, setChanged] = useState(false);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); const form = event.currentTarget; const values = new FormData(form); setError('');
    if (values.get('newPassword') !== values.get('confirmPassword')) { setError('New passwords do not match.'); return; }
    setBusy(true);
    try {
      await send('/auth/password', 'POST', { currentPassword: values.get('currentPassword'), newPassword: values.get('newPassword') });
      form.reset(); setChanged(true); setUser(null);
    } catch (e) { setError((e as Error).message); } finally { setBusy(false); }
  }
  if (changed) return <section className="panel"><h1>{t("Password changed")}</h1><p>{t("Your previous sessions have been signed out.")}</p><Link className="button primary" to="/login">{t("Sign in with your new password")}</Link></section>;
  if (!user) return <SignInPrompt />;
  return <><Heading eyebrow={t("ACCOUNT SECURITY")} title={t("Change password")} /><form className="panel auth-form" onSubmit={submit}><Alert message={error} /><input type="hidden" name="username" value={user.username} autoComplete="username" /><label>{t("Current password")}<input required name="currentPassword" type="password" maxLength={128} autoComplete="current-password" /></label><label>{t("New password")}<input required name="newPassword" type="password" minLength={12} maxLength={128} autoComplete="new-password" /></label><label>{t("Confirm new password")}<input required name="confirmPassword" type="password" minLength={12} maxLength={128} autoComplete="new-password" /></label><small>{t("Use 12–128 characters. Changing your password signs out all existing website and desktop sessions.")}</small><button className="button primary" disabled={busy}>{busy ? t('Saving…') : t('Update password')}</button></form></>;
}

export function Profile() {
  const { t, locale } = useI18n();
  const { username, uid } = useParams(); const { user: viewer, setUser } = useAuth();
  const { data, error, loading, refresh } = useQuery<{ user: User; scores: Score[] }>(uid ? '/players/' + encodeURIComponent(uid) : '/users/' + encodeURIComponent(username ?? ''));
  const [editing, setEditing] = useState(false); const [notice, setNotice] = useState(''); const [busy, setBusy] = useState(false);
  useEffect(() => { setEditing(false); setNotice(''); }, [username, uid]);
  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); const form = new FormData(event.currentTarget); setBusy(true); setNotice('');
    try {
      await send<User>('/me', 'PUT', { displayName: form.get('displayName'), signature: form.get('signature'), bio: form.get('bio') });
      const avatar = form.get('avatar') as File;
      if (avatar.size) { const upload = new FormData(); upload.set('file', avatar); await api('/me/avatar', { method: 'POST', body: upload }); }
      setUser(await api<User>('/me')); setEditing(false); refresh();
    } catch (e) { setNotice((e as Error).message); } finally { setBusy(false); }
  }
  if (loading) return <Empty>{t("Loading player…")}</Empty>;
  if (!data) return <Alert message={error} />;
  const { user, scores } = data;
  return <><div className="profile-banner"><span className="eyebrow">{t("PLAYER PROFILE")}</span><div className="profile-identity"><Avatar user={user} large /><div><h1>{user.displayName}</h1><span className="muted">@{user.username} · UID <Link to={'/players/id/' + user.uid}>{user.uid}</Link> {t(" · Joined ")}{date(user.createdAt, locale)}</span><p>{user.signature || '—'}</p></div>{viewer?.id === user.id && <div className="profile-edit"><button className="button secondary" onClick={() => setEditing(value => !value)}>{t("Edit profile")}</button> <Link className="button secondary" to="/account/password">{t("Change password")}</Link></div>}</div></div><Alert message={notice} />{editing && <form className="panel profile-form" onSubmit={save}><label>{t("Display name")}<input name="displayName" required maxLength={40} defaultValue={user.displayName} /></label><label>{t("Signature")}<input name="signature" maxLength={200} defaultValue={user.signature} /></label><label>{t("About you")}<textarea name="bio" maxLength={2000} defaultValue={user.bio} rows={5} /></label><label>{t("Avatar · PNG or JPEG, up to 1 MiB")}<input name="avatar" type="file" accept="image/png,image/jpeg" /></label><button disabled={busy} className="button primary">{busy ? t('Saving…') : t('Save changes')}</button></form>}<div className="profile-columns"><section className="panel"><span className="eyebrow">{t("ABOUT")}</span><p className="biography">{user.bio || t('This player has not added a biography.')}</p></section><section className="panel"><div className="section-title"><h2>{t("Recent scores")}</h2><span className="badge">{scores.length}</span></div>{scores.length ? <div className="score-list">{scores.map(score => <Link key={score.id} to={'/rankings/' + score.chartId}><span><strong>{score.title}</strong><small>{score.clear} · {date(score.createdAt, locale)}</small></span><strong className="score-number">{score.exScore.toLocaleString(locale)}</strong><span className="badge">{score.verified ? t('Verified') : t('Submitted')}</span></Link>)}</div> : <Empty>{t("No scores yet.")}</Empty>}</section></div></>;
}

function PackCard({ pack }: { pack: Pack }) {
  const { t, locale } = useI18n();
  return <Link className="pack-card" to={'/packs/' + pack.id}><div className="pack-art"><span>LR</span><i /></div><div className="pack-card-content"><span className="badge">{t("BMS COLLECTION")}</span><h3>{pack.title}</h3><p>{pack.description || t('A collection for your next session.')}</p><div className="card-meta"><span>{size(pack.sizeBytes)}</span><span>{date(pack.createdAt, locale)}</span></div></div></Link>;
}
export function Packs() {
  const { t } = useI18n();
  const { data, error, loading } = useQuery<Pack[]>('/packs'); const [query, setQuery] = useState('');
  const filtered = data?.filter(pack => (pack.title + ' ' + pack.description).toLowerCase().includes(query.toLowerCase())) ?? [];
  return <><Heading eyebrow={t("SONG PACKS")} title={t("Find your next favorite")}>{t("A curated collection of charts, ready for your library.")}</Heading><div className="filter-bar"><input aria-label={t("Search song packs")} placeholder={t("Search song packs…")} value={query} onChange={event => setQuery(event.target.value)} /><span>{filtered.length} {t(" packs")}</span></div><Alert message={error} />{loading ? <Empty>{t("Loading packs…")}</Empty> : filtered.length ? <div className="pack-grid">{filtered.map(pack => <PackCard key={pack.id} pack={pack} />)}</div> : <Empty>{t("No song packs found.")}</Empty>}</>;
}
export function PackDetail() {
  const { t, locale } = useI18n();
  const { id } = useParams(); const { data, error, loading } = useQuery<{ pack: Pack; charts: Chart[] }>('/packs/' + id);
  if (loading) return <Empty>{t("Loading pack…")}</Empty>;
  if (!data) return <Alert message={error} />;
  return <><Heading eyebrow={t("SONG PACK")} title={data.pack.title}>{data.pack.description}</Heading><div className="pack-detail-meta"><span className="badge">{data.charts.length} {t(" charts")}</span><span>{size(data.pack.sizeBytes)} · {date(data.pack.createdAt, locale)}</span><a className="button primary" href={'/api/packs/' + id + '/download'}>{t("Download ZIP ↓")}</a></div><section className="panel"><h2>{t("Included charts")}</h2><div className="chart-list">{data.charts.map((chart, index) => <Link key={chart.id + index} to={'/rankings/' + chart.id}><span className="key-badge">{chart.keys}K</span><span><strong>{chart.title}</strong><small>{chart.artist} · {chart.difficulty || t('Unspecified difficulty')}</small></span><span className="level">Lv. {chart.level}</span><span>{t("Ranking →")}</span></Link>)}</div><details className="checksum"><summary>{t("Archive checksum")}</summary><code>{data.pack.sha256}</code></details></section></>;
}

export function Rankings() {
  const { t } = useI18n();
  const { chartId } = useParams(); const [query, setQuery] = useState(''); const [search, setSearch] = useState(''); const [keys, setKeys] = useState('7'); const [page, setPage] = useState(1); const [boardPage, setBoardPage] = useState(1);
  const [arrangement, setArrangement] = useState('all'); const [gauge, setGauge] = useState('all'); const [verified, setVerified] = useState(false);
  useEffect(() => { const timeout = setTimeout(() => { setSearch(query); setPage(1); }, 250); return () => clearTimeout(timeout); }, [query]);
  useEffect(() => { setBoardPage(1); }, [chartId, arrangement, gauge, verified]);
  const charts = useQuery<Chart[]>('/charts?' + new URLSearchParams({ q: search, ...(keys ? { keys } : {}), page: String(page) }));
  const selected = useQuery<Chart | null>(chartId ? '/charts/' + chartId : '/charts/00000000-0000-0000-0000-000000000000');
  const board = useQuery<Score[]>(chartId ? `/rankings/${chartId}?arrangement=${arrangement}&gauge=${gauge}&verified=${verified}&page=${boardPage}` : '/rankings/00000000-0000-0000-0000-000000000000');
  return <><Heading eyebrow={t("INTERNET RANKING")} title={t("One chart. A higher standard.")}>{t("Compare each player’s best EX SCORE for the same chart.")}</Heading><div className="ranking-layout"><aside className="panel chart-picker"><input aria-label={t("Search charts")} placeholder={t("Search title or artist…")} value={query} onChange={e => setQuery(e.target.value)} /><select aria-label={t("Key mode")} value={keys} onChange={e => { setKeys(e.target.value); setPage(1); }}><option value="">{t("All key modes")}</option>{[5, 7, 9, 10, 14].map(key => <option key={key} value={key}>{key}Key</option>)}</select><Alert message={charts.error} />{charts.data?.map(chart => <Link className={'chart-choice' + (chart.id === chartId ? ' selected' : '')} key={chart.id} to={'/rankings/' + chart.id}><span className="key-badge">{chart.keys}K</span><span><strong>{chart.title}</strong><small>{chart.artist} · Lv. {chart.level} · {chart.difficulty}</small></span></Link>)}{!charts.loading && !charts.data?.length && <Empty>{t("No matching charts.")}</Empty>}<div className="pagination"><button disabled={page === 1} onClick={() => setPage(value => value - 1)}>←</button><span>{page}</span><button disabled={(charts.data?.length ?? 0) < 50} onClick={() => setPage(value => value + 1)}>→</button></div></aside><section className="panel ranking-board"><h2>{selected.data?.title || t('Select a chart')}</h2>{selected.data && <p className="muted">{selected.data.artist} · {selected.data.keys}Key · Lv. {selected.data.level}</p>}<div className="filter-bar"><label>{t("Arrangement")}<select aria-label={t("Arrangement")} value={arrangement} onChange={e => setArrangement(e.target.value)}><option value="all">{t('All arrangements')}</option>{['off', 'mirror', 'random', 's-random', 'scatter', 'converge'].map(value => <option key={value} value={value}>{value.toUpperCase()}</option>)}</select></label><label>{t("Gauge")}<select aria-label={t("Gauge")} value={gauge} onChange={e => setGauge(e.target.value)}><option value="all">{t('All gauges')}</option>{['normal', 'hard', 'death', 'easy', 'p-attack', 'g-attack'].map(value => <option key={value} value={value}>{value.toUpperCase()}</option>)}</select></label><label>{t("Records")}<select aria-label={t("Records")} value={String(verified)} onChange={e => setVerified(e.target.value === 'true')}><option value="false">{t("All submissions")}</option><option value="true">{t("Verified only")}</option></select></label></div>{selected.data && <details className="chart-identity"><summary>{t("Chart identity")}</summary><code>MD5 {selected.data.md5}</code><code>SHA-256 {selected.data.sha256}</code></details>}{chartId && <IrOverview chartId={chartId} arrangement={arrangement} gauge={gauge} verified={verified} />}{chartId && <Alert message={board.error} />}{chartId && board.data?.length ? <><IrScoreTable scores={board.data} /><div className="pagination"><button disabled={boardPage === 1} onClick={() => setBoardPage(value => value - 1)}>{t("Previous")}</button><span>{boardPage}</span><button disabled={board.data.length < 50} onClick={() => setBoardPage(value => value + 1)}>{t("Next")}</button></div></> : <Empty>{chartId ? t('No scores for these filters yet.') : t('Choose a chart to view its leaderboard.')}</Empty>}<p className="ranking-note">{t("Submitted records are reported by clients. Verified records require server replay validation.")}</p></section></div></>;
}

export function Admin() {
  const { t } = useI18n();
  const { user } = useAuth();
  return user?.role === 'admin' ? <AdminPanel /> : user ? <Empty>{t("Administrator access is required.")}</Empty> : <SignInPrompt />;
}
function AdminPanel() {
  const { t, locale } = useI18n();
  const { user } = useAuth();
  const [tab, setTab] = useState('packs'); const [error, setError] = useState(''); const [busy, setBusy] = useState(false);
  const packs = useQuery<Pack[]>('/admin/packs'); const users = useQuery<(User & { disabled: boolean })[]>('/admin/users');
  const audit = useQuery<{ id: number; username: string; action: string; target: string; createdAt: string }[]>('/admin/audit');
  const overview = useQuery<Record<string, number>>('/admin/overview');
  async function upload(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); const form = event.currentTarget; setBusy(true); setError('');
    try { await api('/admin/packs', { method: 'POST', body: new FormData(form) }); form.reset(); packs.refresh(); overview.refresh(); audit.refresh(); }
    catch (e) { setError((e as Error).message); } finally { setBusy(false); }
  }
  async function publish(pack: Pack) {
    setError(''); try { await send('/admin/packs/' + pack.id + '/publication', 'PUT', { published: !pack.published }); packs.refresh(); audit.refresh(); } catch (e) { setError((e as Error).message); }
  }
  async function disable(player: User & { disabled: boolean }) {
    setError(''); try { await send('/admin/users/' + player.id + '/disabled', 'PUT', { disabled: !player.disabled }); users.refresh(); audit.refresh(); } catch (e) { setError((e as Error).message); }
  }
  return <><Heading eyebrow={t("ADMINISTRATION")} title={t("Community management")} /><div className="stats-grid">{Object.entries(overview.data ?? {}).map(([key, value]) => <div className="panel" key={key}><span className="eyebrow">{t(key)}</span><strong>{value.toLocaleString(locale)}</strong></div>)}</div><div className="tabs" role="tablist">{['packs', 'tables', 'users', 'audit'].map(value => <button role="tab" aria-selected={value === tab} className={value === tab ? 'active' : ''} key={value} onClick={() => setTab(value)}>{value === 'packs' ? t('Song packs') : value === 'tables' ? t('Difficulty tables') : value === 'users' ? t('Players') : t('Activity log')}</button>)}</div><Alert message={error || packs.error || users.error || audit.error || overview.error} />{tab === 'packs' && <div className="admin-columns"><form className="panel" onSubmit={upload}><h2>{t("Upload a song pack")}</h2><label>{t("Title")}<input required name="title" maxLength={120} /></label><label>{t("Description")}<textarea name="description" maxLength={2000} rows={4} /></label><label>{t("ZIP archive")}<input required name="file" type="file" accept=".zip,application/zip" /></label><small>{t("Up to 128 MiB. New packs remain drafts until published.")}</small><button className="button primary" disabled={busy}>{busy ? t('Uploading and checking…') : t('Upload pack')}</button></form><section className="panel"><h2>{t("Catalog")}</h2>{packs.data?.length ? <div className="admin-list">{packs.data.map(pack => <div key={pack.id}><span><Link to={'/packs/' + pack.id}><strong>{pack.title}</strong></Link><small>{size(pack.sizeBytes)} · {pack.published ? t('Published') : t('Draft')}</small></span><button className="button small secondary" onClick={() => publish(pack)}>{pack.published ? t('Unpublish') : t('Publish')}</button></div>)}</div> : <Empty>{t("No uploaded packs.")}</Empty>}</section></div>}{tab === 'tables' && <DifficultyTableAdmin />}{tab === 'users' && <section className="panel"><div className="table-wrap"><table><thead><tr><th>UID</th><th>{t("Player")}</th><th>{t("Role")}</th><th>{t("Status")}</th><th>{t("Actions")}</th></tr></thead><tbody>{users.data?.map(player => <tr key={player.id}><td>{player.uid}</td><td><Link to={'/players/' + player.username}>{player.displayName}</Link></td><td>{t(player.role)}</td><td>{player.disabled ? t('Disabled') : t('Active')}</td><td><button className="button small secondary" disabled={player.id === user?.id} onClick={() => disable(player)}>{player.disabled ? t('Enable') : t('Disable')}</button></td></tr>)}</tbody></table></div></section>}{tab === 'audit' && <section className="panel"><div className="table-wrap"><table><thead><tr><th>{t("Time")}</th><th>{t("Administrator")}</th><th>{t("Action")}</th><th>{t("Target")}</th></tr></thead><tbody>{audit.data?.map(row => <tr key={row.id}><td>{date(row.createdAt, locale)}</td><td>{row.username}</td><td>{row.action}</td><td className="mono">{row.target}</td></tr>)}</tbody></table></div></section>}</>;
}
