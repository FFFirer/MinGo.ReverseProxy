import { createSignal, onMount, For, Show } from 'solid-js';
import { api } from '../api/client';
import { addToast } from '../store/toast';
import type { CertificateConfig, CertificateParseResult } from '../types';
import { FaSolidUpload } from 'solid-icons/fa';

export default function CertificatesPage() {
  const [certs, setCerts] = createSignal<CertificateConfig[]>([]);
  const [loading, setLoading] = createSignal(true);
  const [showUpload, setShowUpload] = createSignal(false);
  const [showManual, setShowManual] = createSignal(false);
  const [editingCert, setEditingCert] = createSignal<CertificateConfig | null>(null);

  const loadCerts = async () => {
    try {
      setCerts(await api.get<CertificateConfig[]>('/certificates'));
    } catch (err) {
      addToast('error', '加载证书列表失败');
    } finally {
      setLoading(false);
    }
  };

  onMount(loadCerts);

  const handleDelete = async (id: string) => {
    if (!confirm('确定删除此证书？')) return;
    try {
      await api.delete(`/certificates/${id}`);
      await loadCerts();
      addToast('success', '证书已删除');
    } catch (err) {
      addToast('error', `删除证书失败: ${(err as Error).message}`);
    }
  };

  const daysUntilExpiry = (cert: CertificateConfig): number | null => {
    if (!cert.expiresAt) return null;
    const diff = new Date(cert.expiresAt).getTime() - Date.now();
    return Math.ceil(diff / (1000 * 60 * 60 * 24));
  };

  return (
    <div>
      <div class="mb-6 flex items-center justify-between">
        <div>
          <h2 class="text-2xl font-bold mb-2">证书管理</h2>
          <p class="text-secondary">管理TLS/SSL证书</p>
        </div>
        <div class="flex space-x-2">
          <button class="btn btn-primary" onClick={() => { setShowUpload(true); setShowManual(false); }}>
            <FaSolidUpload class="mr-2" />上传证书
          </button>
        </div>
      </div>

      <div class="card">
        <div class="overflow-x-auto">
          <table class="w-full">
            <thead>
              <tr class="border-b border-gray-200 dark:border-dark-200">
                <th class="text-left py-3 px-4 font-medium text-secondary">域名</th>
                <th class="text-left py-3 px-4 font-medium text-secondary">类型</th>
                <th class="text-left py-3 px-4 font-medium text-secondary">主题</th>
                <th class="text-left py-3 px-4 font-medium text-secondary">过期时间</th>
                <th class="text-left py-3 px-4 font-medium text-secondary">过期倒计时</th>
                <th class="text-left py-3 px-4 font-medium text-secondary">状态</th>
                <th class="text-left py-3 px-4 font-medium text-secondary">操作</th>
              </tr>
            </thead>
            <tbody>
              <For each={certs()}>{(cert) => {
                const days = daysUntilExpiry(cert);
                return (
                  <tr class="border-b border-gray-100 dark:border-dark-100 hover:bg-gray-50 dark:hover:bg-dark-100/50">
                    <td class="py-3 px-4 font-medium">{cert.domainName}</td>
                    <td class="py-3 px-4">{cert.certificateType}</td>
                    <td class="py-3 px-4 text-sm">{cert.subject || '-'}</td>
                    <td class="py-3 px-4">{cert.expiresAt ? new Date(cert.expiresAt).toLocaleDateString() : '-'}</td>
                    <td class="py-3 px-4 text-sm">
                      {days !== null ? (
                        days > 0 ? (
                          <span class="text-success">{days} 天</span>
                        ) : (
                          <span class="text-danger">已过期 {Math.abs(days)} 天</span>
                        )
                      ) : '-'}
                    </td>
                    <td class="py-3 px-4">
                      <span class={`badge ${cert.isValid ? 'badge-success' : 'badge-danger'}`}>
                        {cert.isValid ? '有效' : '无效'}
                      </span>
                    </td>
                    <td class="py-3 px-4">
                      <div class="flex space-x-2">
                        <button class="btn-text btn-text-primary" onClick={() => { setEditingCert(cert); setShowManual(true); setShowUpload(false); }}>
                          查看
                        </button>
                        <button class="btn-text btn-text-danger" onClick={() => handleDelete(cert.id)}>
                          删除
                        </button>
                      </div>
                    </td>
                  </tr>
                );
              }}</For>
              {!loading() && certs().length === 0 && (
                <tr><td colspan="7" class="py-8 text-center text-secondary">暂无证书</td></tr>
              )}
              {loading() && (
                <tr><td colspan="7" class="py-8 text-center text-secondary">加载中...</td></tr>
              )}
            </tbody>
          </table>
        </div>
      </div>

      {showUpload() && (
        <UploadCertModal
          onClose={() => setShowUpload(false)}
          onDone={() => { setShowUpload(false); loadCerts(); }}
        />
      )}

      {showManual() && editingCert() && (
        <CertDetailModal
          cert={editingCert()!}
          onClose={() => { setShowManual(false); setEditingCert(null); }}
        />
      )}
    </div>
  );
}

