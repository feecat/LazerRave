import { useI18n } from './i18n';
import { useEffect, useState, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { api } from './api';
import type { User } from './types';

export function useAmbientEffects() {
  useEffect(() => {
    const header = document.querySelector('.site-header');
    const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
    let frame = 0;
    const scroll = () => header?.classList.toggle('scrolled', window.scrollY > 8);
    const move = (event: PointerEvent) => {
      if (event.pointerType !== 'mouse' || reducedMotion.matches || !(event.target instanceof Element)) return;
      const card = event.target.closest<HTMLElement>('.spotlight');
      if (!card) return;
      cancelAnimationFrame(frame);
      frame = requestAnimationFrame(() => {
        const rect = card.getBoundingClientRect();
        card.style.setProperty('--spotlight-x', `${event.clientX - rect.left}px`);
        card.style.setProperty('--spotlight-y', `${event.clientY - rect.top}px`);
      });
    };
    scroll();
    window.addEventListener('scroll', scroll, { passive: true });
    window.addEventListener('pointermove', move, { passive: true });
    return () => {
      cancelAnimationFrame(frame);
      window.removeEventListener('scroll', scroll);
      window.removeEventListener('pointermove', move);
    };
  }, []);
}

export function Avatar({ user, large = false }: { user: Pick<User, 'displayName' | 'avatarUrl'>; large?: boolean }) {
  const { t } = useI18n();
  return <span className={'avatar' + (large ? ' avatar-large' : '')}>{user.avatarUrl ? <img src={user.avatarUrl} alt={`${user.displayName} · ${t('avatar')}`} /> : user.displayName.slice(0, 1).toUpperCase()}</span>;
}
export function Heading({ eyebrow, title, children }: { eyebrow: string; title: string; children?: ReactNode }) {
  return <header className="page-heading"><span className="eyebrow">{eyebrow}</span><h1>{title}</h1>{children && <p>{children}</p>}</header>;
}
export function Empty({ children }: { children: ReactNode }) { return <div className="empty">{children}</div>; }
export function Alert({ message }: { message: string }) {
  const { t } = useI18n();
  return message ? <div className="alert" role="alert">{t(message)}</div> : null;
}
export function SignInPrompt() {
  const { t } = useI18n();
  return <Empty><p>{t("Sign in to join the community.")}</p><Link className="button primary" to="/login">{t("Sign in")}</Link></Empty>;
}
export function useQuery<T>(path: string | null) {
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);
  const [revision, setRevision] = useState(0);
  useEffect(() => {
    let active = true;
    setData(null); setLoading(!!path); setError('');
    if (!path) return;
    api<T>(path).then(value => { if (active) setData(value); }).catch(e => { if (active) { setError(e.message); setData(null); } }).finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [path, revision]);
  return { data, error, loading, refresh: () => setRevision(value => value + 1) };
}
