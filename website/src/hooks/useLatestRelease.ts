import { useEffect, useState } from 'react';

export interface ReleaseInfo {
  version: string;
  downloadUrl: string;
  releaseUrl: string;
  assetName: string;
  isLive: boolean;
}

const FALLBACK_RELEASE: ReleaseInfo = {
  version: 'v0.8.0',
  downloadUrl: 'https://github.com/zero1-spec/wallpaper-picker/releases/latest',
  releaseUrl: 'https://github.com/zero1-spec/wallpaper-picker/releases/latest',
  assetName: 'WallpaperSetup-0.8.0.exe',
  isLive: false,
};

export function useLatestRelease(): ReleaseInfo {
  const [release, setRelease] = useState<ReleaseInfo>(FALLBACK_RELEASE);

  useEffect(() => {
    const controller = new AbortController();

    async function fetchRelease() {
      try {
        const res = await fetch(
          'https://api.github.com/repos/zero1-spec/wallpaper-picker/releases/latest',
          {
            signal: controller.signal,
            headers: { Accept: 'application/vnd.github+json' },
          }
        );
        if (!res.ok) return;
        const data = await res.json();
        const tagName: string = data?.tag_name || FALLBACK_RELEASE.version;
        const htmlUrl: string = data?.html_url || FALLBACK_RELEASE.releaseUrl;
        const assets: Array<{ name?: string; browser_download_url?: string }> = Array.isArray(
          data?.assets
        )
          ? data.assets
          : [];

        const exeAsset = assets.find(
          (a) =>
            typeof a.name === 'string' &&
            a.name.toLowerCase().endsWith('.exe') &&
            typeof a.browser_download_url === 'string'
        );

        setRelease({
          version: tagName.startsWith('v') ? tagName : `v${tagName}`,
          downloadUrl: exeAsset?.browser_download_url || htmlUrl,
          releaseUrl: htmlUrl,
          assetName: exeAsset?.name || `WallpaperSetup-${tagName.replace(/^v/, '')}.exe`,
          isLive: true,
        });
      } catch {
        // Graceful fallback if offline, rate-limited, or aborted
      }
    }

    fetchRelease();
    return () => controller.abort();
  }, []);

  return release;
}