function UploadCertModal(props: { onClose: () => void; onDone: () => void }) {
  const [password, setPassword] = createSignal('');
  const [file, setFile] = createSignal<File | null>(null);
  const [parsing, setParsing] = createSignal(false);
  const [uploading, setUploading] = createSignal(false);
  const [errors, setErrors] = createSignal<Record<string, string>>({});
  const [parsed, setParsed] = createSignal<CertificateParseResult | null>(null);
  const [domainName, setDomainName] = createSignal('');

  const handleParse = async () => {
    const errs: Record<string, string> = {};
    if (!file()) errs.file = '请选择证书文件';
    setErrors(errs);
    if (Object.keys(errs).length > 0) return;

    setParsing(true);
    try {
      const formData = new FormData();
      formData.append('certificateFile', file()!);
      if (password()) formData.append('password', password());

      const res = await fetch('/api/certificates/parse', {
        method: 'POST',
        credentials: 'include',
        body: formData,
      });
      if (!res.ok) {
        const text = await res.text();
        throw new Error(text || '解析失败');
      }
      const result: CertificateParseResult = await res.json();
      setParsed(result);
      setDomainName(result.domainName);
    } catch (err) {
      addToast('error', `解析失败: ${(err as Error).message}`);
    } finally {
      setParsing(false);
    }
  };

  const handleUpload = async () => {
    if (!domainName().trim()) {
      setErrors({ domainName: '域名不能为空' });
      return;
    }
    setErrors({});
    setUploading(true);
    try {
      const formData = new FormData();
      formData.append('certificateFile', file()!);
      formData.append('domainName', domainName());
      if (password()) formData.append('password', password());

      const res = await fetch('/api/certificates/upload', {
        method: 'POST',
        credentials: 'include',
        body: formData,
      });
      if (!res.ok) {
        const text = await res.text();
        throw new Error(text || '上传失败');
      }
      addToast('success', parsed()?.existingCertificateId ? '证书已更新' : '证书上传成功');
      props.onDone();
    } catch (err) {
      addToast('error', `上传失败: ${(err as Error).message}`);
    } finally {
      setUploading(false);
    }
  };

  return (
    <div class="fixed inset-0 bg-black/50 flex items-center justify-center z-50" onClick={props.onClose}>
      <div class="card w-full max-w-lg mx-4" onClick={(e) => e.stopPropagation()}>
        <h3 class="font-semibold mb-4">上传证书</h3>

        <Show when={!parsed()} fallback={
          <div class="space-y-4">
            <div class="bg-gray-50 dark:bg-dark-200 rounded-lg p-4 space-y-2 text-sm">
              <div class="flex justify-between">
                <span class="text-secondary">类型</span>
                <span class="font-medium">{parsed()!.certificateType}</span>
              </div>
              <div class="flex justify-between">
                <span class="text-secondary">主题</span>
                <span class="font-medium truncate ml-4" title={parsed()!.subject}>{parsed()!.subject || '-'}</span>
              </div>
              <div class="flex justify-between">
                <span class="text-secondary">颁发者</span>
                <span class="font-medium truncate ml-4" title={parsed()!.issuer}>{parsed()!.issuer || '-'}</span>
              </div>
              <div class="flex justify-between">
                <span class="text-secondary">有效期</span>
                <span class="font-medium">{new Date(parsed()!.notBefore).toLocaleDateString()} ~ {new Date(parsed()!.notAfter).toLocaleDateString()}</span>
              </div>
              <Show when={parsed()!.sanNames.length > 0}>
                <div class="flex justify-between items-start">
                  <span class="text-secondary">备用域名</span>
                  <div class="text-right ml-4">
                    <For each={parsed()!.sanNames}>{(name) =>
                      <span class="inline-block bg-blue-100 dark:bg-blue-900/30 text-blue-700 dark:text-blue-300 rounded px-1.5 py-0.5 text-xs mr-1 mb-1">{name}</span>
                    }</For>
                  </div>
                </div>
              </Show>
              <div class="flex justify-between">
                <span class="text-secondary">指纹</span>
                <span class="font-mono text-xs truncate ml-4" title={parsed()!.thumbprint}>{parsed()!.thumbprint}</span>
              </div>
            </div>

            <div>
              <label class="block text-sm font-medium mb-1">域名</label>
              <input class="input" value={domainName()} onInput={(e) => setDomainName(e.currentTarget.value)} placeholder="example.com" />
              {errors().domainName && <p class="text-danger text-xs mt-1">{errors().domainName}</p>}
              <Show when={parsed()!.sanNames.length > 1}>
                <p class="text-secondary text-xs mt-1">检测到多个域名，请确认或手动修改</p>
              </Show>
            </div>

            <Show when={parsed()!.existingCertificateId}>
              <div class="bg-blue-50 dark:bg-blue-900/20 border border-blue-200 dark:border-blue-700 rounded-lg p-3 text-sm">
                <p class="text-blue-700 dark:text-blue-300">
                  系统中已有相同证书（域名: {parsed()!.existingDomainName}），将自动更新
                </p>
              </div>
            </Show>

            <div class="flex justify-end space-x-2">
              <button type="button" class="btn btn-secondary" onClick={() => { setParsed(null); setDomainName(''); }}>重新选择</button>
              <button type="button" class="btn btn-primary" disabled={uploading()} onClick={handleUpload}>
                {uploading() ? '上传中...' : '确认上传'}
              </button>
            </div>
          </div>
        }>
          <div class="space-y-4">
            <div>
              <label class="block text-sm font-medium mb-1">证书文件</label>
              <input type="file" accept=".pfx,.cer,.crt,.pem" class="input"
                onChange={(e) => { setFile(e.currentTarget.files?.[0] || null); setParsed(null); setDomainName(''); }} />
              {errors().file && <p class="text-danger text-xs mt-1">{errors().file}</p>}
            </div>
            <div>
              <label class="block text-sm font-medium mb-1">密码 <span class="text-secondary text-xs">(可选，PFX文件)</span></label>
              <input type="password" class="input" value={password()} onInput={(e) => setPassword(e.currentTarget.value)} placeholder="输入证书密码" />
            </div>
            <div class="flex justify-end space-x-2">
              <button type="button" class="btn btn-secondary" onClick={props.onClose}>取消</button>
              <button type="button" class="btn btn-primary" disabled={parsing() || !file()} onClick={handleParse}>
                {parsing() ? '解析中...' : '解析证书'}
              </button>
            </div>
          </div>
        </Show>
      </div>
    </div>
  );
}

