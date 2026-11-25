import React, { useRef, useState } from 'react';

interface SpotlightCardProps {
  children: React.ReactNode;
  className?: string;
}

export function SpotlightCard({ children, className = '' }: SpotlightCardProps) {
  const ref = useRef<HTMLDivElement>(null);
  const [coords, setCoords] = useState({ x: 0, y: 0, opacity: 0 });

  const handleMouseMove = (e: React.MouseEvent<HTMLDivElement>) => {
    const el = ref.current;
    if (!el) return;
    const rect = el.getBoundingClientRect();
    setCoords({
      x: e.clientX - rect.left,
      y: e.clientY - rect.top,
      opacity: 1,
    });
  };

  const handleMouseLeave = () => {
    setCoords((c) => ({ ...c, opacity: 0 }));
  };

  return (
    <div
      ref={ref}
      onMouseMove={handleMouseMove}
      onMouseLeave={handleMouseLeave}
      className={`glass-panel group relative overflow-hidden rounded-2xl transition-colors duration-300 hover:border-white/15 ${className}`}
    >
      {/* Pointer-tracking radial spotlight */}
      <div
        className="pointer-events-none absolute -inset-px rounded-2xl transition-opacity duration-300"
        style={{
          opacity: coords.opacity,
          background: `radial-gradient(520px circle at ${coords.x}px ${coords.y}px, rgba(var(--accent-rgb), 0.14), transparent 42%)`,
        }}
      />
      {/* Border highlight mask */}
      <div
        className="pointer-events-none absolute inset-0 rounded-2xl transition-opacity duration-300"
        style={{
          opacity: coords.opacity,
          boxShadow: 'inset 0 0 0 1px rgba(var(--accent-rgb), 0.28)',
          maskImage: `radial-gradient(320px circle at ${coords.x}px ${coords.y}px, black, transparent 60%)`,
          WebkitMaskImage: `radial-gradient(320px circle at ${coords.x}px ${coords.y}px, black, transparent 60%)`,
        }}
      />
      <div className="relative z-10 h-full">{children}</div>
    </div>
  );
}
