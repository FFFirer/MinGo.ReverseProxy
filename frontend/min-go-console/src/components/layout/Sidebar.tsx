import { useLocation, useNavigate } from '@solidjs/router';
import { createEffect, createSignal, type Component } from 'solid-js';
import { FaSolidTachometer, FaSolidRandom, FaSolidServer, FaSolidShield, FaSolidLock, FaSolidLineChart, FaSolidListAlt, FaSolidCubes, FaSolidCog } from 'solid-icons/fa';

const navItems = [
  { path: '/dashboard', icon: 'fa-tachometer', label: '仪表盘' },
  { path: '/routes', icon: 'fa-random', label: '路由管理' },
  { path: '/clusters', icon: 'fa-server', label: '集群管理' },
  { path: '/security', icon: 'fa-shield', label: '安全管理' },
  { path: '/certificates', icon: 'fa-lock', label: '证书管理' },
  { path: '/monitoring', icon: 'fa-line-chart', label: '监控' },
  { path: '/logs', icon: 'fa-list-alt', label: '日志管理' },
  { path: '/instances', icon: 'fa-cubes', label: '实例管理' },
  { path: '/settings', icon: 'fa-cog', label: '设置' },
];

const iconMap: Record<string, Component> = {
  'fa-tachometer': FaSolidTachometer,
  'fa-random': FaSolidRandom,
  'fa-server': FaSolidServer,
  'fa-shield': FaSolidShield,
  'fa-lock': FaSolidLock,
  'fa-line-chart': FaSolidLineChart,
  'fa-list-alt': FaSolidListAlt,
  'fa-cubes': FaSolidCubes,
  'fa-cog': FaSolidCog,
};

export default function Sidebar() {
  const location = useLocation();
  const navigate = useNavigate();
  const [open, setOpen] = createSignal(false);

  // 响应式 sidebar 控制
  createEffect(() => {
    const handleResize = () => {
      if (window.innerWidth >= 768) setOpen(true);
      else setOpen(false);
    };
    handleResize();
    window.addEventListener('resize', handleResize);
    return () => window.removeEventListener('resize', handleResize);
  });

  return (
    <>
      {/* overlay */}
      {open() && (
        <div
          class="fixed inset-0 top-[61px] bg-black bg-opacity-50 z-30 md:hidden"
          onClick={() => setOpen(false)}
        />
      )}
      <aside
        class={`fixed inset-y-0 left-0 top-[61px] w-64 bg-white dark:bg-dark-100 border-r border-gray-200 dark:border-dark-200 flex flex-col transform transition-transform duration-300 ease-in-out z-40 ${
          open() ? 'translate-x-0' : '-translate-x-full'
        } md:translate-x-0`}
      >
        <nav class="flex-1 p-4 space-y-1 overflow-y-auto">
          {navItems.map((item) => (
            <a
              href={item.path}
              onClick={(e) => {
                e.preventDefault();
                navigate(item.path);
                if (window.innerWidth < 768) setOpen(false);
              }}
              class={`flex items-center space-x-3 px-3 py-2 rounded-lg transition-all duration-200 cursor-pointer ${
                location.pathname === item.path
                  ? 'bg-primary text-white'
                  : 'hover:bg-gray-100 dark:hover:bg-dark-200'
              }`}
            >
              {(() => { const Icon = iconMap[item.icon]; return <Icon class="w-6 text-center" />; })()}
              <span>{item.label}</span>
            </a>
          ))}
        </nav>
      </aside>
    </>
  );
}

export function useSidebar() {
  const [open, setOpen] = createSignal(false);
  return { open, toggle: () => setOpen(!open), close: () => setOpen(false) };
}
