import { createSignal } from 'solid-js';

export default function SettingsPage() {
  const [gatewayName, setGatewayName] = createSignal('MinGo API Gateway');

  return (
    <div>
      <div class="mb-6">
        <h2 class="text-2xl font-bold mb-2">设置</h2>
        <p class="text-secondary">配置网关全局设置</p>
      </div>

      <div class="card">
        <div class="grid grid-cols-1 lg:grid-cols-2 gap-6">
          <div>
            <h3 class="font-semibold mb-4">基本设置</h3>
            <div class="space-y-4">
              <div>
                <label class="block text-sm font-medium mb-1">网关名称</label>
                <input type="text" class="input" value={gatewayName()} onInput={(e) => setGatewayName(e.currentTarget.value)} />
              </div>
              <div>
                <label class="block text-sm font-medium mb-1">日志级别</label>
                <select class="input">
                  <option>INFO</option>
                  <option>DEBUG</option>
                  <option>WARN</option>
                  <option>ERROR</option>
                </select>
              </div>
            </div>
          </div>
          <div>
            <h3 class="font-semibold mb-4">高级设置</h3>
            <div class="space-y-4">
              <div>
                <label class="block text-sm font-medium mb-1">健康检查间隔</label>
                <div class="flex items-center space-x-2">
                  <input type="number" value="10" class="input" />
                  <span class="text-secondary">秒</span>
                </div>
              </div>
              <div>
                <label class="block text-sm font-medium mb-1">超时时间</label>
                <div class="flex items-center space-x-2">
                  <input type="number" value="30" class="input" />
                  <span class="text-secondary">秒</span>
                </div>
              </div>
            </div>
          </div>
        </div>
        <div class="mt-6 flex justify-end space-x-2">
          <button class="btn btn-secondary"><i class="fa fa-undo mr-2"></i>重置</button>
          <button class="btn btn-primary"><i class="fa fa-save mr-2"></i>保存设置</button>
        </div>
      </div>
    </div>
  );
}
