import { Keyboard } from 'lucide-react';

const HOTKEYS = [
  { keys: ['Alt', 'Shift', 'W'], label: 'Open Picker' },
  { keys: ['Alt', 'Shift', '→'], label: 'Next Wallpaper' },
  { keys: ['Alt', 'Shift', '←'], label: 'Prev Wallpaper' },
  { keys: ['/'], label: 'Instant Search' },
  { keys: ['F'], label: 'Toggle Favorite' },
  { keys: ['E'], label: 'Export Palette' },
  { keys: ['M'], label: 'Cycle Fit Mode' },
];

export function HotkeysSection() {
  return (
    <section className="relative px-4 py-14 sm:px-6">
      <div className="mx-auto max-w-6xl">
        <div className="glass-panel rounded-3xl px-5 py-7 sm:px-8">
          <div className="mb-6 flex flex-wrap items-center justify-between gap-2">
            <div className="flex items-center gap-2.5">
              <Keyboard className="h-4 w-4 text-[var(--accent)]" />
              <h2 className="font-display text-lg font-bold text-white sm:text-xl">
                Hotkey-First Muscle Memory
              </h2>
            </div>
            <span className="font-mono text-xs text-zinc-400">
              Global hotkeys editable in Settings &bull; Picker supports Vim keys (H/J/K/L)
            </span>
          </div>

          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4">
            {HOTKEYS.map((item) => (
              <div
                key={item.label}
                className="group flex items-center justify-between gap-3 rounded-2xl border border-white/[0.07] bg-black/35 px-3.5 py-3 transition hover:border-white/15"
              >
                <span className="text-xs font-medium text-zinc-300 group-hover:text-white">
                  {item.label}
                </span>
                <div className="flex items-center gap-1">
                  {item.keys.map((k, i) => (
                    <span key={`${item.label}-${k}`} className="inline-flex items-center gap-1">
                      <kbd className="keycap-3d inline-flex min-w-[26px] items-center justify-center rounded-lg px-2 py-1 font-mono text-[11px] font-semibold text-zinc-100 select-none">
                        {k}
                      </kbd>
                      {i < item.keys.length - 1 && (
                        <span className="font-mono text-[10px] text-zinc-600">+</span>
                      )}
                    </span>
                  ))}
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>
    </section>
  );
}
