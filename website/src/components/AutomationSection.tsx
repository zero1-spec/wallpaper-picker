import { useState } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import {
  CloudRain,
  Sun,
  Cloud,
  Snowflake,
  CloudLightning,
  Globe,
  Clock,
  Terminal,
  Copy,
  Check,
  Sparkles,
  Image as ImageIcon,
} from 'lucide-react';
import { BUILTIN_WALLPAPERS } from '../data/wallpapers';
import { useAccent } from '../context/AccentContext';

type FeatureTab = 'weather' | 'sources' | 'schedule' | 'cli';

const WEATHER_CONDITIONS = [
  {
    id: 'clear',
    label: 'Clear',
    temp: '22°C',
    icon: Sun,
    folder: 'D:\\Walls\\Weather\\Clear',
    wp: BUILTIN_WALLPAPERS[4], // machapuchare
  },
  {
    id: 'cloudy',
    label: 'Cloudy',
    temp: '14°C',
    icon: Cloud,
    folder: 'D:\\Walls\\Weather\\Cloudy',
    wp: BUILTIN_WALLPAPERS[11], // madeira misty forest
  },
  {
    id: 'rain',
    label: 'Rain',
    temp: '11°C',
    icon: CloudRain,
    folder: 'D:\\Walls\\Weather\\Rain',
    wp: BUILTIN_WALLPAPERS[9], // lake louise
  },
  {
    id: 'snow',
    label: 'Snow',
    temp: '-4°C',
    icon: Snowflake,
    folder: 'D:\\Walls\\Weather\\Snow',
    wp: BUILTIN_WALLPAPERS[2], // aurora saana
  },
  {
    id: 'storm',
    label: 'Storm',
    temp: '16°C',
    icon: CloudLightning,
    folder: 'D:\\Walls\\Weather\\Storm',
    wp: BUILTIN_WALLPAPERS[7], // earth thunderheads
  },
];

const CLI_COMMANDS = [
  { cmd: 'walld.exe next', out: '[IPC] Applied next wallpaper → cosmic-cliffs-carina-nebula.jpg' },
  { cmd: 'walld.exe prev', out: '[IPC] Restored previous arrangement from history.json' },
  { cmd: 'walld.exe random', out: '[IPC] Picked random (non-repeating) → aurora-over-saana-fell.jpg' },
  { cmd: 'walld.exe pause', out: '[IPC] Auto-change paused (timer suspended)' },
  { cmd: 'walld.exe set "D:\\Walls\\space.jpg"', out: '[IPC] Applied explicit path on active monitor' },
];

