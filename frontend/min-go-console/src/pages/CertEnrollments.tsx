import { createSignal, onMount, Show } from 'solid-js';
import { addToast } from '../store/toast';
import { api } from '../api/client';
import type { CertificateEnrollment, CertManagerSettings, CertManagerConnectionResult, EnrollmentState } from '../types';
import type { Column } from '../components/shared/Table';
import Table from '../components/shared/Table';
import {
  FaSolidPlus,
  FaSolidSave,
  FaSolidCheck,
  FaSolidSpinner,
  FaSolidTrash,
  FaSolidSyncAlt,
} from 'solid-icons/fa';

const stateBadge: Record<EnrollmentState, string> = {
  Unrequested: 'badge-warning',
  Pending: 'badge-info',
  Synced: 'badge-success',
  Failed: 'badge-danger',
  Disabled: 'badge-warning',
};

const stateLabel: Record<EnrollmentState, string> = {
  Unrequested: '未申请',
  Pending: '申请中',
  Synced: '已同步',
  Failed: '失败',
  Disabled: '停用',
};

export default function CertEnrollmentsPage() {
  return (
    <div>
      <div class="mb-6">
        <h2 class="text-2xl font-bold mb-2">证书接入</h2>
        <p class="text-secondary">接入 MinGo.CertManager，登记域名后自动申请与续期 TLS 证书</p>
      </div>

      <CertManagerSettingsCard />
      <EnrollmentsCard />
    </div>
  );
}

// ── 连接设置（CertManager） ──

function CertManagerSettingsCard() {
  const [settings, setSettings] = createSignal<CertManagerSettings | null>(null);
  const [baseUrl, setBaseUrl] = createSignal('');
  const [apiKey, setApiKey] = createSignal('');
  const [enabled, setEnabled] = createSignal(true);
  const [timeoutSeconds, setTimeoutSeconds] = createSignal(30);
  const [loading, setLoading] = createSignal(true);
  const [saving, setSaving] = createSignal(false);
  const [testing, setTesting] = createSignal(false);
  const [testResult, setTestResult] = createSignal<CertManagerConnectionResult | null>(null);
  const [errors, setErrors] = createSignal<Record<string, string>>({});

  const applySettings = (s: CertManagerSettings) => {
    setSettings(s);
    setBaseUrl(s.baseUrl || '');
    setApiKey(s.apiKey || '');
    setEnabled(s.enabled);
    setTimeoutSeconds(s.timeoutSeconds || 30);
  };

  const load = async () => {
    setLoading(true);
    try {
      applySettings(await api.getCertManagerSettings());
    } catch (err) {
      addToast('error', `加载 CertManager 配置失败: ${(err as Error).message}`);
    } finally {
      setLoading(false);
    }
  };

  onMount(load);

  const validate = () => {
    const errs: Record<string, string> = {};
    const url = baseUrl().trim();
    if (!url) {
      errs.baseUrl = 'BaseUrl 不能为空';
    } else {
      try { new URL(url); } catch { errs.baseUrl = 'BaseUrl 格式不合法'; }
    }
    if (!apiKey().trim() && !settings()?.configured) {
      errs.apiKey = 'ApiKey 不能为空';
    }
    setErrors(errs);
    return Object.keys(errs).length === 0;
  };

  const handleSave = async () => {
    if (!validate()) return;
    setSaving(true);
    try {
      const saved = await api.saveCertManagerSettings({
        baseUrl: baseUrl().trim(),
        apiKey: apiKey(),
        enabled: enabled(),
        timeoutSeconds: timeoutSeconds(),
      });
      applySettings(saved);
      addToast('success', 'CertManager 接入配置已保存');
    } catch (err) {
      addToast('error', `保存失败: ${(err as Error).message}`);
    } finally {
      setSaving(false);
    }
  };

  const handleTest = async () => {
    setTesting(true);
    setTestResult(null);
    try {
      const result = await api.testCertManagerConnection();
      setTestResult(result);
      if (result.ok) {
        addToast('success', result.message);
      } else {
        addToast('error', `连接失败: ${result.message}`);
      }
    } catch (err) {
      addToast('error', `测试连接失败: ${(err as Error).message}`);
    } finally {
      setTesting(false);
    }
  };

  return (
    <div class="card">
      <div class="flex items-center justify-between gap-4 mb-4">
        <div>
          <h3 class="font-semibold">CertManager 连接</h3>
          <p class="text-sm text-secondary mt-1">
            全托管申请：登记域名后由 CertManager 向 Let's Encrypt 申请并自动续期，事件驱动同步回控制面。
          </p>
        </div>
        <span class={`badge shrink-0 ${settings()?.configured ? 'badge-success' : 'badge-warning'}`}>
          {settings()?.configured ? '已配置' : '未配置'}
        </span>
      </div>

      <Show when={loading()}>
        <div class="text-sm text-secondary py-6 text-center">正在加载...</div>
      </Show>

      <Show when={!loading()}>
        <div class="space-y-4">
          <div class="grid grid-cols-1 lg:grid-cols-2 gap-4">
            <div>
              <label class="block text-sm font-medium mb-1">BaseUrl</label>
              <input
                class="input font-mono"
                value={baseUrl()}
                onInput={(e) => setBaseUrl(e.currentTarget.value)}
                placeholder="http://100.102.18.111:5042"
              />
              {errors().baseUrl && <p class="text-danger text-xs mt-1">{errors().baseUrl}</p>}
            </div>
            <div>
              <label class="block text-sm font-medium mb-1">ApiKey</label>
              <input
                class="input font-mono"
                type="password"
                value={apiKey()}
                onInput={(e) => setApiKey(e.currentTarget.value)}
                placeholder={settings()?.configured ? '留空保持不变，输入新值覆盖' : 'CertManager External ApiKey'}
              />
              {errors().apiKey && <p class="text-danger text-xs mt-1">{errors().apiKey}</p>}
              <p class="text-xs text-secondary mt-1">读取时脱敏，服务端不回显明文；加密落库</p>
            </div>
          </div>

          <div class="flex items-center gap-6">
            <label class="flex items-center gap-2">
              <input
                type="checkbox"
                checked={enabled()}
                onChange={(e) => setEnabled(e.currentTarget.checked)}
              />
              <span class="text-sm font-medium">启用自动接入</span>
            </label>
            <div class="w-48">
              <label class="block text-sm font-medium mb-1">请求超时（秒）</label>
              <input
                type="number"
                class="input"
                value={timeoutSeconds()}
                onInput={(e) => setTimeoutSeconds(parseInt(e.currentTarget.value) || 30)}
                min="1"
                max="120"
              />
            </div>
          </div>

          <Show when={testResult()}>
            <div class="p-3 rounded-lg bg-gray-50 dark:bg-dark-200 text-xs">
              <p>
                <span class="text-secondary">最近测试：</span>
                <span class={testResult()!.ok ? 'text-success' : 'text-danger'}>
                  {testResult()!.ok ? '连接成功' : '连接失败'}
                </span>
              </p>
              <p class="mt-1 break-all">{testResult()!.message}</p>
            </div>
          </Show>

          <div class="flex justify-end gap-2 pt-2 border-t border-gray-200 dark:border-dark-200">
            <button class="btn btn-secondary" disabled={testing()} onClick={handleTest}>
              <Show when={testing()} fallback={<FaSolidCheck class="mr-2" />}>
                <FaSolidSpinner class="mr-2 animate-spin" />
              </Show>
              {testing() ? '测试中...' : '测试连接'}
            </button>
            <button class="btn btn-primary" disabled={saving()} onClick={handleSave}>
              <FaSolidSave class="mr-2" />
              {saving() ? '保存中...' : '保存'}
            </button>
          </div>
        </div>
      </Show>
    </div>
  );
}

