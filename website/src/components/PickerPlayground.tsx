import React, { useEffect, useMemo, useRef, useState } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import {
  Heart,
  Search,
  Check,
  Sparkles,
  CornerDownLeft,
  ChevronLeft,
  ChevronRight,
  Monitor,
  SlidersHorizontal,
  X,
} from 'lucide-react';
import { BUILTIN_WALLPAPERS, WallpaperCategory, WallpaperItem } from '../data/wallpapers';
import { useAccent } from '../context/AccentContext';

type PickerThemeId = 'arc-carousel' | 'skewed-accordion' | 'centered-carousel' | 'classic-grid';

const PICKER_THEMES: Array<{ id: PickerThemeId; label: string; badge: string }> = [
  { id: 'arc-carousel', label: 'Arc Carousel', badge: 'Signature' },
  { id: 'skewed-accordion', label: 'Skewed Accordion', badge: 'Default' },
  { id: 'centered-carousel', label: 'Centered Carousel', badge: 'Cinematic' },
  { id: 'classic-grid', label: 'Classic Grid', badge: 'Fast' },
];

const CATEGORIES: Array<{ id: 'all' | 'favorites' | WallpaperCategory; label: string }> = [
  { id: 'all', label: 'All (16)' },
  { id: 'favorites', label: 'Favorites' },
  { id: 'space', label: 'Space' },
  { id: 'nature', label: 'Nature' },
  { id: 'night', label: 'Night' },
  { id: 'abstract', label: 'Abstract' },
];

