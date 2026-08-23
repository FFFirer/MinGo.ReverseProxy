import { createSignal, createEffect } from 'solid-js';

export type ThemeMode = 'light' | 'dark' | 'system';

const STORAGE_KEY = 'theme';
const systemDark = window.matchMedia('(prefers-color-scheme: dark)');

function readStoredMode(): ThemeMode {
  const stored = localStorage.getItem(STORAGE_KEY);
  if (stored === 'light' || stored === 'dark' || stored === 'system') return stored;
  return 'system';
}

function resolveDark(mode: ThemeMode): boolean {
  return mode === 'system' ? systemDark.matches : mode === 'dark';
}

// --- signals ---
const [mode, setMode] = createSignal<ThemeMode>(readStoredMode());
const [isDark, setIsDark] = createSignal(resolveDark(mode()));

function applyTheme(dark: boolean) {
  const el = document.documentElement;
  el.classList.toggle('dark', dark);
  el.setAttribute('data-theme', dark ? 'dark' : 'light');
  el.style.colorScheme = dark ? 'dark' : 'light';
  setIsDark(dark);
}

// keep in sync when mode changes
createEffect(() => {
  const m = mode();
  localStorage.setItem(STORAGE_KEY, m);
  applyTheme(resolveDark(m));
});

// follow system preference changes when in system mode
systemDark.addEventListener('change', () => {
  if (mode() === 'system') applyTheme(systemDark.matches);
});

// --- public API ---

/** Cycle: light → dark → system */
export function toggleTheme() {
  setMode(m => (m === 'light' ? 'dark' : m === 'dark' ? 'system' : 'light'));
}

export function initTheme() {
  applyTheme(isDark());
}

export { isDark, mode };
