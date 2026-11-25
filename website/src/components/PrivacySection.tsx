import { useState } from 'react';
import { motion } from 'framer-motion';
import { ShieldCheck, Lock, WifiOff, CheckCircle2, FileCheck2, FolderLock } from 'lucide-react';

interface NetworkToggle {
  id: string;
  name: string;
  endpoint: string;
  detail: string;
}

const OPTIONAL_NETWORK_FEATURES: NetworkToggle[] = [
  {
    id: 'bing',
    name: 'Bing Daily (Wallpaper of the day)',
    endpoint: 'www.bing.com/HPImageArchive.aspx',
    detail: 'Only sends your Windows locale (mkt) + User-Agent: WallpaperSuite/0.8.0.',
  },
  {
    id: 'weather',
    name: 'Open-Meteo Weather Switching',
    endpoint: 'api.open-meteo.com/v1/forecast',
    detail: 'Only sends coordinates of your chosen city. No API key, no telemetry.',
  },
  {
    id: 'wallhaven',
    name: 'Wallhaven SFW Catalogue',
    endpoint: 'wallhaven.cc/api/v1/search?purity=100',
    detail: 'Only sends your optional keywords, category, and sorting. Capped at 25 MB.',
  },
  {
    id: 'updates',
    name: 'GitHub Release Update Check',
    endpoint: 'api.github.com/repos/zero1-spec/wallpaper-picker/releases/latest',
    detail: 'Verifies .sha256 checksum before executing; mismatch deletes download immediately.',
  },
];

const TRUST_PILLARS = [
  {
    icon: WifiOff,
    title: 'Zero Background Ports',
    desc: 'Uses Win32 WM_COPYDATA IPC and a named mutex—never opens a local HTTP server or socket.',
  },
  {
    icon: Lock,
    title: 'Zero Telemetry & Accounts',
    desc: 'No analytics SDKs, no crash uploaders, no machine IDs, and no third-party runtime NuGet packages.',
  },
  {
    icon: FileCheck2,
    title: 'SHA-256 Verified Updates',
    desc: 'In-app updater checks the release .sha256 over HTTPS and aborts on any hash mismatch.',
  },
  {
    icon: FolderLock,
    title: 'Per-User Install (No Admin)',
    desc: 'Installs cleanly to %LOCALAPPDATA%\\Programs\\WallpaperSuite without UAC elevation.',
  },
];