export function PickerPlayground() {
  const {
    activeWallpaper,
    accentColor,
    favorites,
    selectWallpaper,
    selectSwatch,
    applyWallpaper,
    toggleFavorite,
    showToast,
  } = useAccent();

  const [theme, setTheme] = useState<PickerThemeId>('arc-carousel');
  const [searchOpen, setSearchOpen] = useState(false);
  const [query, setQuery] = useState('');
  const [category, setCategory] = useState<'all' | 'favorites' | WallpaperCategory>('all');
  const [hoveredIdx, setHoveredIdx] = useState<number | null>(null);
  const [pressedKey, setPressedKey] = useState<string | null>(null);
  const [copiedHex, setCopiedHex] = useState<string | null>(null);
  const [showNativeModal, setShowNativeModal] = useState(false);

  const searchInputRef = useRef<HTMLInputElement>(null);
  const stageRef = useRef<HTMLDivElement>(null);

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    return BUILTIN_WALLPAPERS.filter((wp) => {
      if (category === 'favorites' && !favorites.has(wp.id)) return false;
      if (category !== 'all' && category !== 'favorites' && wp.category !== category) return false;
      if (!q) return true;
      return (
        wp.title.toLowerCase().includes(q) ||
        wp.filename.toLowerCase().includes(q) ||
        wp.category.toLowerCase().includes(q) ||
        wp.tags.some((t) => t.toLowerCase().includes(q))
      );
    });
  }, [query, category, favorites]);

  const activeIndex = useMemo(() => {
    const idx = filtered.findIndex((w) => w.id === activeWallpaper.id);
    return idx >= 0 ? idx : 0;
  }, [filtered, activeWallpaper.id]);

  // Keep activeWallpaper inside filtered list when filters change
  useEffect(() => {
    if (filtered.length > 0 && !filtered.some((w) => w.id === activeWallpaper.id)) {
      selectWallpaper(filtered[0]);
    }
  }, [filtered, activeWallpaper.id, selectWallpaper]);

  const stepIndex = (delta: number) => {
    if (filtered.length === 0) return;
    const next = (activeIndex + delta + filtered.length) % filtered.length;
    selectWallpaper(filtered[next]);
  };

  const flashKey = (k: string) => {
    setPressedKey(k);
    setTimeout(() => setPressedKey((prev) => (prev === k ? null : prev)), 180);
  };

  const handleCopySwatch = (hex: string, e?: React.MouseEvent) => {
    e?.stopPropagation();
    selectSwatch(hex);
    setCopiedHex(hex);
    navigator.clipboard?.writeText(hex).catch(() => {});
    setTimeout(() => setCopiedHex(null), 1200);
  };

  // Global keyboard shortcuts when user is not typing in inputs
  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement | null;
      const isTyping =
        target &&
        (target.tagName === 'INPUT' ||
          target.tagName === 'TEXTAREA' ||
          target.isContentEditable);

      if (e.key === 'Escape') {
        if (showNativeModal) {
          setShowNativeModal(false);
          return;
        }
        if (searchOpen) {
          setSearchOpen(false);
          setQuery('');
          searchInputRef.current?.blur();
        }
        return;
      }

      if (isTyping) return;

      if (e.key === 'ArrowRight' || e.key === 'l' || e.key === 'L') {
        e.preventDefault();
        flashKey('right');
        stepIndex(1);
      } else if (e.key === 'ArrowLeft' || e.key === 'h' || e.key === 'H') {
        e.preventDefault();
        flashKey('left');
        stepIndex(-1);
      } else if (e.key === 'Enter') {
        // Only intercept Enter when playground stage or body has focus
        if (document.activeElement === document.body || stageRef.current?.contains(document.activeElement)) {
          e.preventDefault();
          flashKey('enter');
          applyWallpaper();
        }
      } else if (e.key === '/') {
        e.preventDefault();
        flashKey('/');
        setSearchOpen(true);
        setTimeout(() => searchInputRef.current?.focus(), 40);
      } else if (e.key === 'f' || e.key === 'F') {
        e.preventDefault();
        flashKey('f');
        toggleFavorite(activeWallpaper.id);
      }
    };

    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  });

  const currentItem: WallpaperItem | undefined = filtered[activeIndex] ?? filtered[0];

  return (
    <div id="themes" ref={stageRef} className="relative mx-auto mt-10 w-full max-w-6xl">
      {/* Theme Switcher Pill Bar */}
      <div className="mb-4 flex flex-col items-center justify-between gap-3 sm:flex-row">
        <div className="glass-pill inline-flex flex-wrap items-center justify-center gap-1 rounded-2xl p-1">
          {PICKER_THEMES.map((t) => {
            const active = theme === t.id;
            return (
              <button
                key={t.id}
                type="button"
                onClick={() => setTheme(t.id)}
                className={`relative flex items-center gap-2 rounded-xl px-3.5 py-1.5 text-xs font-medium transition-colors ${
                  active ? 'text-white' : 'text-zinc-400 hover:text-zinc-200'
                }`}
              >
                {active && (
                  <motion.span
                    layoutId="picker-theme-pill"
                    transition={{ type: 'spring', stiffness: 360, damping: 28 }}
                    className="absolute inset-0 rounded-xl border border-white/15 bg-white/10 shadow-inner"
                  />
                )}
                <span className="relative z-10">{t.label}</span>
                <span
                  className="relative z-10 hidden sm:inline-block rounded-md px-1.5 py-0.5 font-mono text-[10px]"
                  style={{
                    backgroundColor: active ? 'rgba(var(--accent-rgb), 0.22)' : 'rgba(255,255,255,0.05)',
                    color: active ? 'var(--accent)' : '#71717a',
                  }}
                >
                  {t.badge}
                </span>
              </button>
            );
          })}
        </div>

        {/* Live Accent Pill + Native Screenshot toggle */}
        <div className="flex items-center gap-2">
          <div className="glass-pill flex items-center gap-2 rounded-xl px-3 py-1.5 text-xs">
            <span
              className="h-3 w-3 rounded-full ring-2 ring-white/20 transition-colors duration-300"
              style={{ backgroundColor: accentColor }}
            />
            <span className="text-zinc-400">Live Accent:</span>
            <span className="font-mono font-semibold text-white">{accentColor.toUpperCase()}</span>
          </div>

          <button
            type="button"
            onClick={() => setShowNativeModal(true)}
            className="glass-pill inline-flex items-center gap-1.5 rounded-xl px-3 py-1.5 text-xs font-medium text-zinc-300 transition hover:border-white/20 hover:text-white"
          >
            <Monitor className="h-3.5 w-3.5 text-zinc-400" />
            <span className="hidden sm:inline">Native WPF Shot</span>
          </button>
        </div>
      </div>

      {/* Main Picker Viewport Stage */}
      <div className="glass-panel relative overflow-hidden rounded-3xl border border-white/10 bg-[#08090f]/90 shadow-[0_32px_90px_-24px_rgba(0,0,0,0.95)]">
        {/* Frosted Blurred Copy of Selected Wallpaper (Authentic Arc Carousel signature) */}
        <div className="pointer-events-none absolute inset-0 overflow-hidden">
          <AnimatePresence mode="wait">
            <motion.img
              key={activeWallpaper.id}
              src={activeWallpaper.src}
              alt=""
              initial={{ opacity: 0, scale: 1.12 }}
              animate={{ opacity: 0.34, scale: 1.08 }}
              exit={{ opacity: 0 }}
              transition={{ duration: 0.55 }}
              className="h-full w-full object-cover blur-3xl saturate-150"
            />
          </AnimatePresence>
          <div
            className="absolute inset-0"
            style={{
              background:
                'radial-gradient(circle at 50% 45%, rgba(var(--accent-rgb), 0.16) 0%, rgba(6,7,11,0.82) 72%, rgba(6,7,11,0.96) 100%)',
            }}
          />
        </div>

        {/* Top Bar inside Picker: Heart + Set Wallpaper Pill + Filter/Search */}
        <div className="relative z-20 flex flex-wrap items-center justify-between gap-3 border-b border-white/[0.06] bg-black/25 px-4 py-3 backdrop-blur-md">
          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={() => {
                flashKey('f');
                toggleFavorite(activeWallpaper.id);
              }}
              className="flex h-8 w-8 items-center justify-center rounded-xl border border-white/10 bg-white/5 transition hover:bg-white/10"
              title="Toggle Favorite (F)"
              aria-label="Toggle Favorite"
            >
              <Heart
                className="h-4 w-4 transition-colors"
                style={{
                  color: favorites.has(activeWallpaper.id) ? '#fb7185' : '#a1a1aa',
                  fill: favorites.has(activeWallpaper.id) ? '#fb7185' : 'transparent',
                }}
              />
            </button>

            <button
              type="button"
              onClick={() => {
                flashKey('enter');
                applyWallpaper();
              }}
              className="inline-flex items-center gap-2 rounded-xl border border-white/15 bg-white/10 px-3.5 py-1.5 text-xs font-semibold text-white transition hover:bg-white/20"
              style={{
                boxShadow: '0 0 24px -6px rgba(var(--accent-rgb), 0.65)',
              }}
            >
              <span>Set wallpaper</span>
              <span className="inline-flex h-5 w-5 items-center justify-center rounded-md border border-white/15 bg-black/30 font-mono text-[10px] text-zinc-300">
                <CornerDownLeft className="h-3 w-3" />
              </span>
            </button>
          </div>

          {/* Search & Category Filter */}
          <div className="flex flex-1 items-center justify-end gap-2">
            <div className="hidden lg:flex items-center gap-1 rounded-xl border border-white/10 bg-black/30 p-0.5">
              {CATEGORIES.map((cat) => (
                <button
                  key={cat.id}
                  type="button"
                  onClick={() => setCategory(cat.id)}
                  className={`rounded-lg px-2.5 py-1 text-[11px] font-medium transition ${
                    category === cat.id
                      ? 'bg-white/12 text-white'
                      : 'text-zinc-400 hover:text-zinc-200'
                  }`}
                >
                  {cat.label}
                </button>
              ))}
            </div>

            {searchOpen || theme === 'classic-grid' || theme === 'skewed-accordion' ? (
              <div className="relative flex items-center">
                <Search className="pointer-events-none absolute left-2.5 h-3.5 w-3.5 text-zinc-400" />
                <input
                  ref={searchInputRef}
                  type="text"
                  value={query}
                  onChange={(e) => setQuery(e.target.value)}
                  placeholder="Search space, aurora, jwst... (/)"
                  className="w-44 sm:w-56 rounded-xl border border-white/15 bg-black/45 py-1.5 pl-8 pr-7 text-xs text-white placeholder-zinc-500 outline-none transition focus:border-white/30"
                />
                {query && (
                  <button
                    type="button"
                    onClick={() => setQuery('')}
                    className="absolute right-2 text-zinc-400 hover:text-white"
                    aria-label="Clear search"
                  >
                    <X className="h-3.5 w-3.5" />
                  </button>
                )}
              </div>
            ) : (
              <button
                type="button"
                onClick={() => {
                  flashKey('/');
                  setSearchOpen(true);
                  setTimeout(() => searchInputRef.current?.focus(), 40);
                }}
                className="inline-flex items-center gap-1.5 rounded-xl border border-white/10 bg-white/5 px-2.5 py-1.5 text-xs text-zinc-300 hover:bg-white/10 hover:text-white"
              >
                <Search className="h-3.5 w-3.5" />
                <span className="hidden sm:inline">Search</span>
                <kbd className="rounded bg-white/10 px-1 font-mono text-[10px]">/</kbd>
              </button>
            )}
          </div>
        </div>

        {/* Mobile category strip */}
        <div className="relative z-20 flex lg:hidden items-center gap-1 overflow-x-auto border-b border-white/[0.05] bg-black/20 px-4 py-2">
          <SlidersHorizontal className="mr-1 h-3.5 w-3.5 shrink-0 text-zinc-500" />
          {CATEGORIES.map((cat) => (
            <button
              key={cat.id}
              type="button"
              onClick={() => setCategory(cat.id)}
              className={`shrink-0 rounded-lg px-2.5 py-1 text-[11px] font-medium transition ${
                category === cat.id ? 'bg-white/15 text-white' : 'text-zinc-400'
              }`}
            >
              {cat.label}
            </button>
          ))}
        </div>

        {/* Theme Render Area */}
        <div className="relative z-10 min-h-[430px] sm:min-h-[470px] flex flex-col justify-center px-3 py-6 sm:px-6">
          {filtered.length === 0 ? (
            <div className="flex flex-col items-center justify-center py-16 text-center">
              <Sparkles className="mb-3 h-8 w-8 text-zinc-500" />
              <p className="text-sm font-medium text-zinc-300">
                No wallpapers matched &ldquo;{query}&rdquo;
              </p>
              <button
                type="button"
                onClick={() => {
                  setQuery('');
                  setCategory('all');
                }}
                className="mt-3 rounded-xl border border-white/10 bg-white/5 px-3 py-1.5 text-xs text-white hover:bg-white/10"
              >
                Reset filters
              </button>
            </div>
          ) : (
            <AnimatePresence mode="wait">
              {theme === 'arc-carousel' && (
                <ArcCarouselView
                  key="arc"
                  items={filtered}
                  activeIndex={activeIndex}
                  favorites={favorites}
                  copiedHex={copiedHex}
                  onSelect={selectWallpaper}
                  onCopySwatch={handleCopySwatch}
                  onStep={stepIndex}
                />
              )}

              {theme === 'skewed-accordion' && (
                <SkewedAccordionView
                  key="skewed"
                  items={filtered}
                  activeIndex={activeIndex}
                  hoveredIdx={hoveredIdx}
                  setHoveredIdx={setHoveredIdx}
                  onSelect={selectWallpaper}
                  onCopySwatch={handleCopySwatch}
                />
              )}

              {theme === 'centered-carousel' && (
                <CenteredCarouselView
                  key="centered"
                  items={filtered}
                  activeIndex={activeIndex}
                  onSelect={selectWallpaper}
                  onCopySwatch={handleCopySwatch}
                  onStep={stepIndex}
                />
              )}

              {theme === 'classic-grid' && (
                <ClassicGridView
                  key="grid"
                  items={filtered}
                  activeWallpaperId={activeWallpaper.id}
                  favorites={favorites}
                  onSelect={selectWallpaper}
                  onToggleFav={toggleFavorite}
                  onApply={applyWallpaper}
                />
              )}
            </AnimatePresence>
          )}
        </div>

        {/* Bottom Keycap Hint Bar + Counter (just like real WallPicker) */}
        <div className="relative z-20 flex flex-wrap items-center justify-between gap-3 border-t border-white/[0.06] bg-black/45 px-4 py-2.5 text-[11px] text-zinc-400 backdrop-blur-md">
          <div className="flex flex-wrap items-center gap-2 sm:gap-3">
            <button
              type="button"
              onClick={() => {
                flashKey('left');
                stepIndex(-1);
              }}
              className={`inline-flex items-center gap-1 rounded-lg px-1.5 py-0.5 transition ${
                pressedKey === 'left' ? 'bg-white/20 text-white' : 'hover:text-white'
              }`}
            >
              <kbd className="rounded border border-white/15 bg-white/10 px-1.5 py-0.5 font-mono text-[10px] text-zinc-200">
                ←
              </kbd>
              <span>prev</span>
            </button>

            <button
              type="button"
              onClick={() => {
                flashKey('right');
                stepIndex(1);
              }}
              className={`inline-flex items-center gap-1 rounded-lg px-1.5 py-0.5 transition ${
                pressedKey === 'right' ? 'bg-white/20 text-white' : 'hover:text-white'
              }`}
            >
              <kbd className="rounded border border-white/15 bg-white/10 px-1.5 py-0.5 font-mono text-[10px] text-zinc-200">
                →
              </kbd>
              <span>next</span>
            </button>

            <button
              type="button"
              onClick={() => {
                flashKey('enter');
                applyWallpaper();
              }}
              className={`inline-flex items-center gap-1 rounded-lg px-1.5 py-0.5 transition ${
                pressedKey === 'enter' ? 'bg-white/20 text-white' : 'hover:text-white'
              }`}
            >
              <kbd className="rounded border border-white/15 bg-white/10 px-1.5 py-0.5 font-mono text-[10px] text-zinc-200">
                Enter
              </kbd>
              <span>set</span>
            </button>

            <button
              type="button"
              onClick={() => {
                flashKey('/');
                setSearchOpen(true);
                setTimeout(() => searchInputRef.current?.focus(), 40);
              }}
              className={`inline-flex items-center gap-1 rounded-lg px-1.5 py-0.5 transition ${
                pressedKey === '/' ? 'bg-white/20 text-white' : 'hover:text-white'
              }`}
            >
              <kbd className="rounded border border-white/15 bg-white/10 px-1.5 py-0.5 font-mono text-[10px] text-zinc-200">
                /
              </kbd>
              <span>search</span>
            </button>

            <button
              type="button"
              onClick={() => {
                flashKey('f');
                toggleFavorite(activeWallpaper.id);
              }}
              className={`inline-flex items-center gap-1 rounded-lg px-1.5 py-0.5 transition ${
                pressedKey === 'f' ? 'bg-white/20 text-white' : 'hover:text-white'
              }`}
            >
              <kbd className="rounded border border-white/15 bg-white/10 px-1.5 py-0.5 font-mono text-[10px] text-zinc-200">
                F
              </kbd>
              <span>fav</span>
            </button>

            <span className="hidden md:inline-flex items-center gap-1 text-zinc-500">
              • Click any swatch to sync site accent
            </span>
          </div>

          <div className="flex items-center gap-2 font-mono text-[11px] text-zinc-400">
            {currentItem && <span className="hidden sm:inline text-zinc-500">{currentItem.credit}</span>}
            <span className="text-zinc-600">•</span>
            <span>fill</span>
            <span className="text-zinc-600">•</span>
            <span className="text-zinc-200">
              {filtered.length === 0 ? '0 / 0' : `${activeIndex + 1} / ${filtered.length}`}
            </span>
          </div>
        </div>
      </div>

      {/* Native WPF Screenshot Modal */}
      <AnimatePresence>
        {showNativeModal && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            onClick={() => setShowNativeModal(false)}
            className="fixed inset-0 z-50 flex items-center justify-center bg-black/80 p-4 backdrop-blur-md"
          >
            <motion.div
              initial={{ scale: 0.94, opacity: 0 }}
              animate={{ scale: 1, opacity: 1 }}
              exit={{ scale: 0.94, opacity: 0 }}
              onClick={(e) => e.stopPropagation()}
              className="glass-panel relative w-full max-w-4xl overflow-hidden rounded-3xl p-4 sm:p-6"
            >
              <div className="mb-3 flex items-center justify-between">
                <div>
                  <h3 className="font-display text-lg font-bold text-white">
                    Native Windows WPF Picker &amp; Tray Daemon
                  </h3>
                  <p className="text-xs text-zinc-400">
                    Real screenshots from WallpaperSuite running on Windows 11
                  </p>
                </div>
                <button
                  type="button"
                  onClick={() => {
                    setShowNativeModal(false);
                    showToast('Returned to interactive web playground');
                  }}
                  className="rounded-xl border border-white/10 bg-white/5 p-2 text-zinc-300 hover:bg-white/10 hover:text-white"
                >
                  <X className="h-4 w-4" />
                </button>
              </div>
              <div className="grid gap-4 md:grid-cols-[1fr_240px]">
                <div className="overflow-hidden rounded-2xl border border-white/10 bg-black/40">
                  <img
                    src="./screenshots/picker.png"
                    alt="Native WallpaperSuite WPF Arc Carousel Picker"
                    className="h-full w-full object-cover"
                  />
                </div>
                <div className="flex flex-col justify-between overflow-hidden rounded-2xl border border-white/10 bg-black/40 p-3">
                  <img
                    src="./screenshots/tray.png"
                    alt="WallpaperSuite System Tray Menu"
                    className="mx-auto rounded-xl border border-white/10"
                  />
                  <div className="mt-3 text-xs text-zinc-400">
                    <p className="font-semibold text-zinc-200">walld.exe + wallpicker.exe</p>
                    <p className="mt-1">
                      Tray daemon idles at ~13 MB RAM; WPF picker opens on demand and closes cleanly on apply or Esc.
                    </p>
                  </div>
                </div>
              </div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}

/* ============================================================================
   1. ARC CAROUSEL THEME (Matches docs/screenshots/picker.png!)
============================================================================ */
function ArcCarouselView({
  items,
  activeIndex,
  favorites,
  copiedHex,
  onSelect,
  onCopySwatch,
  onStep,
}: {
  items: WallpaperItem[];
  activeIndex: number;
  favorites: Set<string>;
  copiedHex: string | null;
  onSelect: (wp: WallpaperItem) => void;
  onCopySwatch: (hex: string, e?: React.MouseEvent) => void;
  onStep: (delta: number) => void;
}) {
  // Show up to 7 cards around activeIndex in a curved arc
  const visibleOffsets = [-3, -2, -1, 0, 1, 2, 3];

  return (
    <motion.div
      initial={{ opacity: 0, y: 12 }}
      animate={{ opacity: 1, y: 0 }}
      exit={{ opacity: 0, y: -12 }}
      transition={{ duration: 0.25 }}
      className="relative flex h-[395px] w-full items-center justify-center overflow-hidden select-none"
    >
      {/* Left/Right quick arrows */}
      <button
        type="button"
        onClick={() => onStep(-1)}
        aria-label="Previous wallpaper"
        className="absolute left-2 z-30 flex h-9 w-9 items-center justify-center rounded-full border border-white/15 bg-black/55 text-zinc-200 backdrop-blur-md transition hover:scale-110 hover:border-white/30 hover:text-white"
      >
        <ChevronLeft className="h-5 w-5" />
      </button>
      <button
        type="button"
        onClick={() => onStep(1)}
        aria-label="Next wallpaper"
        className="absolute right-2 z-30 flex h-9 w-9 items-center justify-center rounded-full border border-white/15 bg-black/55 text-zinc-200 backdrop-blur-md transition hover:scale-110 hover:border-white/30 hover:text-white"
      >
        <ChevronRight className="h-5 w-5" />
      </button>

      {visibleOffsets.map((offset) => {
        if (items.length < 2 && offset !== 0) return null;
        const idx = (activeIndex + offset + items.length * 10) % items.length;
        const wp = items[idx];
        if (!wp) return null;

        const isCenter = offset === 0;
        const abs = Math.abs(offset);
        const translateX = offset * 155;
        const translateY = abs * abs * 13;
        const rotate = offset * 5.2;
        const scale = isCenter ? 1 : Math.max(0.76, 1 - abs * 0.085);
        const opacity = isCenter ? 1 : Math.max(0.32, 1 - abs * 0.24);
        const zIndex = 20 - abs * 4;

        const swatchRows: Array<{ label: string; hex: string }> = [
          { label: 'PRIMARY', hex: wp.palette.primary },
          { label: 'SECONDARY', hex: wp.palette.secondary },
          { label: 'TERTIARY', hex: wp.palette.tertiary },
          { label: 'CONTAINER', hex: wp.palette.container },
          { label: 'SURFACE', hex: wp.palette.surface },
        ];

        return (
          <motion.div
            key={`${wp.id}-${offset}`}
            onClick={() => onSelect(wp)}
            animate={{
              x: translateX,
              y: translateY,
              rotate,
              scale,
              opacity,
            }}
            transition={{ type: 'spring', stiffness: 300, damping: 28 }}
            style={{ zIndex }}
            className={`absolute w-[250px] sm:w-[278px] cursor-pointer overflow-hidden rounded-2xl bg-[#10121a] transition-shadow ${
              isCenter
                ? 'ring-2 ring-[var(--accent)] shadow-[0_24px_60px_-10px_rgba(var(--accent-rgb),0.55)]'
                : 'border border-white/10 shadow-2xl hover:brightness-110'
            }`}
          >
            {/* Preview Image */}
            <div className="relative aspect-[16/10] w-full overflow-hidden bg-zinc-900">
              <img
                src={wp.src}
                alt={wp.title}
                loading="lazy"
                className="h-full w-full object-cover"
              />
              <div className="absolute right-2 top-2 rounded-md bg-black/65 px-2 py-0.5 font-mono text-[10px] font-semibold uppercase tracking-wider text-white backdrop-blur-md">
                // {wp.category}
              </div>
              {favorites.has(wp.id) && (
                <div className="absolute left-2 top-2 rounded-full bg-black/55 p-1 backdrop-blur-md">
                  <Heart className="h-3 w-3 fill-rose-400 text-rose-400" />
                </div>
              )}
            </div>

            {/* 5-Color Swatch Strip (Authentic WallPicker look) */}
            <div className="divide-y divide-black/20">
              {swatchRows.map((row) => {
                const isLight = isLightHex(row.hex);
                return (
                  <button
                    key={row.label}
                    type="button"
                    onClick={(e) => {
                      if (!isCenter) return;
                      onCopySwatch(row.hex, e);
                    }}
                    className={`flex w-full items-center justify-between px-2.5 py-1 font-mono text-[10px] transition ${
                      isCenter ? 'hover:brightness-110 cursor-pointer' : 'pointer-events-none'
                    }`}
                    style={{
                      backgroundColor: row.hex,
                      color: isLight ? '#0b0d14' : '#f4f4f5',
                    }}
                    title={isCenter ? `Click to sync accent & copy ${row.hex}` : undefined}
                  >
                    <span className="font-semibold tracking-wider opacity-85">{row.label}</span>
                    <span className="flex items-center gap-1 font-semibold">
                      {copiedHex === row.hex && isCenter ? (
                        <>
                          <Check className="h-2.5 w-2.5" /> COPIED
                        </>
                      ) : (
                        row.hex
                      )}
                    </span>
                  </button>
                );
              })}
            </div>

            {/* Footer Metadata */}
            <div className="bg-[#0b0d14] px-3 py-2">
              <p className="truncate font-mono text-[11px] font-semibold text-zinc-100">
                {wp.filename}
              </p>
              <p className="mt-0.5 font-mono text-[10px] text-zinc-500">
                JPG &bull; {wp.resolution} &bull; {wp.size}
              </p>
            </div>
          </motion.div>
        );
      })}
    </motion.div>
  );
}

/* ============================================================================
   2. SKEWED ACCORDION THEME (Slanted parallelogram slices + upright art)
============================================================================ */
function SkewedAccordionView({
  items,
  activeIndex,
  hoveredIdx,
  setHoveredIdx,
  onSelect,
  onCopySwatch,
}: {
  items: WallpaperItem[];
  activeIndex: number;
  hoveredIdx: number | null;
  setHoveredIdx: (idx: number | null) => void;
  onSelect: (wp: WallpaperItem) => void;
  onCopySwatch: (hex: string, e?: React.MouseEvent) => void;
}) {
  // Show a clean 7-slice window centered on activeIndex so slices stay dramatic & smooth
  const windowSize = Math.min(items.length, 7);
  const half = Math.floor(windowSize / 2);
  const slices = Array.from({ length: windowSize }, (_, i) => {
    const idx = (activeIndex - half + i + items.length * 10) % items.length;
    return { wp: items[idx], idx };
  });

  const expandedTargetIdx = hoveredIdx !== null ? hoveredIdx : activeIndex;
  const focusedWp = items[expandedTargetIdx] ?? items[activeIndex];

  return (
    <motion.div
      initial={{ opacity: 0, y: 12 }}
      animate={{ opacity: 1, y: 0 }}
      exit={{ opacity: 0, y: -12 }}
      transition={{ duration: 0.25 }}
      className="flex flex-col items-center gap-4"
    >
      <div className="flex h-[300px] sm:h-[325px] w-full items-stretch justify-center gap-2 overflow-hidden px-4 sm:px-8">
        {slices.map(({ wp, idx }) => {
          const isExpanded = idx === expandedTargetIdx;
          const isSelected = idx === activeIndex;

          return (
            <motion.button
              key={wp.id}
              type="button"
              onMouseEnter={() => setHoveredIdx(idx)}
              onMouseLeave={() => setHoveredIdx(null)}
              onClick={() => onSelect(wp)}
              animate={{
                flexGrow: isExpanded ? 4.4 : 0.75,
              }}
              transition={{ type: 'spring', stiffness: 260, damping: 26 }}
              className={`group relative h-full min-w-[38px] sm:min-w-[54px] basis-0 overflow-hidden rounded-xl border transition-colors ${
                isSelected
                  ? 'border-[var(--accent)] shadow-[0_0_32px_-8px_rgba(var(--accent-rgb),0.6)]'
                  : 'border-white/10 hover:border-white/25'
              }`}
              style={{
                transform: 'skewX(-11deg)',
              }}
            >
              {/* Counter-skewed upright artwork inside parallelogram */}
              <div
                className="absolute inset-[-18%] h-[136%] w-[136%]"
                style={{ transform: 'skewX(11deg)' }}
              >
                <img
                  src={wp.src}
                  alt={wp.title}
                  loading="lazy"
                  className={`h-full w-full object-cover transition duration-500 ${
                    isExpanded ? 'scale-100 brightness-100' : 'scale-105 brightness-50 saturate-75'
                  }`}
                />
              </div>

              {/* Gradient label overlay on expanded slice */}
              <div
                className={`pointer-events-none absolute inset-x-0 bottom-0 bg-gradient-to-t from-black/90 via-black/45 to-transparent p-4 text-left transition-opacity duration-300 ${
                  isExpanded ? 'opacity-100' : 'opacity-0'
                }`}
                style={{ transform: 'skewX(11deg)' }}
              >
                <span className="inline-block rounded-md bg-white/15 px-2 py-0.5 font-mono text-[10px] uppercase text-white backdrop-blur-md">
                  {wp.category}
                </span>
                <p className="mt-1 truncate font-display text-sm font-bold text-white">
                  {wp.title}
                </p>
              </div>
            </motion.button>
          );
        })}
      </div>

      {/* Bottom Active Info & Palette Pill */}
      {focusedWp && (
        <div className="flex flex-wrap items-center justify-center gap-3 rounded-2xl border border-white/10 bg-black/45 px-4 py-2 backdrop-blur-md">
          <div className="text-left">
            <p className="text-xs font-semibold text-white">{focusedWp.title}</p>
            <p className="font-mono text-[10px] text-zinc-400">
              {focusedWp.resolution} &bull; {focusedWp.size}
            </p>
          </div>
          <div className="flex items-center gap-1.5">
            {Object.values(focusedWp.palette).map((hex) => (
              <button
                key={hex}
                type="button"
                onClick={(e) => onCopySwatch(hex, e)}
                className="h-5 w-5 rounded-md border border-white/20 transition-transform hover:scale-115"
                style={{ backgroundColor: hex }}
                title={`Sync accent & copy ${hex}`}
              />
            ))}
          </div>
        </div>
      )}
    </motion.div>
  );
}

/* ============================================================================
   3. CENTERED CAROUSEL THEME (Prominent 16:9 center + angled dimmed sides)
============================================================================ */
function CenteredCarouselView({
  items,
  activeIndex,
  onSelect,
  onCopySwatch,
  onStep,
}: {
  items: WallpaperItem[];
  activeIndex: number;
  onSelect: (wp: WallpaperItem) => void;
  onCopySwatch: (hex: string, e?: React.MouseEvent) => void;
  onStep: (delta: number) => void;
}) {
  const prevIdx = (activeIndex - 1 + items.length) % items.length;
  const nextIdx = (activeIndex + 1) % items.length;
  const centerWp = items[activeIndex];
  const prevWp = items[prevIdx];
  const nextWp = items[nextIdx];

  return (
    <motion.div
      initial={{ opacity: 0, y: 12 }}
      animate={{ opacity: 1, y: 0 }}
      exit={{ opacity: 0, y: -12 }}
      transition={{ duration: 0.25 }}
      className="flex flex-col items-center justify-center gap-5"
    >
      <div className="relative flex w-full items-center justify-center gap-4 px-2 sm:px-6">
        {/* Left Side Preview */}
        {items.length > 1 && prevWp && (
          <motion.button
            type="button"
            onClick={() => onStep(-1)}
            whileHover={{ scale: 0.92, opacity: 0.78 }}
            className="hidden md:block w-[220px] shrink-0 overflow-hidden rounded-2xl border border-white/10 opacity-45 brightness-75 transition"
            style={{ transform: 'perspective(900px) rotateY(16deg) scale(0.88)' }}
          >
            <div className="aspect-video w-full">
              <img src={prevWp.src} alt={prevWp.title} className="h-full w-full object-cover" />
            </div>
          </motion.button>
        )}

        {/* Prominent 16:9 Hero Card */}
        {centerWp && (
          <motion.div
            key={centerWp.id}
            initial={{ opacity: 0.6, scale: 0.96 }}
            animate={{ opacity: 1, scale: 1 }}
            transition={{ type: 'spring', stiffness: 280, damping: 24 }}
            className="relative w-full max-w-[520px] overflow-hidden rounded-2xl border-2 bg-[#0c0e15]"
            style={{
              borderColor: 'var(--accent)',
              boxShadow: '0 26px 70px -14px rgba(var(--accent-rgb), 0.55)',
            }}
          >
            <div className="relative aspect-video w-full overflow-hidden">
              <img
                src={centerWp.src}
                alt={centerWp.title}
                className="h-full w-full object-cover"
              />
              <div className="absolute inset-0 bg-gradient-to-t from-black/80 via-transparent to-transparent" />
              <div className="absolute bottom-3 left-4 right-4 flex items-end justify-between gap-2">
                <div>
                  <span className="rounded-md bg-white/15 px-2 py-0.5 font-mono text-[10px] uppercase text-white backdrop-blur-md">
                    {centerWp.category}
                  </span>
                  <h4 className="mt-1 font-display text-base sm:text-lg font-bold text-white">
                    {centerWp.title}
                  </h4>
                </div>
                <span className="font-mono text-[11px] text-zinc-300">{centerWp.resolution}</span>
              </div>
            </div>
          </motion.div>
        )}

        {/* Right Side Preview */}
        {items.length > 1 && nextWp && (
          <motion.button
            type="button"
            onClick={() => onStep(1)}
            whileHover={{ scale: 0.92, opacity: 0.78 }}
            className="hidden md:block w-[220px] shrink-0 overflow-hidden rounded-2xl border border-white/10 opacity-45 brightness-75 transition"
            style={{ transform: 'perspective(900px) rotateY(-16deg) scale(0.88)' }}
          >
            <div className="aspect-video w-full">
              <img src={nextWp.src} alt={nextWp.title} className="h-full w-full object-cover" />
            </div>
          </motion.button>
        )}
      </div>

      {/* Palette Strip & Thumb Dots */}
      {centerWp && (
        <div className="flex flex-wrap items-center justify-center gap-3">
          <div className="flex items-center gap-1.5 rounded-xl border border-white/10 bg-black/45 px-3 py-1.5 backdrop-blur-md">
            {Object.entries(centerWp.palette).map(([key, hex]) => (
              <button
                key={key}
                type="button"
                onClick={(e) => onCopySwatch(hex, e)}
                className="group flex items-center gap-1.5 rounded-lg px-1.5 py-0.5 hover:bg-white/10"
                title={`Sync ${key}: ${hex}`}
              >
                <span
                  className="h-3.5 w-3.5 rounded-full border border-white/25"
                  style={{ backgroundColor: hex }}
                />
                <span className="hidden sm:inline font-mono text-[10px] text-zinc-300">{hex}</span>
              </button>
            ))}
          </div>
          <div className="flex items-center gap-1">
            {items.slice(0, 8).map((wp) => (
              <button
                key={wp.id}
                type="button"
                onClick={() => onSelect(wp)}
                aria-label={`Select ${wp.title}`}
                className={`h-1.5 rounded-full transition-all ${
                  wp.id === centerWp.id ? 'w-5 bg-[var(--accent)]' : 'w-1.5 bg-white/20 hover:bg-white/40'
                }`}
              />
            ))}
          </div>
        </div>
      )}
    </motion.div>
  );
}

/* ============================================================================
   4. CLASSIC GRID THEME (Responsive 16:9 grid with instant search & quick fav)
============================================================================ */
function ClassicGridView({
  items,
  activeWallpaperId,
  favorites,
  onSelect,
  onToggleFav,
  onApply,
}: {
  items: WallpaperItem[];
  activeWallpaperId: string;
  favorites: Set<string>;
  onSelect: (wp: WallpaperItem) => void;
  onToggleFav: (id: string) => void;
  onApply: (wp: WallpaperItem) => void;
}) {
  return (
    <motion.div
      initial={{ opacity: 0, y: 12 }}
      animate={{ opacity: 1, y: 0 }}
      exit={{ opacity: 0, y: -12 }}
      transition={{ duration: 0.25 }}
      className="grid max-h-[385px] grid-cols-2 gap-3 overflow-y-auto pr-1 sm:grid-cols-3 lg:grid-cols-4"
    >
      {items.map((wp) => {
        const active = wp.id === activeWallpaperId;
        const fav = favorites.has(wp.id);

        return (
          <div
            key={wp.id}
            onClick={() => onSelect(wp)}
            onDoubleClick={() => onApply(wp)}
            className={`group relative cursor-pointer overflow-hidden rounded-xl border bg-[#0e1018] transition-all ${
              active
                ? 'border-[var(--accent)] ring-1 ring-[var(--accent)] shadow-[0_0_24px_-6px_rgba(var(--accent-rgb),0.55)]'
                : 'border-white/10 hover:border-white/25'
            }`}
          >
            <div className="relative aspect-video w-full overflow-hidden">
              <img
                src={wp.src}
                alt={wp.title}
                loading="lazy"
                className="h-full w-full object-cover transition duration-300 group-hover:scale-105"
              />
              <button
                type="button"
                onClick={(e) => {
                  e.stopPropagation();
                  onToggleFav(wp.id);
                }}
                className="absolute right-2 top-2 rounded-lg bg-black/60 p-1.5 backdrop-blur-md transition hover:bg-black/80"
                aria-label="Toggle favorite"
              >
                <Heart
                  className="h-3 w-3"
                  style={{
                    color: fav ? '#fb7185' : '#d4d4d8',
                    fill: fav ? '#fb7185' : 'transparent',
                  }}
                />
              </button>
              <div className="absolute bottom-2 left-2 flex gap-1">
                {[wp.palette.primary, wp.palette.secondary, wp.palette.tertiary].map((hex) => (
                  <span
                    key={hex}
                    className="h-2.5 w-2.5 rounded-full border border-white/30"
                    style={{ backgroundColor: hex }}
                  />
                ))}
              </div>
            </div>
            <div className="flex items-center justify-between gap-2 px-2.5 py-2">
              <div className="min-w-0">
                <p className="truncate text-xs font-medium text-zinc-100">{wp.title}</p>
                <p className="font-mono text-[10px] text-zinc-500">{wp.category}</p>
              </div>
              {active && (
                <button
                  type="button"
                  onClick={(e) => {
                    e.stopPropagation();
                    onApply(wp);
                  }}
                  className="shrink-0 rounded-md px-2 py-0.5 font-mono text-[10px] font-semibold text-zinc-950"
                  style={{ backgroundColor: 'var(--accent)' }}
                >
                  Apply
                </button>
              )}
            </div>
          </div>
        );
      })}
    </motion.div>
  );
}

function isLightHex(hex: string): boolean {
  const clean = hex.replace('#', '');
  const num = parseInt(clean, 16);
  const r = (num >> 16) & 255;
  const g = (num >> 8) & 255;
  const b = num & 255;
  const luma = 0.299 * r + 0.587 * g + 0.114 * b;
  return luma > 165;
}
