import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { Heading } from './components';
import { Icon } from './Icon';
import { useI18n } from './i18n';
import { date, size } from './api';
import { releasesApi, releasesUrl, repositoryUrl, selectReleases, type DesktopRelease } from './releases';

export function Downloads() {
  const { t, locale } = useI18n();
  const [releases, setReleases] = useState<DesktopRelease[]>([]);
  const [status, setStatus] = useState<'loading' | 'ready' | 'error'>('loading');
  const [attempt, setAttempt] = useState(0);
  useEffect(() => {
    let cancelled = false;
    const controller = new AbortController();
    const timeout = window.setTimeout(() => controller.abort(), 15000);
    setStatus('loading');
    async function load() {
      try {
        const response = await fetch(releasesApi, { signal: controller.signal, headers: { Accept: 'application/vnd.github+json' } });
        if (!response.ok) throw new Error('Release lookup failed.');
        const available = selectReleases(await response.json());
        if (!cancelled) { setReleases(available); setStatus('ready'); }
      } catch {
        if (!cancelled) setStatus('error');
      } finally { window.clearTimeout(timeout); }
    }
    void load();
    return () => { cancelled = true; window.clearTimeout(timeout); controller.abort(); };
  }, [attempt]);
  const latest = releases[0];
  return <>
    <Heading eyebrow={t('THE DESKTOP CLIENT')} title={t('Download LazerRave')}><p>{t('Your next session starts here.')}</p></Heading>
    <div className="download-layout">
      <section className="panel download-card" aria-busy={status === 'loading'}>
        <div className="download-platform"><Icon name="windows" size={30} /><span>Windows 10 / 11<small>{t('64-bit · Portable ZIP')}</small></span><span className="release-badge">{latest ? latest.prerelease ? /-rc\./.test(latest.version) ? 'RC' : 'BETA' : t('Stable') : 'BETA'}</span></div>
        <div className="release-status" aria-live="polite">
          {status === 'loading' ? <><h2>{t('Checking releases…')}</h2><p>{t('Getting the latest build from GitHub.')}</p></>
            : status === 'error' ? <><h2>{t('Releases are temporarily unavailable')}</h2><p>{t('Check GitHub Releases directly, or try again.')}</p><button className="text-button" onClick={() => setAttempt(value => value + 1)}>{t('Try again')}</button></>
              : latest ? <><h2 className="download-version">{latest.version}</h2><p>{size(latest.download.bytes)}<span className="release-divider">·</span>{date(latest.published, locale)}</p><a className="button primary download-button" href={latest.download.url}><Icon name="download" />{t('Download for Windows')}</a><div className="release-links"><a href={latest.page} target="_blank" rel="noopener noreferrer">{t('Release notes')} ↗</a>{latest.checksum && <a href={latest.checksum.url}>{t('SHA-256 checksum')}</a>}</div>{latest.download.digest && <details className="checksum"><summary>SHA-256</summary><code>{latest.download.digest}</code></details>}</>
                : <><h2>{t('No downloadable builds yet')}</h2><p>{t('LazerRave is in Beta. Downloadable builds will appear here when published on GitHub Releases.')}</p></>}
        </div>
        <a className="button secondary" href={releasesUrl} target="_blank" rel="noopener noreferrer">{t('All releases')} ↗</a>
      </section>
      <section className="panel download-guide"><h2>{t('Start playing')}</h2><ol>
        <li><strong>{t('Extract the ZIP')}</strong><p>{t('Extract the complete folder to a location you can write to.')}</p></li>
        <li><strong>{t('Open LazerRave.exe')}</strong><p>{t('Keep the extracted files together. No separate .NET installation is needed.')}</p></li>
        <li><strong>{t('Add your songs')}</strong><p>{t('Place your BMS songs in BMS, or choose a library folder in Settings.')}</p></li>
      </ol><Link to="/packs">{t('Browse song packs ↗')}</Link></section>
    </div>
    {releases.length > 1 && <section className="panel release-history"><h2>{t('Previous versions')}</h2>{releases.slice(1, 6).map(release => <a key={release.page} href={release.page} target="_blank" rel="noopener noreferrer"><span>{release.version}<small>{date(release.published, locale)}</small></span><span>{t('View release')} ↗</span></a>)}</section>}
    <section className="download-source"><Icon name="code" size={26} /><div><h2>{t('Follow the project')}</h2><p>{t('Source code, issues and development updates on GitHub.')}</p></div><a className="button secondary" href={repositoryUrl} target="_blank" rel="noopener noreferrer">GitHub ↗</a></section>
  </>;
}
