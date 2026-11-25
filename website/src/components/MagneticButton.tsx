import React, { useRef, useState } from 'react';
import { motion } from 'framer-motion';

interface MagneticButtonProps {
  children: React.ReactNode;
  href?: string;
  onClick?: () => void;
  className?: string;
  style?: React.CSSProperties;
  target?: string;
  rel?: string;
  strength?: number;
  type?: 'button' | 'submit';
  ariaLabel?: string;
}

export function MagneticButton({
  children,
  href,
  onClick,
  className = '',
  style,
  target,
  rel,
  strength = 0.22,
  type = 'button',
  ariaLabel,
}: MagneticButtonProps) {
  const ref = useRef<HTMLElement>(null);
  const [pos, setPos] = useState({ x: 0, y: 0 });

  const handleMouseMove = (e: React.MouseEvent<HTMLElement>) => {
    const el = ref.current;
    if (!el) return;
    const rect = el.getBoundingClientRect();
    const dx = e.clientX - (rect.left + rect.width / 2);
    const dy = e.clientY - (rect.top + rect.height / 2);
    setPos({ x: dx * strength, y: dy * strength });
  };

  const handleMouseLeave = () => setPos({ x: 0, y: 0 });

  const motionProps = {
    animate: { x: pos.x, y: pos.y },
    transition: { type: 'spring' as const, stiffness: 320, damping: 20, mass: 0.4 },
    onMouseMove: handleMouseMove,
    onMouseLeave: handleMouseLeave,
    className,
    style,
    'aria-label': ariaLabel,
  };

  if (href) {
    return (
      <motion.a
        ref={ref as React.RefObject<HTMLAnchorElement>}
        href={href}
        target={target}
        rel={rel}
        {...motionProps}
      >
        {children}
      </motion.a>
    );
  }

  return (
    <motion.button
      ref={ref as React.RefObject<HTMLButtonElement>}
      type={type}
      onClick={onClick}
      {...motionProps}
    >
      {children}
    </motion.button>
  );
}
