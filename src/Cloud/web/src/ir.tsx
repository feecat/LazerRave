import { useI18n } from './i18n';
import { Link } from 'react-router-dom';
import type { Score } from './types';

export function IrScoreTable({ scores }: { scores: Score[] }) {
  const { t, locale } = useI18n();
  return <div className="table-wrap"><table className="ir-score-table"><thead><tr>
    <th>#</th><th>{t("Player / UID")}</th><th>{t("Clear")}</th><th>{t("Rank")}</th><th>EX SCORE</th><th>{t("Rate")}</th><th>{t("Combo")}</th><th>{t("BP / min BP")}</th><th>PG</th><th>GR</th><th>GD</th><th>BD</th><th>PR</th><th>{t("Options")}</th><th>{t("Input")}</th><th>{t("Record")}</th>
  </tr></thead><tbody>{scores.map(score => <tr key={score.id}>
    <td className={score.rank <= 3 ? 'podium' : ''}>{score.rank}</td>
    <td><Link to={'/players/id/' + score.uid}>{score.displayName}</Link><small className="table-secondary">UID {score.uid}</small>{score.comment && <small className="table-secondary">{score.comment}</small>}</td>
    <td><span className={'clear-lamp clear-' + score.bestClear}>{score.bestClear.toUpperCase()}</span></td><td>{score.letterRank ?? '—'}</td>
    <td className="score-number">{score.exScore.toLocaleString(locale)}<small className="table-secondary">/ {score.scoreMax?.toLocaleString(locale) ?? '—'}</small></td>
    <td>{score.scoreMax ? (score.exScore * 100 / score.scoreMax).toFixed(2) + '%' : '—'}</td><td>{score.maxCombo}</td><td>{score.misses} / {score.minMisses}</td>
    <td>{score.perfect}</td><td>{score.great}</td><td>{score.good}</td><td>{score.bad}</td><td>{score.poor}</td>
    <td>{score.arrangement.toUpperCase()}<small className="table-secondary">{score.gauge.toUpperCase()}</small></td><td>{t(score.inputType)}</td>
    <td><span className={'badge' + (score.verified ? ' verified' : '')}>{score.verified ? t('Verified') : t('Submitted')}</span></td>
  </tr>)}</tbody></table></div>;
}
