import { createSignal, onMount } from 'solid-js';
import { api } from '../api/client';
import type { CertificateConfig } from '../types';

export default function CertificatesPage() {
  const [certs, setCerts] = createSignal<CertificateConfig[]>([]);

  onMount(async () => {
    try {
      setCerts(await api.get<CertificateConfig[]>('/certificates'));
    } catch { /* ignore */ }
  });

  return (
    <div>
      <div class="mb-6 flex items-center justify-between">
        <div>
          <h2 class="text-2xl font-bold mb-2">证书管理</h2>
          <p class="text-secondary">管理TLS/SSL证书</p>
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
                <th class="text-left py-3 px-4 font-medium text-secondary">状态</th>
              </tr>
            </thead>
            <tbody>
              {certs().map((cert) => (
                <tr class="border-b border-gray-100 dark:border-dark-100 hover:bg-gray-50 dark:hover:bg-dark-100/50">
                  <td class="py-3 px-4 font-medium">{cert.domainName}</td>
                  <td class="py-3 px-4">{cert.certificateType}</td>
                  <td class="py-3 px-4 text-sm">{cert.subject || '-'}</td>
                  <td class="py-3 px-4">{cert.expiresAt ? new Date(cert.expiresAt).toLocaleDateString() : '-'}</td>
                  <td class="py-3 px-4">
                    <span class={`badge ${cert.isValid ? 'badge-success' : 'badge-danger'}`}>
                      {cert.isValid ? '有效' : '无效'}
                    </span>
                  </td>
                </tr>
              ))}
              {certs().length === 0 && (
                <tr><td colspan="5" class="py-8 text-center text-secondary">暂无证书</td></tr>
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
