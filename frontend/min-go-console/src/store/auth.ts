import { createSignal } from 'solid-js';
import type { UserInfo } from '../types';

const [user, setUser] = createSignal<UserInfo | null>(null);
const [loading, setLoading] = createSignal(true);

export { user, loading };

export async function fetchUser() {
  try {
    const res = await fetch('/api/auth/me', { credentials: 'include' });
    if (res.ok) {
      setUser(await res.json());
    } else {
      setUser(null);
    }
  } catch {
    setUser(null);
  } finally {
    setLoading(false);
  }
}

export async function login(email: string, password: string): Promise<{ success: boolean; message: string }> {
  const res = await fetch('/api/auth/login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
    body: JSON.stringify({ email, password }),
  });
  const data = await res.json();
  if (res.ok) {
    await fetchUser();
    return { success: true, message: data.message };
  }
  return { success: false, message: data.message };
}

export async function register(email: string, password: string): Promise<{ success: boolean; message: string }> {
  const res = await fetch('/api/auth/register', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
    body: JSON.stringify({ email, password }),
  });
  const data = await res.json();
  if (res.ok) {
    await fetchUser();
    return { success: true, message: data.message };
  }
  return { success: false, message: data.message };
}

export async function logout() {
  await fetch('/api/auth/logout', { method: 'POST', credentials: 'include' });
  setUser(null);
}
