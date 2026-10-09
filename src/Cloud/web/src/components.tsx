import { useEffect, useState, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { api } from './api';
import type { User } from './types';

export function Avatar({ user, large = false }: { user: Pick<User, 'displayName' | 'avatarUrl'>; large?: boolean }) {
  return <span className={'avatar' + (large ? ' avatar-large' : '')}>{user.avatarUrl ? <img src={user.avatarUrl} alt={`${user.displayName}'s avatar`} /> : user.displayName.slice(0, 1).toUpperCase()}</span>;
}
export function Heading({ eyebrow, title, children }: { eyebrow: string; title: string; children?: ReactNode }) {
  return <header className="page-heading"><span className="eyebrow">{eyebrow}</span><h1>{title}</h1>{children && <p>{children}</p>}</header>;
}
export function Empty({ children }: { children: ReactNode }) { return <div className="empty">{children}</div>; }
export function Alert({ message }: { message: string }) { return message ? <div className="alert" role="alert">{message}</div> : null; }
export function SignInPrompt() { return <Empty><p>Sign in to join the community.</p><Link className="button primary" to="/login">Sign in</Link></Empty>; }
export function useQuery<T>(path: string) {
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);
  const [revision, setRevision] = useState(0);
  useEffect(() => {
    let active = true;
    setLoading(true); setError('');
    api<T>(path).then(value => { if (active) setData(value); }).catch(e => { if (active) { setError(e.message); setData(null); } }).finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [path, revision]);
  return { data, error, loading, refresh: () => setRevision(value => value + 1) };
}
