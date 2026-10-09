import { useI18n } from './i18n';
import { useEffect, useState, type FormEvent } from 'react';
import { Link, useParams } from 'react-router-dom';
import { Alert, Empty, Heading, useQuery } from './components';
import { send } from './api';

interface DifficultyTable { id: number; name: string; symbol: string; description: string; sourceUrl: string | null; published: boolean; chartCount: number; levelCount: number }
interface Level { level: string; chartCount: number }
interface Entry { md5: string; level: string; title: string; artist: string; url: string | null; chartId: string | null; keys: number | null }

export function Tables() {
  const { t } = useI18n();
  const { data, error, loading } = useQuery<DifficultyTable[]>('/tables');
  return <><Heading eyebrow={t("DIFFICULTY TABLES")} title={t("Find your next challenge")}>{t("Browse charts by difficulty level.")}</Heading><Alert message={error} />
    {loading ? <Empty>{t("Loading difficulty tables…")}</Empty> : data?.length ? <div className="pack-grid">{data.map(table => <Link className="panel difficulty-card" key={table.id} to={'/tables/' + table.id}>
      <span className="table-symbol">{table.symbol}</span><h2>{table.name}</h2><p className="muted">{table.description}</p><small>{table.chartCount} {t(" charts · ")}{table.levelCount} {t(" levels")}</small>
    </Link>)}</div> : <Empty>{t("No published difficulty tables.")}</Empty>}
  </>;
}

export function TableDetail() {
  const { t } = useI18n();
  const { tableId, level } = useParams();
  const [page, setPage] = useState(1);
  useEffect(() => { setPage(1); }, [tableId, level]);
  const detail = useQuery<{ table: DifficultyTable; levels: Level[] }>('/tables/' + tableId);
  const entries = useQuery<Entry[]>('/tables/' + tableId + '/entries?' + new URLSearchParams({ ...(level ? { level } : {}), page: String(page) }));
  if (!detail.data) return detail.loading ? <Empty>{t("Loading difficulty table…")}</Empty> : <Alert message={detail.error} />;
  const { table, levels } = detail.data;
  return <><Heading eyebrow={t("DIFFICULTY TABLE")} title={table.name}>{table.description}</Heading>
    <div className="pack-detail-meta"><Link to="/tables">{t("All tables")}</Link><a href={'/api/tables/' + tableId + '/header.json'}>{t("Table header")}</a><a href={'/api/tables/' + tableId + '/data.json'}>{t("Chart data")}</a>{table.sourceUrl && <a href={table.sourceUrl} target="_blank" rel="noopener noreferrer">{t("Original source ↗")}</a>}</div>
    <div className="ranking-layout"><aside className="panel"><h2>{t("Levels")}</h2><Link className={'chart-choice' + (!level ? ' selected' : '')} to={'/tables/' + tableId}>{t("All levels")}</Link>{levels.map(item => <Link className={'chart-choice' + (level === item.level ? ' selected' : '')} key={item.level} to={'/tables/' + tableId + '/' + encodeURIComponent(item.level)}><strong>{table.symbol}{item.level}</strong><span className="badge">{item.chartCount}</span></Link>)}</aside>
      <section className="panel"><h2>{level ? table.symbol + level : t('All charts')}</h2><Alert message={entries.error} />{entries.data?.length ? <div className="table-wrap"><table><thead><tr><th>{t("Level")}</th><th>{t("Title / artist")}</th><th>{t("Keys")}</th><th>{t("Ranking / source")}</th></tr></thead><tbody>{entries.data.map(entry => <tr key={entry.md5}>
        <td><span className="badge">{table.symbol}{entry.level}</span></td><td><strong>{entry.title || t('Uncatalogued chart')}</strong><small className="table-secondary">{entry.artist}</small><small className="table-secondary mono">{entry.md5}</small></td><td>{entry.keys ? entry.keys + 'K' : '—'}</td>
        <td>{entry.chartId ? <Link to={'/rankings/' + entry.chartId}>{t("Internet Ranking")}</Link> : <span className="muted">{t("Not in song catalog")}</span>}{entry.url && <a className="table-secondary" href={entry.url} target="_blank" rel="noopener noreferrer">{t("Chart source ↗")}</a>}</td>
      </tr>)}</tbody></table></div> : <Empty>{entries.loading ? t('Loading charts…') : t('No charts for this level.')}</Empty>}
      <div className="pagination"><button disabled={page === 1} onClick={() => setPage(value => value - 1)}>{t("Previous")}</button><span>{page}</span><button disabled={(entries.data?.length ?? 0) < 50} onClick={() => setPage(value => value + 1)}>{t("Next")}</button></div></section>
    </div></>;
}

