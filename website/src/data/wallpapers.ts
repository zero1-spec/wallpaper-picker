export type WallpaperCategory = 'space' | 'nature' | 'night' | 'abstract';

export interface WallpaperPalette {
  primary: string;
  secondary: string;
  tertiary: string;
  container: string;
  surface: string;
}

export interface WallpaperItem {
  id: string;
  filename: string;
  title: string;
  category: WallpaperCategory;
  src: string;
  resolution: string;
  size: string;
  tags: string[];
  credit: string;
  palette: WallpaperPalette;
}

export const BUILTIN_WALLPAPERS: WallpaperItem[] = [
  {
    id: 'aurora-mesh',
    filename: 'aurora-mesh.jpg',
    title: 'Aurora Mesh Gradient',
    category: 'abstract',
    src: './wallpapers/abstract/aurora-mesh.jpg',
    resolution: '3840 × 2160',
    size: '755 KB',
    tags: ['abstract', 'mesh', 'indigo', 'gradient'],
    credit: 'WallpaperSuite Procedural Art (MIT)',
    palette: {
      primary: '#6B7BFF',
      secondary: '#38BDF8',
      tertiary: '#C066FF',
      container: '#222F66',
      surface: '#11162C',
    },
  },
  {
    id: 'cosmic-cliffs',
    filename: 'cosmic-cliffs-carina-nebula.jpg',
    title: 'Cosmic Cliffs — Carina Nebula',
    category: 'space',
    src: './wallpapers/space/cosmic-cliffs-carina-nebula.jpg',
    resolution: '3840 × 2224',
    size: '1.68 MB',
    tags: ['space', 'jwst', 'nebula', 'amber', 'cosmos'],
    credit: 'NASA, ESA, CSA, STScI (PD-Webb)',
    palette: {
      primary: '#F59E56',
      secondary: '#56A5EC',
      tertiary: '#D96B43',
      container: '#3D271E',
      surface: '#17151E',
    },
  },
  {
    id: 'aurora-saana',
    filename: 'aurora-over-saana-fell.jpg',
    title: 'Aurora over Saana Fell',
    category: 'night',
    src: './wallpapers/night/aurora-over-saana-fell.jpg',
    resolution: '3840 × 2159',
    size: '865 KB',
    tags: ['night', 'aurora', 'finland', 'emerald'],
    credit: 'WikiLucas00 (CC BY-SA 4.0)',
    palette: {
      primary: '#34E89E',
      secondary: '#38BDF8',
      tertiary: '#6EE7B7',
      container: '#13382E',
      surface: '#0C1A19',
    },
  },
  {
    id: 'paper-waves',
    filename: 'paper-waves.jpg',
    title: 'Layered Paper Sunset Waves',
    category: 'abstract',
    src: './wallpapers/abstract/paper-waves.jpg',
    resolution: '3840 × 2160',
    size: '424 KB',
    tags: ['abstract', 'sunset', 'waves', 'coral'],
    credit: 'WallpaperSuite Procedural Art (MIT)',
    palette: {
      primary: '#F45D78',
      secondary: '#FCA868',
      tertiary: '#9B4D8E',
      container: '#4B1D3F',
      surface: '#1F0C24',
    },
  },
  {
    id: 'machapuchare',
    filename: 'machapuchare-himalayas.jpg',
    title: 'Machapuchare — Himalayas',
    category: 'nature',
    src: './wallpapers/nature/machapuchare-himalayas.jpg',
    resolution: '3840 × 2160',
    size: '2.67 MB',
    tags: ['nature', 'mountains', 'nepal', 'alpine'],
    credit: 'Bijay Chaurasia (CC BY-SA 4.0)',
    palette: {
      primary: '#60A5FA',
      secondary: '#93C5FD',
      tertiary: '#C29B7B',
      container: '#223A5E',
      surface: '#121926',
    },
  },
  {
    id: 'contour-lines',
    filename: 'contour-lines.jpg',
    title: 'Glowing Topographic Contours',
    category: 'abstract',
    src: './wallpapers/abstract/contour-lines.jpg',
    resolution: '3840 × 2160',
    size: '1.73 MB',
    tags: ['abstract', 'topo', 'cyan', 'dark'],
    credit: 'WallpaperSuite Procedural Art (MIT)',
    palette: {
      primary: '#22D3EE',
      secondary: '#6366F1',
      tertiary: '#2DD4BF',
      container: '#113044',
      surface: '#091420',
    },
  },
  {
    id: 'hong-kong-night',
    filename: 'hong-kong-harbour-night.jpg',
    title: 'Victoria Harbour at Night',
    category: 'night',
    src: './wallpapers/night/hong-kong-harbour-night.jpg',
    resolution: '3840 × 2549',
    size: '2.45 MB',
    tags: ['night', 'city', 'skyline', 'neon', 'harbour'],
    credit: 'Diego Delso (CC BY-SA 3.0)',
    palette: {
      primary: '#FBBF24',
      secondary: '#38BDF8',
      tertiary: '#F472B6',
      container: '#2B2538',
      surface: '#101219',
    },
  },
  {
    id: 'earth-iss',
    filename: 'earth-thunderheads-from-iss.jpg',
    title: 'Earth Thunderheads from ISS',
    category: 'space',
    src: './wallpapers/space/earth-thunderheads-from-iss.jpg',
    resolution: '3840 × 2555',
    size: '1.09 MB',
    tags: ['space', 'earth', 'orbit', 'iss', 'blue'],
    credit: 'NASA / ISS Expedition 40 (PD-USGov-NASA)',
    palette: {
      primary: '#3B9EFF',
      secondary: '#A5C9FF',
      tertiary: '#6366F1',
      container: '#173459',
      surface: '#09101C',
    },
  },
  {
    id: 'bokeh-glow',
    filename: 'bokeh-glow.jpg',
    title: 'Warm Bokeh Lights',
    category: 'abstract',
    src: './wallpapers/abstract/bokeh-glow.jpg',
    resolution: '3840 × 2160',
    size: '928 KB',
    tags: ['abstract', 'bokeh', 'warm', 'magenta'],
    credit: 'WallpaperSuite Procedural Art (MIT)',
    palette: {
      primary: '#E86C8D',
      secondary: '#F29F67',
      tertiary: '#A855F7',
      container: '#401F32',
      surface: '#1C0D18',
    },
  },
  {
    id: 'lake-louise',
    filename: 'lake-louise-banff.jpg',
    title: 'Lake Louise — Banff',
    category: 'nature',
    src: './wallpapers/nature/lake-louise-banff.jpg',
    resolution: '3840 × 2154',
    size: '2.58 MB',
    tags: ['nature', 'lake', 'banff', 'canada', 'teal'],
    credit: 'Chensiyuan (CC BY-SA 4.0)',
    palette: {
      primary: '#38B2AC',
      secondary: '#90CDF4',
      tertiary: '#CBD5E1',
      container: '#1D3B44',
      surface: '#101C21',
    },
  },
  {
    id: 'lighthouse-trails',
    filename: 'lighthouse-star-trails.jpg',
    title: 'Circumpolar Star Trails',
    category: 'night',
    src: './wallpapers/night/lighthouse-star-trails.jpg',
    resolution: '3840 × 2160',
    size: '1.07 MB',
    tags: ['night', 'stars', 'lighthouse', 'cobalt'],
    credit: 'Antoine Lamielle (CC BY-SA 4.0)',
    palette: {
      primary: '#4F8CFF',
      secondary: '#60A5FA',
      tertiary: '#F59E0B',
      container: '#13274D',
      surface: '#091021',
    },
  },
  {
    id: 'madeira-forest',
    filename: 'madeira-misty-forest.jpg',
    title: 'Misty Laurel Forest — Madeira',
    category: 'nature',
    src: './wallpapers/nature/madeira-misty-forest.jpg',
    resolution: '3840 × 2560',
    size: '1.44 MB',
    tags: ['nature', 'forest', 'mist', 'olive', 'portugal'],
    credit: 'Dietmar Rabich (CC BY-SA 4.0)',
    palette: {
      primary: '#9BB87A',
      secondary: '#CBD5E1',
      tertiary: '#6E8550',
      container: '#2B3522',
      surface: '#141914',
    },
  },
  {
    id: 'tarantula-nebula',
    filename: 'tarantula-nebula.jpg',
    title: 'Tarantula Nebula — 30 Doradus',
    category: 'space',
    src: './wallpapers/space/tarantula-nebula.jpg',
    resolution: '3840 × 2221',
    size: '2.00 MB',
    tags: ['space', 'jwst', 'nebula', 'gold', 'deep-sky'],
    credit: 'NASA, ESA, CSA, STScI (PD-Webb)',
    palette: {
      primary: '#E5955D',
      secondary: '#93C5FD',
      tertiary: '#C06E52',
      container: '#38221D',
      surface: '#151214',
    },
  },
  {
    id: 'milky-way-ogwen',
    filename: 'milky-way-over-llyn-ogwen.jpg',
    title: 'Milky Way over Llyn Ogwen',
    category: 'night',
    src: './wallpapers/night/milky-way-over-llyn-ogwen.jpg',
    resolution: '3840 × 2106',
    size: '3.29 MB',
    tags: ['night', 'milky-way', 'wales', 'astro'],
    credit: 'John Badham (CC BY-SA 4.0)',
    palette: {
      primary: '#A78BFA',
      secondary: '#FDBA74',
      tertiary: '#60A5FA',
      container: '#29233D',
      surface: '#121118',
    },
  },
  {
    id: 'twelve-apostles',
    filename: 'twelve-apostles-coast.jpg',
    title: 'Twelve Apostles — Coast',
    category: 'nature',
    src: './wallpapers/nature/twelve-apostles-coast.jpg',
    resolution: '3840 × 2560',
    size: '2.06 MB',
    tags: ['nature', 'ocean', 'coast', 'australia'],
    credit: 'Dietmar Rabich (CC BY-SA 4.0)',
    palette: {
      primary: '#7FB3D5',
      secondary: '#D4AC7D',
      tertiary: '#94A3B8',
      container: '#223544',
      surface: '#111820',
    },
  },
  {
    id: 'sombrero-galaxy',
    filename: 'sombrero-galaxy.jpg',
    title: 'Sombrero Galaxy — M104',
    category: 'space',
    src: './wallpapers/space/sombrero-galaxy.jpg',
    resolution: '3840 × 2152',
    size: '1.75 MB',
    tags: ['space', 'hubble', 'galaxy', 'monochrome', 'gold'],
    credit: 'NASA/ESA Hubble Heritage Team (PD-Hubble)',
    palette: {
      primary: '#E2C799',
      secondary: '#CBD5E1',
      tertiary: '#94A3B8',
      container: '#2D2926',
      surface: '#111114',
    },
  },
];

export function hexToRgbString(hex: string): string {
  const clean = hex.replace('#', '');
  const num = parseInt(clean, 16);
  const r = (num >> 16) & 255;
  const g = (num >> 8) & 255;
  const b = num & 255;
  return `${r}, ${g}, ${b}`;
}
