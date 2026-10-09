import { createRoot } from 'react-dom/client';
import { BrowserRouter, Link, NavLink, Route, Routes, useNavigate } from 'react-router-dom';
import { AuthProvider, LiveProvider, useAuth, useLive } from './state';
import { Avatar, Empty } from './components';
import { api } from './api';
import { Home, Authentication, Profile, Packs, PackDetail, Rankings, Admin, Multiplayer } from './pages';
import './style.css';

function Shell() {
  const { user, loaded, setUser } = useAuth();
  const { connected } = useLive();
  const navigate = useNavigate();
  async function logout() {
    try { await api('/auth/logout', { method: 'POST' }); setUser(null); navigate('/'); }
    catch (error) { window.alert(error instanceof Error ? error.message : 'Sign out failed.'); }
  }
  return <>
    <div className="top-accent" />
    <nav className="navigation" aria-label="Main navigation">
      <Link className="brand" to="/"><img src="/logo.svg" alt="" /><span>Lazer<span>Rave</span><small>BMS COMMUNITY</small></span></Link>
      <div className="nav-links"><NavLink to="/rankings">Rankings</NavLink><NavLink to="/packs">Song packs</NavLink><NavLink to="/multiplayer">Multiplayer</NavLink>{user?.role === 'admin' && <NavLink to="/admin">Admin</NavLink>}</div>
      <div className="account-nav">{user ? <><Link to={'/players/' + user.username} className="player-link"><Avatar user={user} /><span>{user.displayName}</span></Link><button className="text-button" onClick={logout}>Sign out</button></> : <><Link to="/login" className="text-button">Sign in</Link><Link to="/register" className="button small primary">Join</Link></>}</div>
    </nav>
    <main id="main">{!loaded ? <Empty>Connecting to LazerRave…</Empty> : <Routes>
      <Route path="/" element={<Home />} /><Route path="/login" element={<Authentication />} /><Route path="/register" element={<Authentication register />} />
      <Route path="/players/:username" element={<Profile />} /><Route path="/packs" element={<Packs />} /><Route path="/packs/:id" element={<PackDetail />} />
      <Route path="/rankings" element={<Rankings />} /><Route path="/rankings/:chartId" element={<Rankings />} /><Route path="/admin" element={<Admin />} /><Route path="/multiplayer" element={<Multiplayer />} />
      <Route path="*" element={<Empty>Page not found. <Link to="/">Return home</Link></Empty>} />
    </Routes>}</main>
    <footer className="footer"><Link className="footer-brand" to="/">LazerRave</Link><span>BMS. One chart, many possibilities.</span><span className="connection-label"><i className={connected ? 'online-dot' : ''} />{user ? connected ? 'Community connected' : 'Community offline' : 'LazerRave.com'}</span></footer>
  </>;
}
createRoot(document.getElementById('root')!).render(<BrowserRouter><AuthProvider><LiveProvider><Shell /></LiveProvider></AuthProvider></BrowserRouter>);