export function DifficultyTableAdmin() {
  const { t } = useI18n();
  const { data, error, refresh } = useQuery<DifficultyTable[]>('/admin/tables');
  const [editing, setEditing] = useState<DifficultyTable | null>(null);
  const [notice, setNotice] = useState(''); const [busy, setBusy] = useState(false);
  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); const form = new FormData(event.currentTarget); setBusy(true); setNotice('');
    try {
      const file = form.get('data') as File;
      if (file.size > 3 * 1024 * 1024) throw new Error('Chart data must be no larger than 3 MiB.');
      const parsed: unknown = JSON.parse((await file.text()).replace(/^\uFEFF/, ''));
      if (!Array.isArray(parsed) || !parsed.length || parsed.length > 10000) throw new Error('Use a BMS difficulty-table data array with 1–10,000 entries.');
      const entries = parsed.map((item: unknown) => {
        if (!item || typeof item !== 'object' || !('md5' in item) || !('level' in item)) throw new Error('Each entry requires md5 and level.');
        const value = item as Record<string, unknown>;
        return { md5: value.md5, level: String(value.level), title: value.title ?? '', artist: value.artist ?? '', url: value.url || null };
      });
      await send('/admin/tables' + (editing ? '/' + editing.id : ''), editing ? 'PUT' : 'POST', { name: form.get('name'), symbol: form.get('symbol'), description: form.get('description'), sourceUrl: form.get('sourceUrl') || null, entries });
      setEditing(null); setNotice('Difficulty table saved. Publish it to make it visible.'); refresh();
    } catch (e) { setNotice((e as Error).message); } finally { setBusy(false); }
  }
  async function publish(table: DifficultyTable) {
    setNotice(''); setBusy(true);
    try { await send('/admin/tables/' + table.id + '/publication', 'PUT', { published: !table.published }); refresh(); }
    catch (e) { setNotice((e as Error).message); } finally { setBusy(false); }
  }
  return <><Alert message={error || notice} /><div className="admin-columns"><form className="panel" key={editing?.id ?? 'new'} onSubmit={save}>
    <h2>{editing ? t('Replace difficulty table') : t('Import difficulty table')}</h2>
    <label>{t("Name")}<input name="name" required maxLength={120} defaultValue={editing?.name ?? ''} /></label>
    <label>{t("Symbol")}<input name="symbol" required maxLength={16} placeholder="★" defaultValue={editing?.symbol ?? ''} /></label>
    <label>{t("Description")}<textarea name="description" maxLength={2000} defaultValue={editing?.description ?? ''} /></label>
    <label>{t("Source URL")}<input name="sourceUrl" type="url" maxLength={1000} defaultValue={editing?.sourceUrl ?? ''} /></label>
    <label>{t("Chart data")}<input name="data" type="file" required accept=".json,application/json" /></label>
    <small>{t("Upload the standard data file containing md5, level, title and artist. Songs do not need to be uploaded first. Replacing a table replaces its full chart list.")}</small>
    <button className="button primary" disabled={busy}>{busy ? t('Saving…') : t('Save table')}</button>{editing && <button type="button" className="text-button" onClick={() => setEditing(null)}>{t("Cancel editing")}</button>}
  </form><section className="panel"><h2>{t("Difficulty tables")}</h2>{data?.length ? <div className="admin-list">{data.map(table => <div key={table.id}><span><Link to={'/tables/' + table.id}><strong>{table.name}</strong></Link><small>{table.symbol} · {table.chartCount} {t(" charts · ")}{table.published ? t('Published') : t('Draft')}</small></span><button className="button small secondary" disabled={busy} onClick={() => setEditing(table)}>{t("Edit")}</button><button className="button small secondary" disabled={busy} onClick={() => void publish(table)}>{table.published ? t('Unpublish') : t('Publish')}</button></div>)}</div> : <Empty>{t("No difficulty tables.")}</Empty>}</section></div></>;
}
