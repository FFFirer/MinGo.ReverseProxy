import { createSignal, onMount, For } from 'solid-js';
import { api } from '../api/client';
import { addToast } from '../store/toast';
import type { CertificateConfig } from '../types';
import { FaSolidPlus, FaSolidUpload } from 'solid-icons/fa';

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
          <button class="btn btn-secondary" onClick={() => { setShowManual(true); setShowUpload(false); }}>
            <FaSolidPlus class="mr-2" />手动添加
          </button>
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
                          编辑
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

      {showManual() && (
        <CertFormModal
          cert={editingCert()}
          onClose={() => { setShowManual(false); setEditingCert(null); }}
          onDone={() => { setShowManual(false); setEditingCert(null); loadCerts(); }}
        />
      )}
    </div>
  );
}

function UploadCertModal(props: { onClose: () => void; onDone: () => void }) {
  const [domainName, setDomainName] = createSignal('');
  const [password, setPassword] = createSignal('');
  const [file, setFile] = createSignal<File | null>(null);
  const [uploading, setUploading] = createSignal(false);
  const [errors, setErrors] = createSignal<Record<string, string>>({});

  const handleSubmit = async (e: Event) => {
    e.preventDefault();
    const errs: Record<string, string> = {};
    if (!domainName().trim()) errs.domainName = '域名不能为空';
    if (!file()) errs.file = '请选择证书文件';
    setErrors(errs);
    if (Object.keys(errs).length > 0) return;

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
      addToast('success', '证书上传成功');
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
        <form onSubmit={handleSubmit} class="space-y-4">
          <div>
            <label class="block text-sm font-medium mb-1">域名</label>
            <input class="input" value={domainName()} onInput={(e) => setDomainName(e.currentTarget.value)} placeholder="example.com" />
            {errors().domainName && <p class="text-danger text-xs mt-1">{errors().domainName}</p>}
          </div>
          <div>
            <label class="block text-sm font-medium mb-1">证书文件</label>
            <input type="file" accept=".pfx,.cer,.crt,.pem" class="input"
              onChange={(e) => setFile(e.currentTarget.files?.[0] || null)} />
            {errors().file && <p class="text-danger text-xs mt-1">{errors().file}</p>}
          </div>
          <div>
            <label class="block text-sm font-medium mb-1">密码 <span class="text-secondary text-xs">(可选，PFX文件)</span></label>
            <input type="password" class="input" value={password()} onInput={(e) => setPassword(e.currentTarget.value)} />
          </div>
          <div class="flex justify-end space-x-2">
            <button type="button" class="btn btn-secondary" onClick={props.onClose}>取消</button>
            <button type="submit" class="btn btn-primary" disabled={uploading()}>
              {uploading() ? '上传中...' : '上传'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

function CertFormModal(props: { cert: CertificateConfig | null; onClose: () => void; onDone: () => void }) {
  const isEdit = !!props.cert;
  const [domainName, setDomainName] = createSignal(props.cert?.domainName || '');
  const [certType, setCertType] = createSignal(props.cert?.certificateType || 'Cer');
  const [subject, setSubject] = createSignal(props.cert?.subject || '');
  const [issuer, setIssuer] = createSignal(props.cert?.issuer || '');
  const [thumbprint, setThumbprint] = createSignal(props.cert?.thumbprint || '');
  const [expiresAt, setExpiresAt] = createSignal(props.cert?.expiresAt ? props.cert.expiresAt.slice(0, 10) : '');
  const [errors, setErrors] = createSignal<Record<string, string>>({});
  const [saving, setSaving] = createSignal(false);

  const validate = () => {
    const errs: Record<string, string> = {};
    if (!domainName().trim()) errs.domainName = '域名不能为空';
    setErrors(errs);
    return Object.keys(errs).length === 0;
  };

  const handleSubmit = async (e: Event) => {
    e.preventDefault();
    if (!validate()) return;

    setSaving(true);
    try {
      const body = {
        domainName: domainName(),
        certificateType: certType(),
        subject: subject() || undefined,
        issuer: issuer() || undefined,
        thumbprint: thumbprint() || undefined,
        expiresAt: expiresAt() ? new Date(expiresAt()).toISOString() : undefined,
      };

      if (isEdit) {
        await api.put(`/certificates/${props.cert!.id}`, { ...body, id: props.cert!.id });
        addToast('success', '证书已更新');
      } else {
        await api.post('/certificates', body);
        addToast('success', '证书已添加');
      }
      props.onDone();
    } catch (err) {
      addToast('error', `保存失败: ${(err as Error).message}`);
    } finally {
      setSaving(false);
    }
  };

  return (
    <div class="fixed inset-0 bg-black/50 flex items-center justify-center z-50" onClick={props.onClose}>
      <div class="card w-full max-w-lg mx-4" onClick={(e) => e.stopPropagation()}>
        <h3 class="font-semibold mb-4">{isEdit ? '编辑证书' : '手动添加证书'}</h3>
        <form onSubmit={handleSubmit} class="space-y-4">
          <div>
            <label class="block text-sm font-medium mb-1">域名</label>
            <input class="input" value={domainName()} onInput={(e) => setDomainName(e.currentTarget.value)} required />
            {errors().domainName && <p class="text-danger text-xs mt-1">{errors().domainName}</p>}
          </div>
          <div>
            <label class="block text-sm font-medium mb-1">证书类型</label>
            <select class="input" value={certType()} onChange={(e) => setCertType(e.currentTarget.value)}>
              <option value="Cer">CER</option>
              <option value="Pfx">PFX</option>
            </select>
          </div>
          <div>
            <label class="block text-sm font-medium mb-1">主题</label>
            <input class="input" value={subject()} onInput={(e) => setSubject(e.currentTarget.value)} />
          </div>
          <div>
            <label class="block text-sm font-medium mb-1">颁发者</label>
            <input class="input" value={issuer()} onInput={(e) => setIssuer(e.currentTarget.value)} />
          </div>
          <div>
            <label class="block text-sm font-medium mb-1">指纹</label>
            <input class="input" value={thumbprint()} onInput={(e) => setThumbprint(e.currentTarget.value)} />
          </div>
          <div>
            <label class="block text-sm font-medium mb-1">过期时间</label>
            <input type="date" class="input" value={expiresAt()} onInput={(e) => setExpiresAt(e.currentTarget.value)} />
          </div>
          <div class="flex justify-end space-x-2">
            <button type="button" class="btn btn-secondary" onClick={props.onClose}>取消</button>
            <button type="submit" class="btn btn-primary" disabled={saving()}>
              {saving() ? '保存中...' : '保存'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
