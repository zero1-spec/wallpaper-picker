import React, { createContext, useContext, useEffect, useState, useCallback } from 'react';
import {
  BUILTIN_WALLPAPERS,
  WallpaperItem,
  WallpaperPalette,
  hexToRgbString,
} from '../data/wallpapers';

interface AccentContextValue {
  activeWallpaper: WallpaperItem;
  appliedWallpaper: WallpaperItem;
  accentColor: string;
  secondaryColor: string;
  palette: WallpaperPalette;
  favorites: Set<string>;
  toastMessage: string | null;
  selectWallpaper: (wp: WallpaperItem, customSwatch?: string) => void;
  selectSwatch: (hex: string) => void;
  applyWallpaper: (wp?: WallpaperItem) => void;
  toggleFavorite: (id: string) => void;
  showToast: (msg: string) => void;
}

const AccentContext = createContext<AccentContextValue | null>(null);

export function AccentProvider({ children }: { children: React.ReactNode }) {
  const [activeWallpaper, setActiveWallpaper] = useState<WallpaperItem>(BUILTIN_WALLPAPERS[0]);
  const [appliedWallpaper, setAppliedWallpaper] = useState<WallpaperItem>(BUILTIN_WALLPAPERS[0]);
  const [customSwatch, setCustomSwatch] = useState<string | null>(null);
  const [favorites, setFavorites] = useState<Set<string>>(
    () => new Set(['aurora-mesh', 'cosmic-cliffs', 'aurora-saana', 'contour-lines'])
  );
  const [toastMessage, setToastMessage] = useState<string | null>(null);

  const accentColor = customSwatch ?? activeWallpaper.palette.primary;
  const secondaryColor = activeWallpaper.palette.secondary;
  const palette = activeWallpaper.palette;

  const showToast = useCallback((msg: string) => {
    setToastMessage(msg);
  }, []);

  useEffect(() => {
    if (!toastMessage) return;
    const t = setTimeout(() => setToastMessage(null), 2600);
    return () => clearTimeout(t);
  }, [toastMessage]);

  // Sync live CSS variables so glows, badges, borders & buttons react instantaneously
  useEffect(() => {
    const root = document.documentElement.style;
    root.setProperty('--accent', accentColor);
    root.setProperty('--accent-rgb', hexToRgbString(accentColor));
    root.setProperty('--accent-secondary', secondaryColor);
    root.setProperty('--accent-secondary-rgb', hexToRgbString(secondaryColor));
    root.setProperty('--accent-tertiary', palette.tertiary);
    root.setProperty('--accent-container', palette.container);
    root.setProperty('--accent-surface', palette.surface);
  }, [accentColor, secondaryColor, palette]);

  const selectWallpaper = useCallback((wp: WallpaperItem, swatchOverride?: string) => {
    setActiveWallpaper(wp);
    setCustomSwatch(swatchOverride ?? null);
  }, []);

  const selectSwatch = useCallback(
    (hex: string) => {
      setCustomSwatch(hex);
      showToast(`Accent synced to ${hex.toUpperCase()}`);
    },
    [showToast]
  );

  const applyWallpaper = useCallback(
    (wp?: WallpaperItem) => {
      const target = wp ?? activeWallpaper;
      setActiveWallpaper(target);
      setAppliedWallpaper(target);
      setCustomSwatch(null);
      showToast(`Applied "${target.title}" • Accent synced`);
    },
    [activeWallpaper, showToast]
  );

  const toggleFavorite = useCallback(
    (id: string) => {
      setFavorites((prev) => {
        const next = new Set(prev);
        if (next.has(id)) {
          next.delete(id);
          showToast('Removed from Favorites');
        } else {
          next.add(id);
          showToast('Added to Favorites');
        }
        return next;
      });
    },
    [showToast]
  );

  return (
    <AccentContext.Provider
      value={{
        activeWallpaper,
        appliedWallpaper,
        accentColor,
        secondaryColor,
        palette,
        favorites,
        toastMessage,
        selectWallpaper,
        selectSwatch,
        applyWallpaper,
        toggleFavorite,
        showToast,
      }}
    >
      {children}
    </AccentContext.Provider>
  );
}

export function useAccent() {
  const ctx = useContext(AccentContext);
  if (!ctx) throw new Error('useAccent must be used within AccentProvider');
  return ctx;
}
