export const repositoryUrl = 'https://github.com/feecat/LazerRave';
export const releasesUrl = `${repositoryUrl}/releases`;
export const releasesApi = 'https://api.github.com/repos/feecat/LazerRave/releases?per_page=20';

export interface ReleaseAsset { name: string; url: string; bytes: number; digest?: string }
export interface DesktopRelease {
  version: string; page: string; published: string; prerelease: boolean;
  download: ReleaseAsset; checksum?: ReleaseAsset;
}

function repositoryLink(value: unknown, download = false): value is string {
  if (typeof value !== 'string') return false;
  try {
    const url = new URL(value);
    return url.protocol === 'https:' && url.hostname === 'github.com' && !url.username && !url.password
      && url.pathname.startsWith(`/feecat/LazerRave/releases/${download ? 'download/' : 'tag/'}`);
  } catch { return false; }
}

export function selectReleases(data: unknown): DesktopRelease[] {
  if (!Array.isArray(data)) throw new Error('Invalid release response.');
  return data.flatMap((release): DesktopRelease[] => {
    if (!release || release.draft || !repositoryLink(release.html_url) || typeof release.tag_name !== 'string'
      || typeof release.published_at !== 'string' || !Number.isFinite(Date.parse(release.published_at)) || !Array.isArray(release.assets)) return [];
    const assets: ReleaseAsset[] = release.assets.flatMap((asset: Record<string, unknown>) => {
      if (!asset || typeof asset.name !== 'string' || !repositoryLink(asset.browser_download_url, true)
        || typeof asset.size !== 'number' || asset.size <= 0) return [];
      return [{ name: asset.name, url: asset.browser_download_url, bytes: asset.size,
        digest: typeof asset.digest === 'string' && /^sha256:[a-f0-9]{64}$/i.test(asset.digest) ? asset.digest.slice(7) : undefined }];
    });
    const version = release.tag_name.replace(/^v/, '');
    const download = assets.find(asset => asset.name === `LazerRave-${version}-win-x64.zip`);
    if (!download) return [];
    return [{ version, page: release.html_url, published: release.published_at,
      prerelease: Boolean(release.prerelease) || /-(beta|rc)\./.test(version), download,
      checksum: assets.find(asset => asset.name === `${download.name}.sha256`) }];
  }).sort((a, b) => Date.parse(b.published) - Date.parse(a.published));
}
