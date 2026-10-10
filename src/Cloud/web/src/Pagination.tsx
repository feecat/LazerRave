import { useEffect, useRef, useState } from 'react';
import { useI18n } from './i18n';

export function Pagination({ page, totalPages, next = false, loading = false, change }: {
  page: number; totalPages?: number; next?: boolean; loading?: boolean; change: (value: string) => void;
}) {
  const { t } = useI18n();
  const [target, setTarget] = useState(String(page));
  const [editing, setEditing] = useState(false);
  const active = useRef(false);
  const input = useRef<HTMLInputElement>(null);
  const button = useRef<HTMLButtonElement>(null);
  const pages = totalPages === undefined ? undefined : Math.max(1, Math.ceil(totalPages));
  useEffect(() => {
    setTarget(String(page)); setEditing(false); active.current = false;
  }, [page, pages]);
  useEffect(() => {
    if (editing) { input.current?.focus(); input.current?.select(); }
  }, [editing]);
  useEffect(() => {
    if (!loading && pages !== undefined && page > pages) change(String(pages));
  }, [page, pages, loading, change]);
  const value = Number(target);
  const valid = /^\d+$/.test(target) && Number.isSafeInteger(value) && value >= 1 && pages !== undefined && value <= pages;
  function finish(restoreFocus = false, cancel = false) {
    if (!active.current) return;
    active.current = false; setEditing(false); setTarget(String(page));
    if (!cancel && !loading && valid && value !== page) change(String(value));
    if (restoreFocus) requestAnimationFrame(() => button.current?.focus());
  }
  function begin() {
    active.current = true; setTarget(String(page)); setEditing(true);
  }
  return <nav className="pagination" aria-label={t('Pagination')}>
    <button type="button" disabled={loading || page <= 1} onPointerDown={() => finish(false, true)} onClick={() => change(String(page - 1))}>{t('Previous')}</button>
    <span className="pagination-count">
      <span className="pagination-page" style={{ width: `${Math.max(2, String(pages ?? page).length) + 1}ch` }}>
        {editing ? <input ref={input} className="pagination-page-input" aria-label={t('Page number')} aria-invalid={!valid} type="text" inputMode="numeric" pattern="[0-9]*" maxLength={10} value={target} onChange={event => setTarget(event.target.value)} onBlur={() => finish()} onKeyDown={event => {
          if (event.key === 'Escape') { event.preventDefault(); finish(true, true); }
          if (event.key === 'Enter') { event.preventDefault(); if (valid) finish(true); }
        }} /> : <button ref={button} type="button" className="pagination-page-button" aria-label={`${t('Page')} ${page}: ${t('Edit page number')}`} title={t('Click to edit page number')} disabled={loading || pages === undefined || pages <= 1} onClick={begin}>{page}</button>}
      </span>{pages !== undefined && <span className="pagination-total">/ {pages}</span>}
    </span>
    <button type="button" disabled={loading || (pages === undefined ? !next : page >= pages)} onPointerDown={() => finish(false, true)} onClick={() => change(String(page + 1))}>{t('Next')}</button>
  </nav>;
}
