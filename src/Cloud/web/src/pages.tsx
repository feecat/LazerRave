import { useI18n } from './i18n';
import { useEffect, useState, type FormEvent } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { api, date, send, size } from './api';
import { Alert, Avatar, Empty, Heading, SignInPrompt, useQuery } from './components';
import { useAuth, useLive } from './state';
import type { Chart, ChatMessage, Pack, Room, Score, User } from './types';
import { DifficultyTableAdmin } from './tables';
import { IrScoreTable } from './ir';

export function Home() {
  const { t } = useI18n();
  const { data: packs, error } = useQuery<Pack[]>('/packs');
  return <>
    <section className="hero">
      <div className="hero-content">
        <span className="eyebrow">{t("THE NEXT CHART IS WAITING")}</span>
        <h1>{t("Find your rhythm.")}<br /><em>{t("Raise the bar.")}</em></h1>
        <p>{t("Your home for BMS. Discover song packs, chase an EX SCORE, and bring the next session together.")}</p>
        <div className="hero-actions"><Link className="button primary" to="/packs">{t("Explore song packs ")}<span>↗</span></Link><Link className="button secondary" to="/rankings">{t("Internet Ranking")}</Link></div>
      </div>
      <div className="rhythm-art" aria-hidden="true">
        <div className="art-halo" />
        <div className="art-orbit" /><div className="art-orbit second" />
        <div className="art-disc"><img src="/logo.svg" alt="" /></div>
        <div className="art-note a" /><div className="art-note b" /><div className="art-note c" />
        <div className="art-equalizer"><i /><i /><i /><i /><i /><i /><i /></div>
        <span className="art-caption">BMS / 5K · 7K · 9K · DP</span>
      </div>
    </section>
    <section className="feature-grid"><Link to="/rankings" className="feature"><span className="feature-symbol">01</span><h2>{t("Every point counts")}</h2><p>{t("Chart rankings, clear records, and your next personal best.")}</p><span>{t("See the rankings ↗")}</span></Link><Link to="/packs" className="feature"><span className="feature-symbol">02</span><h2>{t("A new discovery")}</h2><p>{t("Browse the catalog and download a pack for your next session.")}</p><span>{t("Browse song packs ↗")}</span></Link><Link to="/multiplayer" className="feature"><span className="feature-symbol">03</span><h2>{t("Play together")}</h2><p>{t("Up to 16 players. One chart. A shared challenge.")}</p><span>{t("Find a room ↗")}</span></Link></section>
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
  const [arrangement, setArrangement] = useState('off'); const [gauge, setGauge] = useState('normal'); const [verified, setVerified] = useState(false);
  useEffect(() => { const timeout = setTimeout(() => { setSearch(query); setPage(1); }, 250); return () => clearTimeout(timeout); }, [query]);
  useEffect(() => { setBoardPage(1); }, [chartId, arrangement, gauge, verified]);
  const charts = useQuery<Chart[]>('/charts?' + new URLSearchParams({ q: search, ...(keys ? { keys } : {}), page: String(page) }));
  const selected = useQuery<Chart | null>(chartId ? '/charts/' + chartId : '/charts/00000000-0000-0000-0000-000000000000');
  const board = useQuery<Score[]>(chartId ? `/rankings/${chartId}?arrangement=${arrangement}&gauge=${gauge}&verified=${verified}&page=${boardPage}` : '/rankings/00000000-0000-0000-0000-000000000000');
  return <><Heading eyebrow={t("INTERNET RANKING")} title={t("One chart. A higher standard.")}>{t("Compare EX SCORE records for the same chart, arrangement and gauge.")}</Heading><div className="ranking-layout"><aside className="panel chart-picker"><input aria-label={t("Search charts")} placeholder={t("Search title or artist…")} value={query} onChange={e => setQuery(e.target.value)} /><select aria-label={t("Key mode")} value={keys} onChange={e => { setKeys(e.target.value); setPage(1); }}><option value="">{t("All key modes")}</option>{[5, 7, 9, 10, 14].map(key => <option key={key} value={key}>{key}Key</option>)}</select><Alert message={charts.error} />{charts.data?.map(chart => <Link className={'chart-choice' + (chart.id === chartId ? ' selected' : '')} key={chart.id} to={'/rankings/' + chart.id}><span className="key-badge">{chart.keys}K</span><span><strong>{chart.title}</strong><small>{chart.artist} · Lv. {chart.level} · {chart.difficulty}</small></span></Link>)}{!charts.loading && !charts.data?.length && <Empty>{t("No matching charts.")}</Empty>}<div className="pagination"><button disabled={page === 1} onClick={() => setPage(value => value - 1)}>←</button><span>{page}</span><button disabled={(charts.data?.length ?? 0) < 50} onClick={() => setPage(value => value + 1)}>→</button></div></aside><section className="panel ranking-board"><h2>{selected.data?.title || t('Select a chart')}</h2>{selected.data && <p className="muted">{selected.data.artist} · {selected.data.keys}Key · Lv. {selected.data.level}</p>}<div className="filter-bar"><label>{t("Arrangement")}<select value={arrangement} onChange={e => setArrangement(e.target.value)}>{['off', 'mirror', 'random', 's-random', 'scatter', 'converge'].map(value => <option key={value} value={value}>{value.toUpperCase()}</option>)}</select></label><label>{t("Gauge")}<select value={gauge} onChange={e => setGauge(e.target.value)}>{['normal', 'hard', 'death', 'easy', 'p-attack', 'g-attack'].map(value => <option key={value} value={value}>{value.toUpperCase()}</option>)}</select></label><label>{t("Records")}<select value={String(verified)} onChange={e => setVerified(e.target.value === 'true')}><option value="false">{t("All submissions")}</option><option value="true">{t("Verified only")}</option></select></label></div>{chartId && <Alert message={board.error} />}{chartId && board.data?.length ? <><IrScoreTable scores={board.data} /><div className="pagination"><button disabled={boardPage === 1} onClick={() => setBoardPage(value => value - 1)}>{t("Previous")}</button><span>{boardPage}</span><button disabled={board.data.length < 50} onClick={() => setBoardPage(value => value + 1)}>{t("Next")}</button></div></> : <Empty>{chartId ? t('No scores for these filters yet.') : t('Choose a chart to view its leaderboard.')}</Empty>}<p className="ranking-note">{t("Submitted records are reported by clients. Verified records require server replay validation.")}</p></section></div></>;
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

export function Multiplayer() {
  const { t } = useI18n();
  const { user } = useAuth();
  return <><Heading eyebrow={t("MULTIPLAYER")} title={t("A shared challenge")}>{t("Create a room, choose a chart and get everyone ready.")}</Heading>{user ? <RoomPanel /> : <SignInPrompt />}</>;
}
function RoomPanel() {
  const { t, locale } = useI18n();
  const { user } = useAuth(); const { connection, connected, rooms, room, setRoom, messages, error: liveError } = useLive();
  const [error, setError] = useState(''); const [busy, setBusy] = useState(false); const [channel, setChannel] = useState('lobby');
  const [chart, setChart] = useState(''); const [name, setName] = useState(''); const [text, setText] = useState(''); const [confirmed, setConfirmed] = useState(false);
  const [now, setNow] = useState(Date.now()); const charts = useQuery<Chart[]>('/charts?page=1');
  const history = useQuery<ChatMessage[]>('/chat/' + (room && channel === 'room' ? room.id : 'lobby'));
  useEffect(() => { const timer = setInterval(() => setNow(Date.now()), 250); return () => clearInterval(timer); }, []);
  useEffect(() => { setConfirmed(false); }, [room?.chart?.id]);
  useEffect(() => { if (!room) setChannel('lobby'); }, [room?.id]);
  async function invoke(method: string, ...args: unknown[]) {
    if (!connection || !connected) return;
    setError(''); setBusy(true);
    try { const next = await connection.invoke<Room | undefined>(method, ...args); if (next) setRoom(next); if (method === 'LeaveRoom') setRoom(null); }
    catch (e) { setError((e as Error).message); } finally { setBusy(false); }
  }
  const me = room?.members.find(member => member.id === user?.id); const host = room?.hostId === user?.id;
  const target = room && channel === 'room' ? room.id : 'lobby';
  const chat = [...new Map([...(history.data ?? []).slice().reverse(), ...messages.filter(message => message.channel === target)].map(message => [message.id, message])).values()].sort((a, b) => a.id - b.id).slice(-100);
  async function chatSubmit(event: FormEvent) {
    event.preventDefault(); if (!connection || !text.trim()) return; setError('');
    try { await connection.invoke('SendChat', target, text); setText(''); } catch (e) { setError((e as Error).message); }
  }
  const disabled = !connected || busy;
  return <><Alert message={error || liveError} /><div className="multiplayer-layout"><section className="panel"><div className="section-title"><h2>{room ? room.name : t('Open rooms')}</h2><span className={'badge' + (connected ? ' verified' : '')}>{connected ? t('Connected') : t('Offline')}</span></div>{!room ? <><form className="inline-form" onSubmit={e => { e.preventDefault(); void invoke('CreateRoom', name); }}><input required maxLength={80} placeholder={t("Name your room…")} aria-label={t("Room name")} value={name} onChange={e => setName(e.target.value)} /><button className="button primary" disabled={disabled}>{t("Create room")}</button></form>{rooms.length ? <div className="room-list">{rooms.map(item => <div key={item.id}><span><strong>{item.name}</strong><small>{item.chart?.title || t('Choosing a chart')} · {t(item.state)}</small></span><span className="badge">{item.members.length}/16</span><button className="button small secondary" disabled={disabled || !['lobby', 'results'].includes(item.state) || item.members.length >= 16} onClick={() => invoke('JoinRoom', item.id)}>{t("Join")}</button></div>)}</div> : <Empty>{t("No open rooms. Start the next session.")}</Empty>}</> : <><div className="room-status"><span className="badge">{t(room.state)}</span><strong>{room.chart?.title || t('Choose a chart')}</strong><button className="text-button" disabled={disabled} onClick={() => invoke('LeaveRoom')}>{t("Leave room")}</button></div>{host && ['lobby', 'results'].includes(room.state) && <div className="inline-form"><select aria-label={t("Select room chart")} value={chart} onChange={e => setChart(e.target.value)}><option value="">{t("Choose a chart")}</option>{charts.data?.map(item => <option key={item.id} value={item.id}>{item.title} · {item.keys}K · Lv. {item.level}</option>)}</select><button className="button secondary" disabled={disabled || !chart} onClick={() => { const selected = charts.data?.find(item => item.id === chart); if (selected) void invoke('SelectChart', selected.id, selected.packId, room.version); }}>{t("Select")}</button></div>}{room.chart && <div className="chart-confirmation">{room.chart.shareId ? <a href={'/api/room-content/' + room.chart.shareId + '/download'}>{t("Download shared song ZIP ↓")}</a> : room.chart.packId ? <a href={'/api/packs/' + room.chart.packId + '/download'}>{t("Download selected pack ↓")}</a> : <span>{t("Waiting for the host to share the song")}</span>}{room.chart.contentSha256 && <span>{t("Install and verify this song with the desktop client.")}</span>}<label className="check-label"><input type="checkbox" checked={confirmed} onChange={e => setConfirmed(e.target.checked)} />{t("I have downloaded the selected chart")}</label><button className="button secondary" disabled={disabled || !confirmed || room.state !== 'lobby' || !!room.chart.contentSha256} onClick={() => invoke('SetReady', !me?.ready, room.chart!.sha256, room.version)}>{me?.ready ? t('Not ready') : t('Ready')}</button>{host && <button className="button primary" disabled={disabled || room.state !== 'lobby' || !room.members.every(member => member.ready)} onClick={() => invoke('StartRound', room.version)}>{t("Start round")}</button>}</div>}{room.startAt && room.state === 'countdown' && <div className="countdown">{t("Starting in ")}{Math.max(0, Math.ceil((new Date(room.startAt).getTime() - now) / 1000))}</div>}<div className="live-ranking">{room.members.map((member, index) => <div key={member.id}><span className="rank-position">{index + 1}</span><Avatar user={member} /><span className="member-name"><Link to={'/players/' + member.username}>{member.displayName}</Link><small>{member.id === room.hostId ? t('Host · ') : ''}{member.finished ? t('Finished') : member.ready ? t('Ready') : t('Preparing')}{member.progress > 0 && ` · ${Math.round(member.progress * 100)}%`}</small></span><strong className="score-number">{member.exScore.toLocaleString(locale)}</strong></div>)}</div><p className="ranking-note">{t("This website manages rooms and chat. Synchronized gameplay and live score reporting require the desktop client integration.")}</p></>}</section><section className="panel chat-panel"><div className="section-title"><h2>{t("Chat")}</h2><div className="tabs compact"><button className={channel === 'lobby' ? 'active' : ''} onClick={() => setChannel('lobby')}>{t("Lobby")}</button><button disabled={!room} className={channel === 'room' ? 'active' : ''} onClick={() => setChannel('room')}>{t("Room")}</button></div></div><Alert message={history.error} /><div className="chat-messages" role="log" aria-label={t("Chat messages")}>{chat.length ? chat.map(message => <div className="chat-message" key={message.id}><div><Link to={'/players/' + message.username}>{message.displayName}</Link><time>{new Date(message.createdAt).toLocaleTimeString(locale, { hour: '2-digit', minute: '2-digit' })}</time></div><p>{message.text}</p></div>) : <Empty>{t("Say hello.")}</Empty>}</div><form className="inline-form" onSubmit={chatSubmit}><input aria-label={t("Message")} placeholder={t("Write a message…")} maxLength={500} value={text} onChange={e => setText(e.target.value)} /><button className="button primary" disabled={!connected || !text.trim()}>{t("Send")}</button></form></section></div></>;
}
