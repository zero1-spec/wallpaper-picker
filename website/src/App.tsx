import { AnimatePresence, motion } from 'framer-motion';
import { CheckCircle2 } from 'lucide-react';
import { AccentProvider, useAccent } from './context/AccentContext';
import { useLatestRelease } from './hooks/useLatestRelease';
import { Navbar } from './components/Navbar';
import { HeroSection } from './components/HeroSection';
import { BentoSection } from './components/BentoSection';
import { AutomationSection } from './components/AutomationSection';
import { PrivacySection } from './components/PrivacySection';
import { HotkeysSection } from './components/HotkeysSection';
import { FooterCTA } from './components/FooterCTA';

function LandingPageContent() {
  const release = useLatestRelease();
  const { toastMessage, accentColor } = useAccent();

  return (
    <div className="relative min-h-screen bg-[#06070B] text-zinc-100">
      {/* Ultra-subtle film grain overlay */}
      <div className="film-grain" aria-hidden="true" />

      {/* Subtle global reactive ambient glow */}
      <div
        aria-hidden="true"
        className="pointer-events-none fixed inset-0 -z-10 overflow-hidden"
      >
        <div
          className="absolute -left-40 top-1/3 h-[520px] w-[520px] rounded-full opacity-15 blur-[140px] transition-colors duration-700"
          style={{ backgroundColor: 'var(--accent)' }}
        />
        <div
          className="absolute -right-40 bottom-1/4 h-[520px] w-[520px] rounded-full opacity-12 blur-[150px] transition-colors duration-700"
          style={{ backgroundColor: 'var(--accent-secondary)' }}
        />
      </div>

      <Navbar release={release} />

      <main>
        <HeroSection release={release} />
        <BentoSection />
        <AutomationSection />
        <PrivacySection />
        <HotkeysSection />
      </main>

      <FooterCTA release={release} />

      {/* Floating Accent-Synced Toast Notification */}
      <AnimatePresence>
        {toastMessage && (
          <motion.div
            initial={{ opacity: 0, y: 20, scale: 0.95 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            exit={{ opacity: 0, y: 12, scale: 0.96 }}
            transition={{ type: 'spring', stiffness: 400, damping: 28 }}
            className="glass-pill fixed bottom-5 right-5 z-50 flex items-center gap-2.5 rounded-2xl px-4 py-2.5 text-xs font-medium text-white shadow-2xl"
            style={{
              borderColor: 'rgba(var(--accent-rgb), 0.45)',
              boxShadow: '0 14px 40px -10px rgba(var(--accent-rgb), 0.45)',
            }}
          >
            <CheckCircle2 className="h-4 w-4 shrink-0" style={{ color: accentColor }} />
            <span>{toastMessage}</span>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}

export function App() {
  return (
    <AccentProvider>
      <LandingPageContent />
    </AccentProvider>
  );
}

export default App;
