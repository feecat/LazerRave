import { useI18n } from './i18n';
import { Fragment, useState } from 'react';
import { Link } from 'react-router-dom';
import { Alert, Avatar } from './components';
import type { RankingSummary, Score } from './types';

export function playTime(score: Score, locale: string): string {
  return score.playedAt ? new Date(score.playedAt).toLocaleString(locale) : "—";
}

export function IrOverview({ summary }: { summary: { data: RankingSummary | null; error: string; loading: boolean } }) {
  const { t, locale } = useI18n();
  return <section className="ir-overview" aria-label={t('Ranking overview')}>
    <Alert message={summary.error} />
    {!summary.loading && summary.data && <>
      <dl className="ir-summary">
        <div><dt>{t('Players')}</dt><dd>{summary.data.players.toLocaleString(locale)}</dd></div>
        <div><dt>{t('Submissions')}</dt><dd>{summary.data.submissions.toLocaleString(locale)}</dd></div>
        <div><dt>{t('Clear rate')}</dt><dd>{summary.data.players ? (summary.data.clearedPlayers * 100 / summary.data.players).toFixed(2) + '%' : '—'}</dd></div>
      </dl>
      <div className="ir-lamps" aria-label={t('Best clear distribution')}>
        {(['failed', 'assist', 'easy', 'normal', 'hard', 'fullCombo', 'perfect'] as const).map(lamp => <span key={lamp} className={'clear-lamp clear-' + (lamp === 'fullCombo' ? 'full-combo' : lamp)}>{t(lamp === 'fullCombo' ? 'FULL COMBO' : lamp.toUpperCase())} <strong>{summary.data![lamp].toLocaleString(locale)}</strong></span>)}
      </div>
      <p className="ranking-note">{t('Statistics use the current filters. Each player counts once, using their best clear.')}</p>
    </>}
  </section>;
}

export function IrScoreTable({ scores, currentUid }: { scores: Score[]; currentUid?: number }) {
  const { t, locale } = useI18n();
  const [expanded, setExpanded] = useState<string | null>(null);
  return <div className="table-wrap"><table className="ir-score-table"><thead><tr>
    <th>#</th><th>{t('Player / UID')}</th><th>EX SCORE</th><th>{t('Rank')}</th><th>{t('Best clear')}</th><th>{t('BP / min BP')}</th><th>{t('Plays')}</th><th>{t('Played at')}</th><th>{t('Details')}</th>
  </tr></thead><tbody>{scores.map(score => <Fragment key={score.id}><tr className={currentUid === score.uid ? 'ir-current-player' : ''}>
    <td className={score.rank <= 3 ? 'podium' : ''}>{score.rank}</td><td><Link className="ir-player-cell" to={'/players/id/' + score.uid}><Avatar user={{ displayName: score.displayName, avatarUrl: score.avatarUrl ?? null }} /><span>{score.displayName}<small className="table-secondary">UID {score.uid}</small></span></Link></td>
    <td className="score-number">{score.exScore.toLocaleString(locale)}<small className="table-secondary">{score.scoreMax ? (score.exScore * 100 / score.scoreMax).toFixed(2) + '%' : '—'}</small></td><td>{score.letterRank ?? '—'}</td>
    <td><span className={'clear-lamp clear-' + score.bestClear}>{score.bestClear.toUpperCase()}</span></td><td>{score.misses} / {score.minMisses}</td>
    <td>{score.playCount?.toLocaleString(locale) ?? '—'}</td><td>{playTime(score, locale)}</td>
    <td><button aria-expanded={expanded === score.id} aria-controls={'score-' + score.id} onClick={() => setExpanded(expanded === score.id ? null : score.id)}>{t(expanded === score.id ? 'Hide details' : 'Score details')}</button></td>
  </tr>{expanded === score.id && <tr id={'score-' + score.id} className="ir-detail-row"><td colSpan={9}>
    <dl className="ir-score-details"><div><dt>{t('Clear for this play')}</dt><dd>{score.clear.toUpperCase()}</dd></div><div><dt>SCORE</dt><dd>{score.normalScore?.toLocaleString(locale) ?? '—'}</dd></div><div><dt>{t('Combo')}</dt><dd>{score.maxCombo}</dd></div><div><dt>{t('Options')}</dt><dd>{score.arrangement.toUpperCase()} · {score.gauge.toUpperCase()}</dd></div><div><dt>{t('Input')}</dt><dd>{t(score.inputType)}</dd></div><div><dt>{t('Played at')}</dt><dd>{playTime(score, locale)}</dd></div><div><dt>PGREAT / GREAT / GOOD / BAD / POOR</dt><dd>{[score.perfect, score.great, score.good, score.bad, score.poor].join(' / ')}</dd></div><div><dt>{t('Record')}</dt><dd>{t(score.verified ? 'Verified' : 'Submitted')}</dd></div></dl>
    {score.comment && <p>{score.comment}</p>}<p className="ranking-note">{t('EX SCORE, judgements and options describe the highest-scoring play. Best clear and minimum BP are independent records within the current filters.')}</p><Link to={'/scores/' + score.id}>{t('Full score details')} →</Link>
  </td></tr>}</Fragment>)}</tbody></table></div>;
}
