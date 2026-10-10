import { LanguageSelect, LocaleProvider, useI18n } from './i18n';
import { createRoot } from 'react-dom/client';
import { BrowserRouter, Link, NavLink, Route, Routes, useNavigate } from 'react-router-dom';
import { AuthProvider, useAuth } from './state';
import { Avatar, Empty } from './components';
import { api } from './api';
import { Home, Authentication, ChangePassword, Profile, Packs, PackDetail, Admin } from './pages';
import { Rankings, SongRanking, MyRankings, ScorePage } from './RankingPages';
import { Tables, TableDetail } from './tables';
import { Icon, type IconName } from './Icon';
import { Downloads } from './Downloads';
import { repositoryUrl } from './releases';
import './style.css';

const navigation: { path: string; label: string; icon: IconName }[] = [
  { path: '/download', label: 'Download', icon: 'download' },
  { path: '/rankings', label: 'Rankings', icon: 'ranking' },
  { path: '/packs', label: 'Song packs', icon: 'pack' },
  { path: '/tables', label: 'Difficulty tables', icon: 'table' },
  { path: '/admin', label: 'Admin', icon: 'admin' },
];

function Shell() {
  const { t } = useI18n();
  const { user, loaded, setUser } = useAuth();
  const navigate = useNavigate();
  async function logout() {
    try { await api('/auth/logout', { method: 'POST' }); setUser(null); navigate('/'); }
    catch (error) { window.alert(t(error instanceof Error ? error.message : 'Sign out failed.')); }
  }
  return <>
    <div className="top-accent" />
    <nav className="navigation" aria-label={t("Main navigation")}>
      <Link className="brand" to="/"><img src="/logo.svg" alt="" /><span>Lazer<span>Rave</span><small>{t("BMS COMMUNITY")}</small></span></Link>
      <div className="nav-links">{navigation.filter(item => item.path !== '/admin' || user?.role === 'admin').map(item => <NavLink key={item.path} to={item.path}><Icon name={item.icon} size={22} /><span>{t(item.label)}</span></NavLink>)}</div>
      <div className="account-nav"><a className="repository-link" href={repositoryUrl} target="_blank" rel="noopener noreferrer"><Icon name="code" /><span>GitHub</span></a><LanguageSelect />{user ? <><Link to={'/players/' + user.username} className="player-link"><Avatar user={user} /><span>{user.displayName}</span></Link><button className="text-button" onClick={logout}>{t("Sign out")}</button></> : <><Link to="/login" className="text-button">{t("Sign in")}</Link><Link to="/register" className="button small primary">{t("Join")}</Link></>}</div>
    </nav>
    <main id="main" key={user?.id ?? "anonymous"}>{!loaded ? <Empty>{t("Connecting to LazerRave…")}</Empty> : <Routes>
      <Route path="/" element={<Home />} /><Route path="/login" element={<Authentication />} /><Route path="/register" element={<Authentication register />} />
      <Route path="/download" element={<Downloads />} /><Route path="/account/password" element={<ChangePassword />} />
      <Route path="/players/:username" element={<Profile />} /><Route path="/players/id/:uid" element={<Profile />} /><Route path="/tables" element={<Tables />} /><Route path="/tables/:tableId" element={<TableDetail />} /><Route path="/tables/:tableId/:level" element={<TableDetail />} /><Route path="/packs" element={<Packs />} /><Route path="/packs/:id" element={<PackDetail />} />
      <Route path="/rankings/mine" element={<MyRankings />} /><Route path="/scores/:id" element={<ScorePage />} /><Route path="/rankings" element={<Rankings />} /><Route path="/rankings/songs/:songKey" element={<SongRanking />} /><Route path="/rankings/:chartId" element={<Rankings />} /><Route path="/admin" element={<Admin />} />
      <Route path="*" element={<Empty>{t("Page not found. ")}<Link to="/">{t("Return home")}</Link></Empty>} />
    </Routes>}</main>
    <footer className="footer"><Link className="footer-brand" to="/">LazerRave</Link><span>{t("BMS. One chart, many possibilities.")}</span><Link to="/download">{t("Download")}</Link><a href={repositoryUrl} target="_blank" rel="noopener noreferrer">GitHub</a><span className="connection-label">LazerRave.com</span></footer>
  </>;
}
createRoot(document.getElementById('root')!).render(<LocaleProvider><BrowserRouter><AuthProvider><Shell /></AuthProvider></BrowserRouter></LocaleProvider>);
