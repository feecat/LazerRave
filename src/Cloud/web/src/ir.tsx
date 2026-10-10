import { useI18n } from './i18n';
import { Fragment, useState } from 'react';
import { Link } from 'react-router-dom';
import { Alert, useQuery } from './components';
import type { RankingSummary, Score } from './types';

export function IrOverview({ chartId, arrangement, gauge, verified }: { chartId: string; arrangement: string; gauge: string; verified: boolean }) {
  const { t, locale } = useI18n();
  const summary = useQuery<RankingSummary>(`/rankings/${chartId}/summary?` + new URLSearchParams({ arrangement, gauge, verified: String(verified) }));
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

export function IrScoreTable({ scores }: { scores: Score[] }) {
  const { t, locale } = useI18n();
  const [expanded, setExpanded] = useState<string | null>(null);
  return <div className="table-wrap"><table className="ir-score-table"><thead><tr>
    <th>#</th><th>{t("Player / UID")}</th><th>{t("Best clear")}</th><th>{t("Rank")}</th><th>EX SCORE</th><th>{t("Rate")}</th><th>{t("Combo")}</th><th>{t("BP / min BP")}</th><th>PG</th><th>GR</th><th>GD</th><th>BD</th><th>PR</th><th>{t("Options")}</th><th>{t("Input")}</th><th>{t("Record")}</th><th>{t('Details')}</th>
  </tr></thead><tbody>{scores.map(score => <Fragment key={score.id}><tr>
    <td className={score.rank <= 3 ? 'podium' : ''}>{score.rank}</td>
    <td><Link to={'/players/id/' + score.uid}>{score.displayName}</Link><small className="table-secondary">UID {score.uid}</small>{score.comment && <small className="table-secondary">{score.comment}</small>}</td>
    <td><span className={'clear-lamp clear-' + score.bestClear}>{score.bestClear.toUpperCase()}</span></td><td>{score.letterRank ?? '—'}</td>
    <td className="score-number">{score.exScore.toLocaleString(locale)}<small className="table-secondary">/ {score.scoreMax?.toLocaleString(locale) ?? '—'}</small></td>
    <td>{score.scoreMax ? (score.exScore * 100 / score.scoreMax).toFixed(2) + '%' : '—'}</td><td>{score.maxCombo}</td><td>{score.misses} / {score.minMisses}</td>
    <td>{score.perfect}</td><td>{score.great}</td><td>{score.good}</td><td>{score.bad}</td><td>{score.poor}</td>
    <td>{score.arrangement.toUpperCase()}<small className="table-secondary">{score.gauge.toUpperCase()}</small></td><td>{t(score.inputType)}</td>
    <td><span className={'badge' + (score.verified ? ' verified' : '')}>{score.verified ? t('Verified') : t('Submitted')}</span></td>
    <td><button aria-expanded={expanded === score.id} aria-controls={'score-' + score.id} onClick={() => setExpanded(expanded === score.id ? null : score.id)}>{t(expanded === score.id ? 'Hide details' : 'Score details')}</button></td>
  </tr>{expanded === score.id && <tr id={'score-' + score.id} className="ir-detail-row"><td colSpan={17}>
    <dl className="ir-score-details">
      <div><dt>{t('Clear for this play')}</dt><dd>{score.clear.toUpperCase()}</dd></div>
      <div><dt>SCORE</dt><dd>{score.normalScore?.toLocaleString(locale) ?? '—'}</dd></div>
      <div><dt>{t('Played at')}</dt><dd>{new Date(score.createdAt).toLocaleString(locale)}</dd></div>
      <div><dt>PGREAT / GREAT / GOOD / BAD / POOR</dt><dd>{[score.perfect, score.great, score.good, score.bad, score.poor].join(' / ')}</dd></div>
    </dl>
    <p className="ranking-note">{t('EX SCORE, judgements and options describe the highest-scoring play. Best clear and minimum BP are independent records within the current filters.')}</p>
  </td></tr>}</Fragment>)}</tbody></table></div>;
}
