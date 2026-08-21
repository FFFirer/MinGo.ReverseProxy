import { toggleTheme, isDark } from '../../store/theme';
import { user, logout } from '../../store/auth';
import { toggleSidebar } from '../../store/sidebar';
import { useNavigate } from '@solidjs/router';
import { FaSolidBars, FaSolidSun, FaSolidMoon, FaSolidBell, FaSolidUser } from 'solid-icons/fa';

export default function Header() {
  const navigate = useNavigate();

  const handleLogout = async () => {
    await logout();
    navigate('/login');
  };

  return (
    <header class="bg-white dark:bg-dark-100 border-b border-gray-200 dark:border-dark-200 py-3 px-4 md:px-6 flex items-center justify-between z-50">
      <div class="flex items-center space-x-3">
        <button
          class="md:hidden text-gray-500 dark:text-gray-400"
          onClick={toggleSidebar}
          aria-label="切换菜单"
        >
          <FaSolidBars class="text-xl" />
        </button>
        <div class="flex items-center space-x-3">
          <h1 class="text-lg md:text-xl font-semibold">MinGo API网关</h1>
          <span class="badge badge-success">运行中</span>
        </div>
      </div>
      <div class="flex items-center space-x-3">
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