export function PrivacySection() {
  // All 4 optional network toggles default to OFF (matching real WallpaperSuite default)
  const [toggles, setToggles] = useState<Record<string, boolean>>({
    bing: false,
    weather: false,
    wallhaven: false,
    updates: false,
  });

  const activeCount = Object.values(toggles).filter(Boolean).length;

  const flip = (id: string) => setToggles((prev) => ({ ...prev, [id]: !prev[id] }));

  return (
    <section id="privacy" className="relative px-4 py-20 sm:px-6">
      <div className="mx-auto max-w-6xl">
        <div className="grid gap-8 lg:grid-cols-[1.05fr_1fr] lg:items-center">
          {/* Left Copy & Trust Pillars */}
          <motion.div
            initial={{ opacity: 0, y: 18 }}
            whileInView={{ opacity: 1, y: 0 }}
            viewport={{ once: true, amount: 0.25 }}
            transition={{ duration: 0.45 }}
          >
            <span className="inline-flex items-center gap-2 rounded-full border border-emerald-400/25 bg-emerald-400/10 px-3 py-1 font-mono text-[11px] uppercase tracking-wider text-emerald-300">
              <ShieldCheck className="h-3.5 w-3.5" />
              Offline By Default
            </span>
            <h2 className="mt-4 font-display text-3xl font-bold text-white sm:text-4xl">
              Zero telemetry. Zero surprise requests.
            </h2>
            <p className="mt-3 text-sm leading-relaxed text-zinc-400 sm:text-base">
              When all four optional toggles are off, WallpaperSuite doesn&apos;t even instantiate an{' '}
              <code className="text-zinc-200">HttpClient</code>. Nothing leaves your machine unless you explicitly flip a switch.
            </p>

            <div className="mt-7 grid gap-3 sm:grid-cols-2">
              {TRUST_PILLARS.map((item) => {
                const Icon = item.icon;
                return (
                  <div
                    key={item.title}
                    className="rounded-2xl border border-white/[0.08] bg-white/[0.02] p-4"
                  >
                    <Icon className="h-4 w-4 text-[var(--accent)]" />
                    <h3 className="mt-2.5 text-sm font-semibold text-white">{item.title}</h3>
                    <p className="mt-1 text-xs leading-relaxed text-zinc-400">{item.desc}</p>
                  </div>
                );
              })}
            </div>
          </motion.div>

          {/* Right Interactive Default-OFF Switchboard */}
          <motion.div
            initial={{ opacity: 0, y: 18 }}
            whileInView={{ opacity: 1, y: 0 }}
            viewport={{ once: true, amount: 0.25 }}
            transition={{ duration: 0.45, delay: 0.08 }}
            className="glass-panel rounded-3xl p-5 sm:p-6"
          >
            <div className="mb-4 flex items-center justify-between border-b border-white/[0.08] pb-3.5">
              <div>
                <p className="font-mono text-xs font-semibold text-white">
                  Outbound Network Switchboard
                </p>
                <p className="text-[11px] text-zinc-400">
                  {activeCount === 0
                    ? 'Offline Mode — HttpClient not created • 0 requests'
                    : `${activeCount} optional source${activeCount > 1 ? 's' : ''} opted-in`}
                </p>
              </div>
              <span
                className={`inline-flex items-center gap-1.5 rounded-full border px-2.5 py-1 font-mono text-[10px] font-semibold ${
                  activeCount === 0
                    ? 'border-emerald-400/35 bg-emerald-400/10 text-emerald-300'
                    : 'border-amber-400/35 bg-amber-400/10 text-amber-300'
                }`}
              >
                <CheckCircle2 className="h-3 w-3" />
                {activeCount === 0 ? '100% OFFLINE' : 'OPT-IN ACTIVE'}
              </span>
            </div>

            <div className="space-y-3">
              {OPTIONAL_NETWORK_FEATURES.map((feat) => {
                const isOn = toggles[feat.id];
                return (
                  <div
                    key={feat.id}
                    className={`rounded-2xl border p-3.5 transition ${
                      isOn
                        ? 'border-[var(--accent)]/40 bg-white/[0.05]'
                        : 'border-white/[0.07] bg-black/30'
                    }`}
                  >
                    <div className="flex items-center justify-between gap-3">
                      <div className="min-w-0">
                        <p className="text-sm font-medium text-white">{feat.name}</p>
                        <p className="truncate font-mono text-[10px] text-zinc-500">
                          {isOn ? feat.endpoint : 'Disabled by default — zero network calls'}
                        </p>
                      </div>

                      {/* Physical-feeling Toggle Switch */}
                      <button
                        type="button"
                        role="switch"
                        aria-checked={isOn}
                        onClick={() => flip(feat.id)}
                        className={`relative inline-flex h-6 w-11 shrink-0 cursor-pointer items-center rounded-full border p-0.5 transition-colors ${
                          isOn
                            ? 'border-[var(--accent)] bg-[var(--accent)]'
                            : 'border-white/15 bg-zinc-800'
                        }`}
                      >
                        <motion.span
                          layout
                          transition={{ type: 'spring', stiffness: 500, damping: 30 }}
                          className={`h-4.5 w-4.5 rounded-full bg-white shadow-md ${
                            isOn ? 'ml-auto' : ''
                          }`}
                        />
                      </button>
                    </div>
                    {isOn && (
                      <p className="mt-2 border-t border-white/[0.06] pt-2 text-[11px] text-zinc-400">
                        {feat.detail}
                      </p>
                    )}
                  </div>
                );
              })}
            </div>
          </motion.div>
        </div>
      </div>
    </section>
  );
}
