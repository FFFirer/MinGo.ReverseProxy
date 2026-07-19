import { createSignal } from 'solid-js';

const [isDark, setIsDark] = createSignal(() => {
  if (typeof window === 'undefined') return false;
  const stored = localStorage.getItem('theme');
  if (stored) return stored === 'dark';
  return window.matchMedia('(prefers-color-scheme: dark)').matches;
});

function applyTheme(dark: boolean) {
  document.documentElement.classList.toggle('dark', dark);
  localStorage.setItem('theme', dark ? 'dark' : 'light');
}

export function toggleTheme() {
  const next = !isDark();
  setIsDark(next);
  applyTheme(next);
}

export function initTheme() {
  applyTheme(isDark());
}

export { isDark };
