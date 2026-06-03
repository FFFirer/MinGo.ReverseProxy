import { createEffect, onCleanup, Suspense } from 'solid-js';
import { Router, Route, Navigate } from '@solidjs/router';
import Header from './components/layout/Header';
import Sidebar from './components/layout/Sidebar';
import Login from './pages/Login';
import Dashboard from './pages/Dashboard';
import RoutesPage from './pages/Routes';
import ClustersPage from './pages/Clusters';
import CertificatesPage from './pages/Certificates';
import MonitoringPage from './pages/Monitoring';
import LogsPage from './pages/Logs';
import InstancesPage from './pages/Instances';
import SettingsPage from './pages/Settings';
import SecurityPage from './pages/Security';
import { user, loading, fetchUser } from './store/auth';

function ProtectedLayout(props: { children: any }) {
  if (loading()) return <div class="flex items-center justify-center min-h-screen text-secondary">验证登录状态...</div>;
  if (!user()) return <Navigate href="/login" />;

  return (
    <div class="h-screen flex flex-col overflow-hidden">
      <Header />
      <div class="flex flex-1 overflow-hidden relative">
        <Sidebar />
        <main class="flex-1 overflow-y-auto p-6 md:ml-64">
          {props.children}
        </main>
      </div>
    </div>
  );
}

export default function App() {
  createEffect(() => {
    fetchUser();

    const onVisibility = () => {
      if (document.visibilityState === 'visible') fetchUser();
    };
    document.addEventListener('visibilitychange', onVisibility);
    onCleanup(() => document.removeEventListener('visibilitychange', onVisibility));
  });

  return (
    <Suspense fallback={<div class="flex items-center justify-center min-h-screen text-secondary">加载中...</div>}>
      <Router>
        <Route path="/login" component={Login} />
        <Route path="/" component={() => <Navigate href="/dashboard" />} />
        <Route path="/dashboard" component={() => <ProtectedLayout><Dashboard /></ProtectedLayout>} />
        <Route path="/routes" component={() => <ProtectedLayout><RoutesPage /></ProtectedLayout>} />
        <Route path="/clusters" component={() => <ProtectedLayout><ClustersPage /></ProtectedLayout>} />
        <Route path="/certificates" component={() => <ProtectedLayout><CertificatesPage /></ProtectedLayout>} />
        <Route path="/monitoring" component={() => <ProtectedLayout><MonitoringPage /></ProtectedLayout>} />
        <Route path="/logs" component={() => <ProtectedLayout><LogsPage /></ProtectedLayout>} />
        <Route path="/instances" component={() => <ProtectedLayout><InstancesPage /></ProtectedLayout>} />
        <Route path="/settings" component={() => <ProtectedLayout><SettingsPage /></ProtectedLayout>} />
        <Route path="/security" component={() => <ProtectedLayout><SecurityPage /></ProtectedLayout>} />
      </Router>
    </Suspense>
  );
}
