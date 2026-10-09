import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { isLocale, translate, type Locale } from './locales';
import { Icon } from './Icon';

const storageKey = 'lazerrave.language';
const LocaleContext = createContext({ locale: 'en' as Locale, setLocale: (_: Locale) => {}, t: (message: string) => message });

function initialLocale(): Locale {
  try { const saved = localStorage.getItem(storageKey); return isLocale(saved) ? saved : 'en'; }
  catch { return 'en'; }
}

export function LocaleProvider({ children }: { children: ReactNode }) {
  const [locale, updateLocale] = useState<Locale>(initialLocale);
  const setLocale = useCallback((value: Locale) => {
    if (!isLocale(value)) return;
    updateLocale(value);
    try { localStorage.setItem(storageKey, value); } catch { /* Storage may be unavailable in private or restricted browsers. */ }
  }, []);
  useEffect(() => {
    document.documentElement.lang = locale;
    document.querySelector('meta[name="description"]')?.setAttribute('content', locale === 'zh-CN' ? 'LazerRave — BMS 曲包、网络排名与多人游戏。' : 'LazerRave — BMS charts, Internet Ranking and multiplayer.');
  }, [locale]);
  useEffect(() => {
    const changed = (event: StorageEvent) => {
      if (event.storageArea === localStorage && (event.key === storageKey || event.key === null)) updateLocale(isLocale(event.newValue) ? event.newValue : 'en');
    };
    window.addEventListener('storage', changed);
    return () => window.removeEventListener('storage', changed);
  }, []);
  const t = useCallback((message: string) => translate(locale, message), [locale]);
  const value = useMemo(() => ({ locale, setLocale, t }), [locale, setLocale, t]);
  return <LocaleContext.Provider value={value}>{children}</LocaleContext.Provider>;
}

export const useI18n = () => useContext(LocaleContext);

export function LanguageSelect() {
  const { locale, setLocale, t } = useI18n();
  return <label className="language-switch"><span className="visually-hidden">{t('Language')}</span><Icon name="globe" /><select aria-label={t('Language')} value={locale} onChange={event => { if (isLocale(event.target.value)) setLocale(event.target.value); }}>
    <option value="en" lang="en">English</option><option value="zh-CN" lang="zh-CN">简体中文</option>
  </select><Icon name="chevron" size={16} /></label>;
}
