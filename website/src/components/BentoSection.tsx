import { useState } from 'react';
import { motion } from 'framer-motion';
import {
  Activity,
  Monitor,
  Palette,
  PauseCircle,
  Volume2,
  VolumeX,
  Gamepad2,
  BatteryCharging,
  Copy,
  Check,
  Layers,
} from 'lucide-react';
import { SpotlightCard } from './SpotlightCard';
import { useAccent } from '../context/AccentContext';
import { BUILTIN_WALLPAPERS } from '../data/wallpapers';

type ExportFormat = 'css' | 'json' | 'toml';

export function BentoSection() {
  const { activeWallpaper, palette, selectWallpaper, selectSwatch, showToast } = useAccent();

  // Smart Pause Simulator state
  const [fullscreenGame, setFullscreenGame] = useState(false);
  const [batterySaver, setBatterySaver] = useState(false);
  const [muted, setMuted] = useState(true);
  const [volume, setVolume] = useState(35);

  const isPaused = fullscreenGame || batterySaver;
  const gpuUsage = isPaused ? 0 : 21;

  // Palette Export state
  const [exportTab, setExportTab] = useState<ExportFormat>('css');
  const [copiedCode, setCopiedCode] = useState(false);

  // Multi-Monitor state
  const [monitorMode, setMonitorMode] = useState<'same' | 'per-monitor'>('per-monitor');
  const [mon1Wp, setMon1Wp] = useState(BUILTIN_WALLPAPERS[1]);
  const [mon2Wp, setMon2Wp] = useState(BUILTIN_WALLPAPERS[2]);
  const [selectedDisplay, setSelectedDisplay] = useState<1 | 2>(1);

  const exportSnippet = (() => {
    if (exportTab === 'css') {
      return `:root {\n  --ws-primary: ${palette.primary};\n  --ws-secondary: ${palette.secondary};\n  --ws-tertiary: ${palette.tertiary};\n  --ws-container: ${palette.container};\n  --ws-surface: ${palette.surface};\n}`;
    }
    if (exportTab === 'json') {
      return JSON.stringify(
        {
          wallpaper: activeWallpaper.filename,
          palette: {
            primary: palette.primary,
            secondary: palette.secondary,
            tertiary: palette.tertiary,
            container: palette.container,
            surface: palette.surface,
          },
        },
        null,
        2
      );
    }
    return `[palette]\nsource = "${activeWallpaper.filename}"\nprimary = "${palette.primary}"\nsecondary = "${palette.secondary}"\ntertiary = "${palette.tertiary}"\ncontainer = "${palette.container}"\nsurface = "${palette.surface}"`;
  })();

  const handleCopyCode = () => {
    navigator.clipboard?.writeText(exportSnippet).catch(() => {});
    setCopiedCode(true);
    showToast(`Copied palette.${exportTab}`);
    setTimeout(() => setCopiedCode(false), 1400);
  };

  const assignMonitorWallpaper = (wp: (typeof BUILTIN_WALLPAPERS)[number]) => {
    if (monitorMode === 'same') {
      setMon1Wp(wp);
      setMon2Wp(wp);
      showToast(`Applied "${wp.title}" to all displays`);
    } else if (selectedDisplay === 1) {
      setMon1Wp(wp);
      showToast(`Display 1 set to "${wp.title}"`);
    } else {
      setMon2Wp(wp);
      showToast(`Display 2 set to "${wp.title}"`);
    }
    selectWallpaper(wp);
  };

  return (
    <section id="performance" className="relative px-4 py-20 sm:px-6">
      <div className="mx-auto max-w-6xl">
        {/* Section Heading */}
        <motion.div
          initial={{ opacity: 0, y: 18 }}
          whileInView={{ opacity: 1, y: 0 }}
          viewport={{ once: true, amount: 0.3 }}
          transition={{ duration: 0.45 }}
          className="mb-12 max-w-2xl"
        >
          <span className="inline-flex items-center gap-2 rounded-full border border-white/10 bg-white/[0.04] px-3 py-1 font-mono text-[11px] uppercase tracking-wider text-[var(--accent)]">
            <Activity className="h-3.5 w-3.5" />
            Architecture &amp; Telemetry
          </span>
          <h2 className="mt-4 font-display text-3xl font-bold text-white sm:text-4xl">
            Engineered to stay invisible until you summon it.
          </h2>
          <p className="mt-3 text-sm text-zinc-400 sm:text-base">
            Built directly on native Windows APIs and <code className="text-zinc-200">.NET 10</code>{' '}
            with zero third-party runtime packages. No Electron shell, no local HTTP server, no background polling.
          </p>
        </motion.div>

        {/* Asymmetric Bento Grid */}
        <div className="grid grid-cols-1 gap-4 md:grid-cols-6">
          {/* Card 1: ~13 MB Idle RAM / 0% Idle CPU */}
          <SpotlightCard className="p-6 md:col-span-3">
            <div className="flex items-start justify-between gap-4">
              <div>
                <p className="font-mono text-xs uppercase tracking-wider text-zinc-400">
                  Daemon Footprint
                </p>
                <h3 className="mt-1 font-display text-2xl font-bold text-white">
                  ~13 MB RAM &bull; 0% Idle CPU
                </h3>
              </div>
              <span
                className="rounded-xl border px-2.5 py-1 font-mono text-xs font-semibold"
                style={{
                  borderColor: 'rgba(var(--accent-rgb), 0.35)',
                  backgroundColor: 'rgba(var(--accent-rgb), 0.12)',
                  color: 'var(--accent)',
                }}
              >
                No Polling
              </span>
            </div>

            <p className="mt-2.5 text-sm text-zinc-400">
              <code className="text-zinc-200">walld.exe</code> sleeps until the next timer boundary, disables concurrent/server GC, and talks over <code className="text-zinc-200">WM_COPYDATA</code> IPC.
            </p>

            {/* Telemetry Comparison Bars */}
            <div className="mt-6 space-y-4">
              <div>
                <div className="mb-1.5 flex items-center justify-between text-xs">
                  <span className="font-semibold text-white">WallpaperSuite (walld.exe)</span>
                  <span className="font-mono text-[var(--accent)]">~12.7 MB &bull; 0.0% CPU</span>
                </div>
                <div className="h-3 w-full overflow-hidden rounded-full bg-white/[0.06] p-0.5">
                  <motion.div
                    initial={{ width: 0 }}
                    whileInView={{ width: '5%' }}
                    viewport={{ once: true }}
                    transition={{ duration: 0.9, ease: [0.22, 1, 0.36, 1] }}
                    className="h-full min-w-[22px] rounded-full"
                    style={{
                      backgroundColor: 'var(--accent)',
                      boxShadow: '0 0 16px rgba(var(--accent-rgb), 0.8)',
                    }}
                  />
                </div>
              </div>

              <div>
                <div className="mb-1.5 flex items-center justify-between text-xs text-zinc-400">
                  <span>Typical WebView / CEF Wallpaper Engine</span>
                  <span className="font-mono text-zinc-500">~340 MB &bull; 2–6% CPU</span>
                </div>
                <div className="h-3 w-full overflow-hidden rounded-full bg-white/[0.06] p-0.5">
                  <motion.div
                    initial={{ width: 0 }}
                    whileInView={{ width: '82%' }}
                    viewport={{ once: true }}
                    transition={{ duration: 0.9, delay: 0.1, ease: [0.22, 1, 0.36, 1] }}
                    className="h-full rounded-full bg-zinc-600/70"
                  />
                </div>
              </div>

              <div>
                <div className="mb-1.5 flex items-center justify-between text-xs text-zinc-400">
                  <span>Electron / Chromium Background Host</span>
                  <span className="font-mono text-zinc-500">~460 MB &bull; 3–8% CPU</span>
                </div>
                <div className="h-3 w-full overflow-hidden rounded-full bg-white/[0.06] p-0.5">
                  <motion.div
                    initial={{ width: 0 }}
                    whileInView={{ width: '96%' }}
                    viewport={{ once: true }}
                    transition={{ duration: 0.9, delay: 0.2, ease: [0.22, 1, 0.36, 1] }}
                    className="h-full rounded-full bg-zinc-700/60"
                  />
                </div>
              </div>
            </div>
          </SpotlightCard>

          {/* Card 2: Live Wallpaper + Smart Pause Simulator */}
          <SpotlightCard className="p-6 md:col-span-3">
            <div className="flex items-start justify-between gap-3">
              <div>
                <p className="font-mono text-xs uppercase tracking-wider text-zinc-400">
                  wallvideo.exe &bull; Smart Pause
                </p>
                <h3 className="mt-1 font-display text-2xl font-bold text-white">
                  Live Video + Smart Pause Simulator
                </h3>
              </div>
              <span
                className={`inline-flex items-center gap-1.5 rounded-full border px-2.5 py-1 font-mono text-[11px] font-semibold transition-all ${
                  isPaused
                    ? 'border-amber-400/40 bg-amber-400/15 text-amber-300'
                    : 'border-emerald-400/35 bg-emerald-400/10 text-emerald-300'
                }`}
              >
                <span
                  className={`h-1.5 w-1.5 rounded-full ${
                    isPaused ? 'bg-amber-400' : 'bg-emerald-400 animate-pulse'
                  }`}
                />
                {isPaused ? 'PAUSED' : 'PLAYING'}
              </span>
            </div>

            <p className="mt-2 text-sm text-zinc-400">
              Flip either trigger below to see how WallpaperSuite automatically freezes playback and frees 100% of video GPU load.
            </p>

            {/* Interactive Controls */}
            <div className="mt-5 grid grid-cols-1 gap-2.5 sm:grid-cols-2">
              <button
                type="button"
                onClick={() => setFullscreenGame((v) => !v)}
                className={`flex items-center justify-between rounded-xl border px-3.5 py-2.5 text-left text-xs transition ${
                  fullscreenGame
                    ? 'border-amber-400/50 bg-amber-400/10 text-white'
                    : 'border-white/10 bg-white/[0.03] text-zinc-300 hover:bg-white/[0.06]'
                }`}
              >
                <span className="flex items-center gap-2 font-medium">
                  <Gamepad2 className="h-4 w-4 text-amber-400" />
                  Fullscreen Game
                </span>
                <span
                  className={`rounded-full px-2 py-0.5 font-mono text-[10px] font-semibold ${
                    fullscreenGame ? 'bg-amber-400 text-zinc-950' : 'bg-white/10 text-zinc-400'
                  }`}
                >
                  {fullscreenGame ? 'ON' : 'OFF'}
                </span>
              </button>

              <button
                type="button"
                onClick={() => setBatterySaver((v) => !v)}
                className={`flex items-center justify-between rounded-xl border px-3.5 py-2.5 text-left text-xs transition ${
                  batterySaver
                    ? 'border-emerald-400/50 bg-emerald-400/10 text-white'
                    : 'border-white/10 bg-white/[0.03] text-zinc-300 hover:bg-white/[0.06]'
                }`}
              >
                <span className="flex items-center gap-2 font-medium">
                  <BatteryCharging className="h-4 w-4 text-emerald-400" />
                  Battery Saver
                </span>
                <span
                  className={`rounded-full px-2 py-0.5 font-mono text-[10px] font-semibold ${
                    batterySaver ? 'bg-emerald-400 text-zinc-950' : 'bg-white/10 text-zinc-400'
                  }`}
                >
                  {batterySaver ? 'ON' : 'OFF'}
                </span>
              </button>
            </div>

            {/* Simulated GPU Meter & Audio Controls */}
            <div className="mt-4 rounded-xl border border-white/10 bg-black/35 p-3.5">
              <div className="flex items-center justify-between text-xs">
                <span className="flex items-center gap-1.5 text-zinc-300">
                  <PauseCircle className="h-3.5 w-3.5 text-zinc-400" />
                  Simulated Video GPU Load
                </span>
                <span className="font-mono text-sm font-bold text-white transition-all">
                  {gpuUsage}%
                </span>
              </div>
              <div className="mt-2 h-2.5 w-full overflow-hidden rounded-full bg-white/10">
                <motion.div
                  animate={{ width: `${gpuUsage === 0 ? 0 : 62}%` }}
                  transition={{ type: 'spring', stiffness: 180, damping: 24 }}
                  className="h-full rounded-full"
                  style={{
                    backgroundColor: isPaused ? '#fbbf24' : 'var(--accent)',
                  }}
                />
              </div>

              {/* Audio Mute & Volume Slider */}
              <div className="mt-3 flex items-center gap-3 border-t border-white/[0.06] pt-3">
                <button
                  type="button"
                  onClick={() => setMuted((m) => !m)}
                  className="rounded-lg border border-white/10 bg-white/5 p-1.5 text-zinc-300 hover:text-white"
                  aria-label="Toggle live wallpaper mute"
                >
                  {muted || volume === 0 ? (
                    <VolumeX className="h-3.5 w-3.5 text-zinc-400" />
                  ) : (
                    <Volume2 className="h-3.5 w-3.5 text-[var(--accent)]" />
                  )}
                </button>
                <input
                  type="range"
                  min={0}
                  max={100}
                  value={muted ? 0 : volume}
                  onChange={(e) => {
                    const val = Number(e.target.value);
                    setVolume(val);
                    if (val > 0 && muted) setMuted(false);
                  }}
                  aria-label="Live wallpaper volume"
                  className="h-1.5 flex-1 cursor-pointer accent-[var(--accent)]"
                />
                <span className="w-10 text-right font-mono text-[11px] text-zinc-400">
                  {muted ? 'MUTE' : `${volume}%`}
                </span>
              </div>
            </div>
          </SpotlightCard>

          {/* Card 3: Palette Export & Accent Sync */}
          <SpotlightCard className="p-6 md:col-span-2">
            <div className="flex items-center gap-2 font-mono text-xs uppercase tracking-wider text-zinc-400">
              <Palette className="h-3.5 w-3.5 text-[var(--accent)]" />
              Palette Export
            </div>
            <h3 className="mt-1 font-display text-xl font-bold text-white">
              Sync &amp; Export Swatches
            </h3>

            {/* 5-color swatch strip */}
            <div className="mt-4 grid grid-cols-5 gap-1.5">
              {Object.entries(palette).map(([role, hex]) => (
                <button
                  key={role}
                  type="button"
                  onClick={() => {
                    selectSwatch(hex);
                    navigator.clipboard?.writeText(hex).catch(() => {});
                  }}
                  className="group flex flex-col items-center gap-1 rounded-xl border border-white/10 bg-black/30 p-1.5 transition hover:border-white/25"
                  title={`${role.toUpperCase()}: ${hex}`}
                >
                  <span
                    className="h-6 w-full rounded-lg border border-white/15 transition-transform group-hover:scale-105"
                    style={{ backgroundColor: hex }}
                  />
                  <span className="font-mono text-[9px] text-zinc-400 group-hover:text-white">
                    {hex.slice(1)}
                  </span>
                </button>
              ))}
            </div>

            {/* Code Export Tabs */}
            <div className="mt-4 overflow-hidden rounded-xl border border-white/10 bg-[#07080d]">
              <div className="flex items-center justify-between border-b border-white/[0.07] px-3 py-1.5">
                <div className="flex gap-1">
                  {(['css', 'json', 'toml'] as ExportFormat[]).map((fmt) => (
                    <button
                      key={fmt}
                      type="button"
                      onClick={() => setExportTab(fmt)}
                      className={`rounded-md px-2 py-0.5 font-mono text-[10px] uppercase transition ${
                        exportTab === fmt
                          ? 'bg-white/15 text-white'
                          : 'text-zinc-500 hover:text-zinc-300'
                      }`}
                    >
                      {fmt}
                    </button>
                  ))}
                </div>
                <button
                  type="button"
                  onClick={handleCopyCode}
                  className="inline-flex items-center gap-1 font-mono text-[10px] text-zinc-400 hover:text-white"
                >
                  {copiedCode ? <Check className="h-3 w-3 text-emerald-400" /> : <Copy className="h-3 w-3" />}
                  {copiedCode ? 'Copied' : 'Copy'}
                </button>
              </div>
              <pre className="max-h-36 overflow-auto p-3 font-mono text-[11px] leading-relaxed text-zinc-300">
                <code>{exportSnippet}</code>
              </pre>
            </div>
          </SpotlightCard>

          {/* Card 4: Multi-Monitor Control */}
          <SpotlightCard className="p-6 md:col-span-2">
            <div className="flex items-center justify-between gap-2">
              <div className="flex items-center gap-2 font-mono text-xs uppercase tracking-wider text-zinc-400">
                <Monitor className="h-3.5 w-3.5 text-[var(--accent)]" />
                IDesktopWallpaper
              </div>
              <div className="inline-flex rounded-lg border border-white/10 bg-black/40 p-0.5 text-[10px]">
                <button
                  type="button"
                  onClick={() => setMonitorMode('same')}
                  className={`rounded-md px-2 py-0.5 font-medium transition ${
                    monitorMode === 'same' ? 'bg-white/15 text-white' : 'text-zinc-400'
                  }`}
                >
                  Same
                </button>
                <button
                  type="button"
                  onClick={() => setMonitorMode('per-monitor')}
                  className={`rounded-md px-2 py-0.5 font-medium transition ${
                    monitorMode === 'per-monitor' ? 'bg-white/15 text-white' : 'text-zinc-400'
                  }`}
                >
                  Per-Monitor
                </button>
              </div>
            </div>

            <h3 className="mt-1 font-display text-xl font-bold text-white">
              Dual-Screen Native COM
            </h3>
            <p className="mt-1 text-xs text-zinc-400">
              Click a display below, then pick a preset to test per-screen assignment.
            </p>

            {/* Dual Monitor Mockup */}
            <div className="mt-4 grid grid-cols-2 gap-2.5">
              {[
                { id: 1 as const, label: 'Display 1 • Primary', wp: mon1Wp },
                { id: 2 as const, label: 'Display 2 • Side', wp: monitorMode === 'same' ? mon1Wp : mon2Wp },
              ].map((screen) => {
                const active = monitorMode === 'per-monitor' && selectedDisplay === screen.id;
                return (
                  <button
                    key={screen.id}
                    type="button"
                    onClick={() => setSelectedDisplay(screen.id)}
                    className="group text-left"
                  >
                    <div
                      className={`relative aspect-video overflow-hidden rounded-xl border-2 transition ${
                        active
                          ? 'border-[var(--accent)] shadow-[0_0_20px_-6px_rgba(var(--accent-rgb),0.7)]'
                          : 'border-white/15 hover:border-white/30'
                      }`}
                    >
                      <img
                        src={screen.wp.src}
                        alt={screen.wp.title}
                        className="h-full w-full object-cover"
                      />
                      <span className="absolute bottom-1 left-1.5 rounded bg-black/70 px-1.5 py-0.5 font-mono text-[9px] text-white backdrop-blur">
                        #{screen.id}
                      </span>
                    </div>
                    <div className="mx-auto mt-1 h-1 w-8 rounded-full bg-white/15" />
                    <p className="mt-1 truncate text-center font-mono text-[10px] text-zinc-400">
                      {screen.label}
                    </p>
                  </button>
                );
              })}
            </div>

            {/* Quick Assign Strip */}
            <div className="mt-3 flex items-center justify-between gap-1.5">
              {BUILTIN_WALLPAPERS.slice(0, 5).map((wp) => (
                <button
                  key={wp.id}
                  type="button"
                  onClick={() => assignMonitorWallpaper(wp)}
                  className="h-7 flex-1 overflow-hidden rounded-lg border border-white/15 transition hover:scale-105 hover:border-white/40"
                  title={`Assign ${wp.title}`}
                >
                  <img src={wp.src} alt={wp.title} className="h-full w-full object-cover" />
                </button>
              ))}
            </div>
          </SpotlightCard>

          {/* Card 5: 16 Built-in Wallpapers Mosaic */}
          <SpotlightCard className="p-6 md:col-span-2">
            <div className="flex items-center gap-2 font-mono text-xs uppercase tracking-wider text-zinc-400">
              <Layers className="h-3.5 w-3.5 text-[var(--accent)]" />
              Instant Ready
            </div>
            <h3 className="mt-1 font-display text-xl font-bold text-white">
              16 Built-in 4K Wallpapers
            </h3>
            <p className="mt-1 text-xs text-zinc-400">
              Space, Nature, Night &amp; Abstract bundled out-of-the-box. Click any tile to sync.
            </p>

            <div className="mt-4 grid grid-cols-4 gap-1.5">
              {BUILTIN_WALLPAPERS.map((wp) => {
                const isSelected = wp.id === activeWallpaper.id;
                return (
                  <button
                    key={wp.id}
                    type="button"
                    onClick={() => selectWallpaper(wp)}
                    className={`relative aspect-video overflow-hidden rounded-lg border transition ${
                      isSelected
                        ? 'border-[var(--accent)] ring-1 ring-[var(--accent)] scale-105 z-10'
                        : 'border-white/10 opacity-80 hover:opacity-100 hover:border-white/30'
                    }`}
                    title={wp.title}
                  >
                    <img
                      src={wp.src}
                      alt={wp.title}
                      loading="lazy"
                      className="h-full w-full object-cover"
                    />
                  </button>
                );
              })}
            </div>
          </SpotlightCard>
        </div>
      </div>
    </section>
  );
}