// ── 登记列表 ──

function EnrollmentsCard() {
  const [enrollments, setEnrollments] = createSignal<CertificateEnrollment[]>([]);
  const [loading, setLoading] = createSignal(true);
  const [domain, setDomain] = createSignal('');
  const [isWildcard, setIsWildcard] = createSignal(false);
  const [useStaging, setUseStaging] = createSignal(false);
  const [submitting, setSubmitting] = createSignal(false);
  const [syncingId, setSyncingId] = createSignal<string | null>(null);
  const [formError, setFormError] = createSignal('');

  const load = async () => {
    setLoading(true);
    try {
      setEnrollments(await api.getCertEnrollments());
    } catch (err) {
      addToast('error', `加载登记列表失败: ${(err as Error).message}`);
    } finally {
      setLoading(false);
    }
  };

  onMount(load);

  const handleRegister = async (e: Event) => {
    e.preventDefault();
    const d = domain().trim();
    if (!d) {
      setFormError('域名不能为空');
      return;
    }
    setFormError('');
    setSubmitting(true);
    try {
      const created = await api.registerCertEnrollment({
        domain: d,
        isWildcard: isWildcard(),
        useStaging: useStaging(),
      });
      addToast('success', created.state === 'Synced'
        ? `域名 ${created.domain} 已登记，证书已同步`
        : `域名 ${created.domain} 已登记，正在申请证书`);
      setDomain('');
      setIsWildcard(false);
      setUseStaging(false);
      await load();
    } catch (err) {
      setFormError((err as Error).message);
    } finally {
      setSubmitting(false);
    }
  };

  const handleSync = async (id: string) => {
    setSyncingId(id);
    try {
      await api.syncCertEnrollment(id);
      addToast('success', '同步成功');
    } catch (err) {
      addToast('error', `同步失败: ${(err as Error).message}`);
    } finally {
      setSyncingId(null);
      await load();
    }
  };

  const handleDelete = async (e: CertificateEnrollment) => {
    if (!confirm(`确定移除域名 "${e.domain}" 的登记？已同步到网关的证书不受影响`)) return;
    try {
      await api.deleteCertEnrollment(e.id);
      addToast('success', `已移除 ${e.domain} 的登记`);
      await load();
    } catch (err) {
      addToast('error', `移除失败: ${(err as Error).message}`);
    }
  };

  const columns: Column<CertificateEnrollment>[] = [
    {
      key: 'domain',
      title: '域名',
      mobileVisible: true,
      cell: (e) => (
        <div class="flex items-center gap-2">
          <span class="font-medium">{e.domain}</span>
          <Show when={e.isWildcard}>
            <span class="badge badge-info text-xs">通配符</span>
          </Show>
        </div>
      ),
    },
    {
      key: 'state',
      title: '状态',
      mobileVisible: true,
      cell: (e) => <span class={`badge ${stateBadge[e.state] ?? 'badge-warning'}`}>{stateLabel[e.state] ?? e.state}</span>,
    },
    {
      key: 'staging',
      title: '环境',
      cell: (e) => (e.useStaging ? <span class="badge badge-warning text-xs">Staging</span> : <span class="badge badge-success text-xs">生产</span>),
    },
    {
      key: 'expiresAt',
      title: '到期时间',
      cell: (e) => (e.expiresAt ? new Date(e.expiresAt).toLocaleDateString() : '-'),
    },
    {
      key: 'lastSyncAt',
      title: '上次同步',
      cell: (e) => (e.lastSyncAt ? new Date(e.lastSyncAt).toLocaleString() : '-'),
    },
    {
      key: 'lastError',
      title: '备注',
      cell: (e) => (
        <span class="text-xs" classList={{ 'text-danger': !!e.lastError }}>
          {e.lastError ? e.lastError : e.lastThumbprint ? '已同步指纹' : '-'}
        </span>
      ),
    },
    {
      key: 'actions',
      title: '操作',
      mobileVisible: true,
      mobileDetailVisible: false,
      cell: (e) => (
        <div class="flex items-center justify-end gap-1">
          <button
            class="btn-icon btn-icon-primary"
            title="手动同步"
            disabled={syncingId() === e.id}
            onClick={(ev) => { ev.stopPropagation(); handleSync(e.id); }}
          >
            <Show when={syncingId() === e.id} fallback={<FaSolidSyncAlt />}>
              <FaSolidSpinner class="animate-spin" />
            </Show>
          </button>
          <button
            class="btn-icon btn-icon-danger"
            title="移除登记"
            onClick={(ev) => { ev.stopPropagation(); handleDelete(e); }}
          >
            <FaSolidTrash />
          </button>
        </div>
      ),
    },
  ];

  return (
    <div class="card mt-6">
      <div class="flex items-center justify-between gap-4 mb-4">
        <div>
          <h3 class="font-semibold">域名登记</h3>
          <p class="text-sm text-secondary mt-1">
            登记后自动向 CertManager 申请证书，签发完成后事件驱动同步 PFX 到网关并推送给数据面。
          </p>
        </div>
      </div>

      <form onSubmit={handleRegister} class="flex flex-wrap items-end gap-4 mb-4 pb-4 border-b border-gray-200 dark:border-dark-200">
        <div class="flex-1 min-w-56">
          <label class="block text-sm font-medium mb-1">域名</label>
          <input
            class="input font-mono"
            value={domain()}
            onInput={(e) => { setDomain(e.currentTarget.value); setFormError(''); }}
            placeholder="example.com（通配符证书登记裸域名）"
          />
        </div>
        <label class="flex items-center gap-2 pb-2">
          <input
            type="checkbox"
            checked={isWildcard()}
            onChange={(e) => setIsWildcard(e.currentTarget.checked)}
          />
          <span class="text-sm">通配符（*.域名）</span>
        </label>
        <label class="flex items-center gap-2 pb-2">
          <input
            type="checkbox"
            checked={useStaging()}
            onChange={(e) => setUseStaging(e.currentTarget.checked)}
          />
          <span class="text-sm">Staging 环境</span>
        </label>
        <button type="submit" class="btn btn-primary" disabled={submitting()}>
          <FaSolidPlus class="mr-2" />
          {submitting() ? '登记中...' : '登记域名'}
        </button>
      </form>
      <Show when={formError()}>
        <p class="text-danger text-xs -mt-2 mb-4">{formError()}</p>
      </Show>

      <Show when={loading()} fallback={
        <Table
          columns={columns}
          data={enrollments()}
          rowKey={(e) => e.id}
          emptyMessage="尚未登记任何域名"
        />
      }>
        <div class="py-12 text-center text-secondary">加载中...</div>
      </Show>
    </div>
  );
}