function CertDetailModal(props: { cert: CertificateConfig; onClose: () => void }) {
  const cert = props.cert;
  const days = cert.expiresAt ? Math.ceil((new Date(cert.expiresAt).getTime() - Date.now()) / (1000 * 60 * 60 * 24)) : null;

  return (
    <div class="fixed inset-0 bg-black/50 flex items-center justify-center z-50" onClick={props.onClose}>
      <div class="card w-full max-w-lg mx-4" onClick={(e) => e.stopPropagation()}>
        <h3 class="font-semibold mb-4">证书详情</h3>
        <div class="space-y-3 text-sm">
          <div class="flex justify-between">
            <span class="text-secondary">域名</span>
            <span class="font-medium">{cert.domainName}</span>
          </div>
          <div class="flex justify-between">
            <span class="text-secondary">类型</span>
            <span class="font-medium">{cert.certificateType}</span>
          </div>
          <div class="flex justify-between">
            <span class="text-secondary">主题</span>
            <span class="font-medium truncate ml-4" title={cert.subject}>{cert.subject || '-'}</span>
          </div>
          <div class="flex justify-between">
            <span class="text-secondary">颁发者</span>
            <span class="font-medium truncate ml-4" title={cert.issuer}>{cert.issuer || '-'}</span>
          </div>
          <div class="flex justify-between">
            <span class="text-secondary">过期时间</span>
            <span class="font-medium">{cert.expiresAt ? new Date(cert.expiresAt).toLocaleDateString() : '-'}</span>
          </div>
          <div class="flex justify-between">
            <span class="text-secondary">剩余天数</span>
            {days !== null ? (
              days > 0 ? <span class="text-success font-medium">{days} 天</span> : <span class="text-danger font-medium">已过期 {Math.abs(days)} 天</span>
            ) : <span class="font-medium">-</span>}
          </div>
          <div class="flex justify-between">
            <span class="text-secondary">状态</span>
            <span class={`badge ${cert.isValid ? 'badge-success' : 'badge-danger'}`}>{cert.isValid ? '有效' : '无效'}</span>
          </div>
          <div class="flex justify-between items-start">
            <span class="text-secondary">指纹</span>
            <span class="font-mono text-xs break-all ml-4 text-right" title={cert.thumbprint}>{cert.thumbprint}</span>
          </div>
        </div>
        <div class="flex justify-end mt-6">
          <button type="button" class="btn btn-secondary" onClick={props.onClose}>关闭</button>
        </div>
      </div>
    </div>
  );
}
