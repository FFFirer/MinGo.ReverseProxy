import { createSignal } from 'solid-js';
import type { UserInfo } from '../types';

const [user, setUser] = createSignal<UserInfo | null>(null);
const [loading, setLoading] = createSignal(true);

export { user, setUser, loading, setLoading };
