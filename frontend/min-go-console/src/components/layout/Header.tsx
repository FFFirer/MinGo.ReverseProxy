import { toggleTheme, isDark } from '../../store/theme';
import { user, logout } from '../../store/auth';
import { useNavigate } from '@solidjs/router';
import { createSignal } from 'solid-js';
import { FaSolidBars, FaSolidSearch, FaSolidSun, FaSolidMoon, FaSolidBell, FaSolidUser } from 'solid-icons/fa';

export default function Header() {
  const navigate = useNavigate();
  const [searchQuery, setSearchQuery] = createSignal('');

  const handleLogout = async () => {
    await logout();
    navigate('/login');
  };

  return (
    <header class="bg-white dark:bg-dark-100 border-b border-gray-200 dark:border-dark-200 py-3 px-4 md:px-6 flex items-center justify-between z-50">
      <div class="flex items-center space-x-3">
        <button id="mobile-menu-button" class="md:hidden text-gray-500 dark:text-gray-400">
          <FaSolidBars class="text-xl" />
        </button>
        <div class="flex items-center space-x-3">
          <h1 class="text-lg md:text-xl font-semibold">MinGo API网关</h1>
          <span class="badge badge-success">运行中</span>
        </div>
      </div>
      <div class="flex items-center space-x-3">
        <div class="relative hidden md:block">
          <input
            type="text"
            placeholder="搜索..."
            class="input pl-10 pr-4 py-1 w-64"
            value={searchQuery()}
            onInput={(e) => setSearchQuery(e.currentTarget.value)}
          />
          <FaSolidSearch class="absolute left-3 top-1/2 transform -translate-y-1/2 text-gray-400" />
        </div>
        <button
          onClick={toggleTheme}
          class="flex items-center justify-center w-10 h-10 text-gray-500 dark:text-gray-400 transition-colors duration-300 hover:text-primary dark:hover:text-primary focus:outline-none focus:ring-2 focus:ring-primary/50 rounded-full"
        >
          {isDark() ? <FaSolidSun class="text-xl" /> : <FaSolidMoon class="text-xl" />}
        </button>
        <div class="hidden md:flex items-center space-x-3">
          <button class="flex items-center justify-center w-10 h-10 text-gray-500 dark:text-gray-400 hover:text-primary rounded-full">
            <FaSolidBell class="text-xl" />
          </button>
          <div class="w-8 h-8 rounded-full bg-primary/20 flex items-center justify-center text-primary">
            <FaSolidUser />
          </div>
          <span class="font-medium">{user()?.userName || user()?.email || '管理员'}</span>
          <button onClick={handleLogout} class="text-sm text-secondary hover:text-danger transition-colors">
            退出
          </button>
        </div>
      </div>
    </header>
  );
}
