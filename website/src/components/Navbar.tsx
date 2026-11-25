import { useEffect, useState } from 'react';
import { motion } from 'framer-motion';
import { Download, Star, Menu, X } from 'lucide-react';
import { ReleaseInfo } from '../hooks/useLatestRelease';
import { MagneticButton } from './MagneticButton';

interface NavbarProps {
  release: ReleaseInfo;
}

const NAV_LINKS = [
  { label: 'Themes', href: '#themes' },
  { label: 'Features', href: '#features' },
  { label: 'Performance', href: '#performance' },
  { label: 'Privacy', href: '#privacy' },
  { label: 'Install', href: '#install' },
];

export function Navbar({ release }: NavbarProps) {
  const [scrolled, setScrolled] = useState(false);
  const [mobileOpen, setMobileOpen] = useState(false);

  useEffect(() => {
    const onScroll = () => setScrolled(window.scrollY > 24);
    window.addEventListener('scroll', onScroll, { passive: true });
    return () => window.removeEventListener('scroll', onScroll);
  }, []);

  return (
    <header className="fixed inset-x-0 top-0 z-50 flex justify-center px-4 pt-4">
      <motion.nav
        initial={{ y: -24, opacity: 0 }}
        animate={{ y: 0, opacity: 1 }}
        transition={{ type: 'spring', stiffness: 260, damping: 24 }}
        className={`relative flex w-full max-w-6xl items-center justify-between rounded-2xl px-4 py-2.5 transition-all duration-300 ${
          scrolled
            ? 'bg-[#090b12]/85 border border-white/12 shadow-[0_18px_50px_-15px_rgba(0,0,0,0.85)] backdrop-blur-2xl'
            : 'glass-pill'
        }`}
        style={{
          boxShadow: scrolled
            ? 'inset 0 1px 0 rgba(255,255,255,0.08), 0 18px 50px -15px rgba(0,0,0,0.85)'
            : undefined,
        }}
      >
        {/* Left: App Icon + Wordmark + Version Badge */}
        <a href="#" className="group flex items-center gap-2.5">
          <div className="relative flex h-8 w-8 items-center justify-center overflow-hidden rounded-xl border border-white/15 bg-white/5 shadow-inner transition-transform duration-200 group-hover:scale-105">
            <img src="./icon.png" alt="WallpaperSuite icon" className="h-full w-full object-cover" />
          </div>
          <span className="font-display text-base font-bold tracking-tight text-white">
            WallpaperSuite
          </span>
          <span
            className="hidden sm:inline-flex items-center gap-1.5 rounded-full border px-2 py-0.5 font-mono text-[11px] font-medium transition-colors duration-300"
            style={{
              borderColor: 'rgba(var(--accent-rgb), 0.35)',
              backgroundColor: 'rgba(var(--accent-rgb), 0.12)',
              color: 'var(--accent)',
            }}
          >
            <span
              className="h-1.5 w-1.5 rounded-full animate-pulse"
              style={{ backgroundColor: 'var(--accent)' }}
            />
            {release.version}
          </span>
        </a>

        {/* Center: Navigation Links */}
        <div className="hidden md:flex items-center gap-1">
          {NAV_LINKS.map((link) => (
            <a
              key={link.href}
              href={link.href}
              className="rounded-lg px-3 py-1.5 text-sm font-medium text-zinc-400 transition-colors hover:bg-white/[0.05] hover:text-white"
            >
              {link.label}
            </a>
          ))}
        </div>

        {/* Right: GitHub Star + Download CTA */}
        <div className="hidden md:flex items-center gap-2.5">
          <a
            href="https://github.com/zero1-spec/wallpaper-picker"
            target="_blank"
            rel="noreferrer"
            className="inline-flex items-center gap-1.5 rounded-xl border border-white/10 bg-white/[0.03] px-3 py-1.5 text-xs font-medium text-zinc-300 transition-all hover:border-white/20 hover:bg-white/[0.08] hover:text-white"
          >
            <Star className="h-3.5 w-3.5 text-amber-400" />
            <span>Star</span>
          </a>

          <MagneticButton
            href={release.downloadUrl}
            target="_blank"
            rel="noreferrer"
            className="relative inline-flex items-center gap-2 rounded-xl px-4 py-1.5 text-xs font-semibold text-zinc-950 transition-shadow duration-300"
            style={{
              background: `linear-gradient(135deg, #ffffff 0%, #e4e4e7 100%)`,
              boxShadow: `0 0 24px -4px rgba(var(--accent-rgb), 0.55), inset 0 1px 0 rgba(255,255,255,0.9)`,
            }}
          >
            <Download className="h-3.5 w-3.5" />
            <span>Download Free</span>
          </MagneticButton>
        </div>

        {/* Mobile menu button */}
        <button
          type="button"
          onClick={() => setMobileOpen((v) => !v)}
          className="md:hidden rounded-lg border border-white/10 bg-white/5 p-1.5 text-zinc-300 hover:text-white"
          aria-label="Toggle navigation menu"
        >
          {mobileOpen ? <X className="h-5 w-5" /> : <Menu className="h-5 w-5" />}
        </button>

        {/* Mobile Drawer */}
        {mobileOpen && (
          <div className="absolute left-0 right-0 top-full mt-2 rounded-2xl border border-white/10 bg-[#090b12]/95 p-4 shadow-2xl backdrop-blur-2xl md:hidden">
            <div className="flex flex-col gap-1">
              {NAV_LINKS.map((link) => (
                <a
                  key={link.href}
                  href={link.href}
                  onClick={() => setMobileOpen(false)}
                  className="rounded-xl px-3 py-2 text-sm font-medium text-zinc-300 hover:bg-white/5 hover:text-white"
                >
                  {link.label}
                </a>
              ))}
            </div>
            <div className="mt-3 flex items-center gap-2 border-t border-white/10 pt-3">
              <a
                href="https://github.com/zero1-spec/wallpaper-picker"
                target="_blank"
                rel="noreferrer"
                className="flex flex-1 items-center justify-center gap-1.5 rounded-xl border border-white/10 bg-white/5 py-2 text-xs font-medium text-zinc-200"
              >
                <Star className="h-3.5 w-3.5 text-amber-400" />
                Star on GitHub
              </a>
              <a
                href={release.downloadUrl}
                target="_blank"
                rel="noreferrer"
                className="flex flex-1 items-center justify-center gap-1.5 rounded-xl bg-white py-2 text-xs font-semibold text-zinc-950"
              >
                <Download className="h-3.5 w-3.5" />
                Download {release.version}
              </a>
            </div>
          </div>
        )}
      </motion.nav>
    </header>
  );
}
