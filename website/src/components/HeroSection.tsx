import { motion } from 'framer-motion';
import { Download, Github, Cpu, HardDrive, ShieldOff, UserCheck } from 'lucide-react';
import { ReleaseInfo } from '../hooks/useLatestRelease';
import { MagneticButton } from './MagneticButton';
import { PickerPlayground } from './PickerPlayground';
import { useAccent } from '../context/AccentContext';

interface HeroSectionProps {
  release: ReleaseInfo;
}

const STAT_PILLS = [
  { icon: HardDrive, text: '~13 MB RAM' },
  { icon: Cpu, text: '0% Idle CPU' },
  { icon: ShieldOff, text: 'Offline-First' },
  { icon: UserCheck, text: 'No Admin Required' },
];

export function HeroSection({ release }: HeroSectionProps) {
  const { activeWallpaper, accentColor } = useAccent();

  return (
    <section className="relative overflow-hidden px-4 pb-20 pt-28 sm:px-6 sm:pt-34">
      {/* Reactive Ambient Frosted Backdrop Glow */}
      <div
        aria-hidden="true"
        className="pointer-events-none absolute left-1/2 top-0 -z-10 h-[760px] w-[1200px] -translate-x-1/2 opacity-45 blur-[130px] transition-all duration-700"
        style={{
          background: `radial-gradient(ellipse at 50% 25%, rgba(var(--accent-rgb), 0.42) 0%, rgba(var(--accent-secondary-rgb), 0.18) 45%, transparent 75%)`,
        }}
      />

      <div className="mx-auto max-w-6xl text-center">
        {/* Top Live Status Pill */}
        <motion.div
          initial={{ opacity: 0, y: 12 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.4 }}
          className="inline-flex flex-wrap items-center justify-center gap-2 rounded-full border border-white/10 bg-white/[0.04] px-3.5 py-1.5 text-xs text-zinc-300 backdrop-blur-md"
        >
          <span
            className="h-2 w-2 rounded-full transition-colors duration-500"
            style={{ backgroundColor: accentColor, boxShadow: `0 0 12px ${accentColor}` }}
          />
          <span className="font-mono text-zinc-200">.NET 10 Native Windows</span>
          <span className="text-zinc-600">&bull;</span>
          <span className="text-zinc-400">
            Synced to <span className="text-white font-medium">{activeWallpaper.title}</span>
          </span>
        </motion.div>

        {/* Hero Headline */}
        <motion.h1
          initial={{ opacity: 0, y: 16 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.5, delay: 0.06 }}
          className="mx-auto mt-6 max-w-4xl font-display text-4xl font-bold tracking-tight text-white sm:text-6xl lg:text-[4.5rem] lg:leading-[1.03]"
        >
          Desktop Wallpapers.{' '}
          <span
            className="bg-clip-text text-transparent transition-all duration-500"
            style={{
              backgroundImage: `linear-gradient(125deg, #ffffff 10%, var(--accent) 55%, var(--accent-secondary) 95%)`,
            }}
          >
            Zero Bloat.
          </span>
        </motion.h1>

        {/* Subheadline & Telemetry Pills */}
        <motion.p
          initial={{ opacity: 0, y: 16 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.5, delay: 0.12 }}
          className="mx-auto mt-5 max-w-2xl text-base text-zinc-400 sm:text-lg"
        >
          A silent tray daemon that schedules, syncs, and powers live wallpapers on Windows—paired
          with an instant WPF picker that opens on hotkey and exits without staying resident.
        </motion.p>

        <motion.div
          initial={{ opacity: 0, y: 14 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.45, delay: 0.16 }}
          className="mt-6 flex flex-wrap items-center justify-center gap-2"
        >
          {STAT_PILLS.map(({ icon: Icon, text }) => (
            <span
              key={text}
              className="inline-flex items-center gap-1.5 rounded-full border border-white/10 bg-white/[0.03] px-3 py-1 font-mono text-xs text-zinc-300"
            >
              <Icon className="h-3.5 w-3.5 text-[var(--accent)] transition-colors duration-300" />
              {text}
            </span>
          ))}
        </motion.div>

        {/* Primary & Secondary CTAs */}
        <motion.div
          initial={{ opacity: 0, y: 16 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.5, delay: 0.22 }}
          className="mt-8 flex flex-wrap items-center justify-center gap-3.5"
        >
          <MagneticButton
            href={release.downloadUrl}
            target="_blank"
            rel="noreferrer"
            className="inline-flex items-center gap-2.5 rounded-2xl px-6 py-3.5 text-sm font-semibold text-zinc-950 transition-all duration-300"
            style={{
              background: 'linear-gradient(135deg, #ffffff 0%, #e4e4e7 100%)',
              boxShadow:
                '0 0 36px -6px rgba(var(--accent-rgb), 0.7), inset 0 1px 0 rgba(255,255,255,0.9)',
            }}
          >
            <Download className="h-4 w-4" />
            <span>Download {release.version} for Windows</span>
          </MagneticButton>

          <MagneticButton
            href="https://github.com/zero1-spec/wallpaper-picker"
            target="_blank"
            rel="noreferrer"
            className="inline-flex items-center gap-2 rounded-2xl border border-white/12 bg-white/[0.04] px-6 py-3.5 text-sm font-medium text-zinc-200 backdrop-blur-md transition hover:border-white/25 hover:bg-white/[0.08] hover:text-white"
          >
            <Github className="h-4 w-4" />
            <span>View on GitHub</span>
          </MagneticButton>
        </motion.div>

        {/* Interactive Picker Playground */}
        <motion.div
          initial={{ opacity: 0, y: 24 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.6, delay: 0.28 }}
        >
          <PickerPlayground />
        </motion.div>
      </div>
    </section>
  );
}
