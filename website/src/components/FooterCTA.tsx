import { Download, Github, ShieldAlert, Terminal, ExternalLink } from 'lucide-react';
import { ReleaseInfo } from '../hooks/useLatestRelease';
import { MagneticButton } from './MagneticButton';

interface FooterCTAProps {
  release: ReleaseInfo;
}

export function FooterCTA({ release }: FooterCTAProps) {
  return (
    <footer id="install" className="relative px-4 pb-16 pt-14 sm:px-6">
      <div className="mx-auto max-w-6xl">
        {/* Big Glowing Glass CTA Card */}
        <div className="glass-panel relative overflow-hidden rounded-3xl px-6 py-12 text-center sm:px-12 sm:py-16">
          <div
            aria-hidden="true"
            className="pointer-events-none absolute inset-x-0 -top-24 mx-auto h-64 w-3/4 rounded-full opacity-35 blur-[100px]"
            style={{
              background:
                'radial-gradient(circle, rgba(var(--accent-rgb), 0.7) 0%, transparent 70%)',
            }}
          />

          <div className="relative z-10 mx-auto max-w-2xl">
            <div className="inline-flex flex-wrap items-center justify-center gap-2 rounded-full border border-white/10 bg-white/[0.04] px-3.5 py-1 font-mono text-[11px] text-zinc-300">
              <span>Windows 11 (tested) &bull; Windows 10 (1607+)</span>
              <span className="text-zinc-600">|</span>
              <span>Self-contained .NET 10</span>
            </div>

            <h2 className="mt-5 font-display text-3xl font-bold text-white sm:text-5xl">
              Install in seconds. Keep your RAM forever.
            </h2>

            <p className="mt-4 text-sm text-zinc-400 sm:text-base">
              Per-user installer (<code className="text-zinc-200">%LOCALAPPDATA%\Programs\WallpaperSuite</code>)—no administrator rights and no separate .NET install required.
            </p>

            <div className="mt-8 flex flex-wrap items-center justify-center gap-3.5">
              <MagneticButton
                href={release.downloadUrl}
                target="_blank"
                rel="noreferrer"
                className="inline-flex items-center gap-2.5 rounded-2xl px-7 py-4 text-sm font-semibold text-zinc-950"
                style={{
                  background: 'linear-gradient(135deg, #ffffff 0%, #e4e4e7 100%)',
                  boxShadow:
                    '0 0 40px -6px rgba(var(--accent-rgb), 0.75), inset 0 1px 0 rgba(255,255,255,0.9)',
                }}
              >
                <Download className="h-4 w-4" />
                <span>Download {release.assetName}</span>
              </MagneticButton>

              <a
                href={release.releaseUrl}
                target="_blank"
                rel="noreferrer"
                className="inline-flex items-center gap-2 rounded-2xl border border-white/12 bg-white/[0.04] px-5 py-4 text-sm font-medium text-zinc-200 transition hover:border-white/25 hover:bg-white/[0.08] hover:text-white"
              >
                <span>Release Notes ({release.version})</span>
                <ExternalLink className="h-3.5 w-3.5" />
              </a>
            </div>

            {/* SmartScreen Transparency Note */}
            <div className="mx-auto mt-7 max-w-xl rounded-2xl border border-amber-400/20 bg-amber-400/[0.06] px-4 py-3 text-left text-xs text-amber-200/90">
              <div className="flex items-start gap-2.5">
                <ShieldAlert className="mt-0.5 h-4 w-4 shrink-0 text-amber-400" />
                <p>
                  <strong className="font-semibold text-amber-100">SmartScreen note:</strong>{' '}
                  Unsigned open-source installer — if Windows SmartScreen prompts, click{' '}
                  <span className="font-semibold underline decoration-amber-400/50">
                    More info → Run anyway
                  </span>
                  , or verify the <code className="font-mono">.sha256</code> checksum or build from source with{' '}
                  <code className="font-mono">dotnet build</code>.
                </p>
              </div>
            </div>
          </div>
        </div>

        {/* Clean Footer */}
        <div className="mt-10 flex flex-col items-center justify-between gap-4 border-t border-white/[0.07] pt-8 text-xs text-zinc-500 sm:flex-row">
          <div className="flex items-center gap-2.5">
            <img src="./icon.png" alt="" className="h-5 w-5 rounded-md" />
            <span className="font-semibold text-zinc-300">WallpaperSuite</span>
            <span>&bull;</span>
            <span>MIT License</span>
            <span>&bull;</span>
            <span className="font-mono">{release.version}</span>
          </div>

          <div className="flex flex-wrap items-center gap-5">
            <a
              href="https://github.com/zero1-spec/wallpaper-picker"
              target="_blank"
              rel="noreferrer"
              className="inline-flex items-center gap-1.5 text-zinc-400 transition hover:text-white"
            >
              <Github className="h-3.5 w-3.5" />
              GitHub Repository
            </a>
            <a
              href="https://github.com/zero1-spec/wallpaper-picker/releases/latest"
              target="_blank"
              rel="noreferrer"
              className="inline-flex items-center gap-1.5 text-zinc-400 transition hover:text-white"
            >
              <Download className="h-3.5 w-3.5" />
              Releases
            </a>
            <a
              href="https://github.com/zero1-spec/wallpaper-picker/blob/main/README.md#build-from-source"
              target="_blank"
              rel="noreferrer"
              className="inline-flex items-center gap-1.5 text-zinc-400 transition hover:text-white"
            >
              <Terminal className="h-3.5 w-3.5" />
              Build from Source
            </a>
          </div>
        </div>
      </div>
    </footer>
  );
}