export function AutomationSection() {
  const { selectWallpaper, showToast } = useAccent();
  const [tab, setTab] = useState<FeatureTab>('weather');
  const [weatherId, setWeatherId] = useState('clear');
  const [wallhavenSort, setWallhavenSort] = useState<'toplist' | 'random' | 'views'>('toplist');
  const [wallhavenCat, setWallhavenCat] = useState<'general' | 'anime' | 'all'>('general');
  const [hour, setHour] = useState(14);
  const [selectedCliIdx, setSelectedCliIdx] = useState(0);
  const [copiedCli, setCopiedCli] = useState(false);

  const activeWeather =
    WEATHER_CONDITIONS.find((w) => w.id === weatherId) ?? WEATHER_CONDITIONS[0];

  const isNightHour = hour >= 19 || hour < 7;
  const scheduleWp = isNightHour ? BUILTIN_WALLPAPERS[13] : BUILTIN_WALLPAPERS[4];

  const tabs: Array<{ id: FeatureTab; label: string; icon: typeof Sun }> = [
    { id: 'weather', label: 'Weather Switching', icon: CloudRain },
    { id: 'sources', label: 'Wallhaven & Bing', icon: Globe },
    { id: 'schedule', label: 'Day / Night Schedule', icon: Clock },
    { id: 'cli', label: 'CLI & Backup', icon: Terminal },
  ];

  const handleCopyCli = () => {
    const text = CLI_COMMANDS[selectedCliIdx].cmd;
    navigator.clipboard?.writeText(text).catch(() => {});
    setCopiedCli(true);
    showToast(`Copied: ${text}`);
    setTimeout(() => setCopiedCli(false), 1400);
  };

  return (
    <section id="features" className="relative px-4 py-20 sm:px-6">
      <div className="mx-auto max-w-6xl">
        <div className="flex flex-col justify-between gap-6 md:flex-row md:items-end">
          <div className="max-w-xl">
            <span className="inline-flex items-center gap-2 rounded-full border border-white/10 bg-white/[0.04] px-3 py-1 font-mono text-[11px] uppercase tracking-wider text-[var(--accent)]">
              <Sparkles className="h-3.5 w-3.5" />
              Smart Automation &amp; Opt-In Sources
            </span>
            <h2 className="mt-4 font-display text-3xl font-bold text-white sm:text-4xl">
              Your desktop adapts—only when you ask it to.
            </h2>
          </div>

          {/* Interactive Tab Switcher */}
          <div className="glass-pill inline-flex flex-wrap gap-1 rounded-2xl p-1">
            {tabs.map((t) => {
              const Icon = t.icon;
              const active = tab === t.id;
              return (
                <button
                  key={t.id}
                  type="button"
                  onClick={() => setTab(t.id)}
                  className={`relative flex items-center gap-2 rounded-xl px-3.5 py-2 text-xs font-medium transition-colors ${
                    active ? 'text-white' : 'text-zinc-400 hover:text-zinc-200'
                  }`}
                >
                  {active && (
                    <motion.span
                      layoutId="automation-tab"
                      transition={{ type: 'spring', stiffness: 360, damping: 28 }}
                      className="absolute inset-0 rounded-xl border border-white/15 bg-white/10"
                    />
                  )}
                  <Icon className="relative z-10 h-3.5 w-3.5" />
                  <span className="relative z-10">{t.label}</span>
                </button>
              );
            })}
          </div>
        </div>

        {/* Showcase Panel */}
        <div className="glass-panel mt-8 overflow-hidden rounded-3xl p-6 sm:p-8">
          <AnimatePresence mode="wait">
            {tab === 'weather' && (
              <motion.div
                key="weather"
                initial={{ opacity: 0, y: 10 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0, y: -10 }}
                transition={{ duration: 0.25 }}
                className="grid items-center gap-8 lg:grid-cols-2"
              >
                <div>
                  <span className="font-mono text-xs text-[var(--accent)]">
                    api.open-meteo.com &bull; Zero API Key
                  </span>
                  <h3 className="mt-2 font-display text-2xl font-bold text-white">
                    Weather-Based Folder Switching
                  </h3>
                  <p className="mt-2 text-sm leading-relaxed text-zinc-400">
                    When opted in, WallpaperSuite resolves your chosen city once via Open-Meteo geocoding and checks local weather every 30 minutes—routing Clear, Cloudy, Rain, Snow, or Storm to your own folders.
                  </p>

                  {/* Interactive Weather Condition Pills */}
                  <div className="mt-6 flex flex-wrap gap-2">
                    {WEATHER_CONDITIONS.map((cond) => {
                      const Icon = cond.icon;
                      const active = cond.id === weatherId;
                      return (
                        <button
                          key={cond.id}
                          type="button"
                          onClick={() => {
                            setWeatherId(cond.id);
                            selectWallpaper(cond.wp);
                          }}
                          className={`inline-flex items-center gap-2 rounded-xl border px-3.5 py-2 text-xs font-medium transition ${
                            active
                              ? 'border-[var(--accent)] bg-white/10 text-white shadow-[0_0_20px_-6px_rgba(var(--accent-rgb),0.6)]'
                              : 'border-white/10 bg-white/[0.03] text-zinc-400 hover:bg-white/[0.07] hover:text-zinc-200'
                          }`}
                        >
                          <Icon className="h-3.5 w-3.5 text-[var(--accent)]" />
                          <span>{cond.label}</span>
                          <span className="font-mono text-[10px] text-zinc-500">{cond.temp}</span>
                        </button>
                      );
                    })}
                  </div>

                  <div className="mt-5 rounded-xl border border-white/10 bg-black/40 p-3 font-mono text-xs text-zinc-300">
                    <span className="text-zinc-500">Matched Condition Folder → </span>
                    <span className="text-white">{activeWeather.folder}</span>
                  </div>
                </div>

                <div className="relative overflow-hidden rounded-2xl border border-white/10 bg-black/40">
                  <div className="aspect-video w-full overflow-hidden">
                    <motion.img
                      key={activeWeather.wp.id}
                      src={activeWeather.wp.src}
                      alt={activeWeather.wp.title}
                      initial={{ opacity: 0.4, scale: 1.04 }}
                      animate={{ opacity: 1, scale: 1 }}
                      transition={{ duration: 0.4 }}
                      className="h-full w-full object-cover"
                    />
                  </div>
                  <div className="flex items-center justify-between border-t border-white/10 bg-black/60 px-4 py-3 backdrop-blur-md">
                    <div>
                      <p className="text-xs font-semibold text-white">{activeWeather.wp.title}</p>
                      <p className="font-mono text-[10px] text-zinc-400">
                        Condition: {activeWeather.label} ({activeWeather.temp})
                      </p>
                    </div>
                    <span className="rounded-md bg-white/10 px-2 py-0.5 font-mono text-[10px] text-zinc-300">
                      CC BY 4.0 Open-Meteo
                    </span>
                  </div>
                </div>
              </motion.div>
            )}

            {tab === 'sources' && (
              <motion.div
                key="sources"
                initial={{ opacity: 0, y: 10 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0, y: -10 }}
                transition={{ duration: 0.25 }}
                className="grid gap-6 lg:grid-cols-2"
              >
                {/* Wallhaven Card */}
                <div className="rounded-2xl border border-white/10 bg-black/35 p-5">
                  <div className="flex items-center justify-between">
                    <span className="inline-flex items-center gap-2 font-display text-lg font-bold text-white">
                      <ImageIcon className="h-4 w-4 text-[var(--accent)]" />
                      Wallhaven SFW Landscape Fetcher
                    </span>
                    <span className="rounded-md border border-emerald-400/30 bg-emerald-400/10 px-2 py-0.5 font-mono text-[10px] text-emerald-300">
                      purity=100
                    </span>
                  </div>
                  <p className="mt-2 text-xs leading-relaxed text-zinc-400">
                    Queries Wallhaven&apos;s free no-API-key search API for crisp <code className="text-zinc-200">≥ 1920×1080</code> images in <code className="text-zinc-200">16:9 / 16:10 / 21:9</code>, validates JPEG/PNG headers, and keeps the newest 60.
                  </p>

                  <div className="mt-4 flex flex-wrap gap-2">
                    {(['toplist', 'random', 'views'] as const).map((s) => (
                      <button
                        key={s}
                        type="button"
                        onClick={() => setWallhavenSort(s)}
                        className={`rounded-lg border px-2.5 py-1 font-mono text-[11px] transition ${
                          wallhavenSort === s
                            ? 'border-[var(--accent)] bg-white/10 text-white'
                            : 'border-white/10 text-zinc-400 hover:text-white'
                        }`}
                      >
                        sort={s}
                      </button>
                    ))}
                    {(['general', 'anime', 'all'] as const).map((c) => (
                      <button
                        key={c}
                        type="button"
                        onClick={() => setWallhavenCat(c)}
                        className={`rounded-lg border px-2.5 py-1 font-mono text-[11px] transition ${
                          wallhavenCat === c
                            ? 'border-white/30 bg-white/10 text-white'
                            : 'border-white/10 text-zinc-400 hover:text-white'
                        }`}
                      >
                        cat={c}
                      </button>
                    ))}
                  </div>

                  <div className="mt-4 rounded-xl border border-white/10 bg-[#05060a] p-3 font-mono text-[11px] text-zinc-300 break-all">
                    GET https://wallhaven.cc/api/v1/search?categories={wallhavenCat}&amp;purity=100&amp;sorting={wallhavenSort}&amp;atleast=1920x1080
                  </div>
                </div>

                {/* Bing Daily Card */}
                <div className="rounded-2xl border border-white/10 bg-black/35 p-5">
                  <div className="flex items-center justify-between">
                    <span className="inline-flex items-center gap-2 font-display text-lg font-bold text-white">
                      <Globe className="h-4 w-4 text-[var(--accent)]" />
                      Bing 4K Photo of the Day
                    </span>
                    <span className="rounded-md border border-white/15 bg-white/5 px-2 py-0.5 font-mono text-[10px] text-zinc-300">
                      _UHD.jpg
                    </span>
                  </div>
                  <p className="mt-2 text-xs leading-relaxed text-zinc-400">
                    Downloads Bing&apos;s daily spotlight photograph in UHD 4K (falling back to 1080p) into <code className="text-zinc-200">%APPDATA%\WallpaperPicker\daily\</code> so every daily photo also appears inside your picker.
                  </p>
                  <div className="mt-4 overflow-hidden rounded-xl border border-white/10">
                    <div className="relative aspect-[21/9] w-full">
                      <img
                        src={BUILTIN_WALLPAPERS[14].src}
                        alt="Sample landscape wallpaper"
                        className="h-full w-full object-cover"
                      />
                      <div className="absolute inset-0 bg-gradient-to-t from-black/80 via-transparent to-transparent" />
                      <div className="absolute bottom-2 left-3 right-3 flex items-center justify-between text-[11px]">
                        <span className="font-medium text-white">Favorites are never pruned</span>
                        <span className="font-mono text-zinc-300">60-item rolling archive</span>
                      </div>
                    </div>
                  </div>
                </div>
              </motion.div>
            )}

            {tab === 'schedule' && (
              <motion.div
                key="schedule"
                initial={{ opacity: 0, y: 10 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0, y: -10 }}
                transition={{ duration: 0.25 }}
                className="grid items-center gap-8 lg:grid-cols-2"
              >
                <div>
                  <span className="font-mono text-xs text-[var(--accent)]">
                    Default Night Window: 19:00 – 07:00
                  </span>
                  <h3 className="mt-2 font-display text-2xl font-bold text-white">
                    Day / Night Folders &amp; Collections
                  </h3>
                  <p className="mt-2 text-sm text-zinc-400">
                    Scrub the time-of-day slider to preview automatic transitions between your bright Day folder and calm Night folder—plus Brightness-Synced Windows Light/Dark mode.
                  </p>

                  <div className="mt-6 rounded-2xl border border-white/10 bg-black/40 p-4">
                    <div className="flex items-center justify-between">
                      <span className="font-mono text-xs text-zinc-400">Simulated System Clock</span>
                      <span className="font-mono text-lg font-bold text-white">
                        {String(hour).padStart(2, '0')}:00{' '}
                        <span className="text-xs font-normal text-[var(--accent)]">
                          ({isNightHour ? 'Night Mode' : 'Day Mode'})
                        </span>
                      </span>
                    </div>
                    <input
                      type="range"
                      min={0}
                      max={23}
                      value={hour}
                      onChange={(e) => {
                        const h = Number(e.target.value);
                        setHour(h);
                        const night = h >= 19 || h < 7;
                        selectWallpaper(night ? BUILTIN_WALLPAPERS[13] : BUILTIN_WALLPAPERS[4]);
                      }}
                      aria-label="Time of day slider"
                      className="mt-3 h-2 w-full cursor-pointer accent-[var(--accent)]"
                    />
                    <div className="mt-2 flex justify-between font-mono text-[10px] text-zinc-500">
                      <span>00:00 (Night)</span>
                      <span>07:00 (Day)</span>
                      <span>19:00 (Night)</span>
                      <span>23:00</span>
                    </div>
                  </div>
                </div>

                <div className="overflow-hidden rounded-2xl border border-white/10 bg-black/40">
                  <div className="aspect-video w-full overflow-hidden">
                    <motion.img
                      key={scheduleWp.id}
                      src={scheduleWp.src}
                      alt={scheduleWp.title}
                      initial={{ opacity: 0.45 }}
                      animate={{ opacity: 1 }}
                      transition={{ duration: 0.35 }}
                      className="h-full w-full object-cover"
                    />
                  </div>
                  <div className="flex items-center justify-between border-t border-white/10 bg-black/60 px-4 py-3">
                    <span className="text-xs font-semibold text-white">{scheduleWp.title}</span>
                    <span className="font-mono text-[11px] text-zinc-400">
                      {isNightHour ? 'NightFolder' : 'DayFolder'}
                    </span>
                  </div>
                </div>
              </motion.div>
            )}

            {tab === 'cli' && (
              <motion.div
                key="cli"
                initial={{ opacity: 0, y: 10 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0, y: -10 }}
                transition={{ duration: 0.25 }}
                className="grid gap-8 lg:grid-cols-[1fr_1.15fr]"
              >
                <div>
                  <span className="font-mono text-xs text-[var(--accent)]">
                    Scriptable IPC &bull; Backup &amp; SHA-256 Dedupe
                  </span>
                  <h3 className="mt-2 font-display text-2xl font-bold text-white">
                    First-Class CLI, Zip Backups &amp; Duplicate Finder
                  </h3>
                  <p className="mt-2 text-sm text-zinc-400">
                    Drive the running tray daemon from batch scripts, Stream Deck, or PowerShell. Export portable <code className="text-zinc-200">.zip</code> config backups or scan folders by file size + SHA-256 hash to recycle duplicates safely.
                  </p>

                  <div className="mt-5 flex flex-wrap gap-2">
                    {CLI_COMMANDS.map((item, idx) => (
                      <button
                        key={item.cmd}
                        type="button"
                        onClick={() => setSelectedCliIdx(idx)}
                        className={`rounded-lg border px-2.5 py-1 font-mono text-[11px] transition ${
                          selectedCliIdx === idx
                            ? 'border-[var(--accent)] bg-white/10 text-white'
                            : 'border-white/10 bg-white/[0.03] text-zinc-400 hover:text-white'
                        }`}
                      >
                        {item.cmd.replace('walld.exe ', '')}
                      </button>
                    ))}
                  </div>
                </div>

                {/* Interactive Terminal */}
                <div className="overflow-hidden rounded-2xl border border-white/12 bg-[#05060a]">
                  <div className="flex items-center justify-between border-b border-white/[0.08] bg-white/[0.03] px-4 py-2.5">
                    <div className="flex items-center gap-2">
                      <span className="h-2.5 w-2.5 rounded-full bg-rose-500/70" />
                      <span className="h-2.5 w-2.5 rounded-full bg-amber-400/70" />
                      <span className="h-2.5 w-2.5 rounded-full bg-emerald-400/70" />
                      <span className="ml-2 font-mono text-[11px] text-zinc-400">
                        PowerShell — walld.exe CLI
                      </span>
                    </div>
                    <button
                      type="button"
                      onClick={handleCopyCli}
                      className="inline-flex items-center gap-1.5 rounded-lg border border-white/10 bg-white/5 px-2.5 py-1 font-mono text-[10px] text-zinc-300 hover:bg-white/10 hover:text-white"
                    >
                      {copiedCli ? (
                        <Check className="h-3 w-3 text-emerald-400" />
                      ) : (
                        <Copy className="h-3 w-3" />
                      )}
                      {copiedCli ? 'Copied' : 'Copy'}
                    </button>
                  </div>
                  <div className="space-y-3 p-4 font-mono text-xs leading-relaxed">
                    <div>
                      <span className="text-zinc-500">PS C:\&gt; </span>
                      <span className="text-white">{CLI_COMMANDS[selectedCliIdx].cmd}</span>
                    </div>
                    <div className="rounded-lg border border-white/[0.06] bg-white/[0.02] p-2.5 text-[11px] text-[var(--accent)]">
                      {CLI_COMMANDS[selectedCliIdx].out}
                    </div>
                    <div className="border-t border-white/[0.06] pt-2.5 text-[11px] text-zinc-500">
                      Available: open | next | prev | random | pause | resume | toggle-pause | set &lt;path&gt; | quit
                    </div>
                  </div>
                </div>
              </motion.div>
            )}
          </AnimatePresence>
        </div>
      </div>
    </section>
  );
}
