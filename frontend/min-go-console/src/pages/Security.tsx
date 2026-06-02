import { createSignal } from 'solid-js';

export default function SecurityPage() {
  const [rateLimit, setRateLimit] = createSignal(1000);

  return (
    <div>
      <div class="mb-6">
        <h2 class="text-2xl font-bold mb-2">安全管理</h2>
        <p class="text-secondary">配置和管理安全策略</p>
      </div>

      <div class="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <div class="card">
          <h3 class="font-semibold mb-4">API密钥管理</h3>
          <div class="p-8 text-center text-secondary">
            <i class="fa fa-key text-4xl mb-4"></i>
            <p>API密钥管理功能开发中</p>
          </div>
        </div>

        <div class="card">
          <h3 class="font-semibold mb-4">IP访问控制</h3>
          <div class="space-y-6">
            <div>
              <div class="flex items-center justify-between mb-2">
                <h4 class="font-medium">IP白名单</h4>
                <button class="text-primary hover:text-primary/80 text-sm"><i class="fa fa-plus mr-1"></i>添加</button>
              </div>
              <div class="p-3 bg-gray-50 dark:bg-dark-200 rounded-lg">
                <p class="text-sm">192.168.1.0/24</p>
              </div>
            </div>
          </div>
        </div>

        <div class="card lg:col-span-2">
          <h3 class="font-semibold mb-4">速率限制</h3>
          <div class="grid grid-cols-1 md:grid-cols-3 gap-4">
            <div class="p-4 bg-gray-50 dark:bg-dark-200 rounded-lg">
              <h4 class="font-medium mb-2">全局限制</h4>
              <div>
                <label class="block text-sm text-secondary mb-1">请求/秒</label>
                <input type="number" value={rateLimit()} class="input" onInput={(e) => setRateLimit(Number(e.currentTarget.value))} />
              </div>
            </div>
            <div class="p-4 bg-gray-50 dark:bg-dark-200 rounded-lg">
              <h4 class="font-medium mb-2">按IP限制</h4>
              <div>
                <label class="block text-sm text-secondary mb-1">请求/秒</label>
                <input type="number" value="100" class="input" />
              </div>
            </div>
            <div class="p-4 bg-gray-50 dark:bg-dark-200 rounded-lg">
              <h4 class="font-medium mb-2">按密钥限制</h4>
              <div>
                <label class="block text-sm text-secondary mb-1">请求/秒</label>
                <input type="number" value="50" class="input" />
              </div>
            </div>
          </div>
          <div class="mt-4 flex justify-end">
            <button class="btn btn-primary"><i class="fa fa-save mr-2"></i>保存配置</button>
          </div>
        </div>
      </div>
    </div>
  );
}
