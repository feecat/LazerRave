import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';
import { api } from './api';
import type { User } from './types';
import { useI18n } from './i18n';

const AuthContext = createContext<{ user: User | null; loaded: boolean; setUser: (user: User | null) => void }>({ user: null, loaded: false, setUser: () => {} });
export function AuthProvider({ children }: { children: ReactNode }) {
  const { t } = useI18n();
  const [user, setUser] = useState<User | null>(null);
  const [loaded, setLoaded] = useState(false);
  const [error, setError] = useState('');
  useEffect(() => { api<User | null>('/me').then(setUser).catch(e => setError(e.message)).finally(() => setLoaded(true)); }, []);
  return <AuthContext.Provider value={{ user, loaded, setUser }}>{error && <div className="service-alert" role="alert">{t(error)}</div>}{children}</AuthContext.Provider>;
}
export const useAuth = () => useContext(AuthContext);
