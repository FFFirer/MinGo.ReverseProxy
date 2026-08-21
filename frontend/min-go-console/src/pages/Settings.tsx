import { createSignal } from 'solid-js';
import { addToast } from '../store/toast';
import { FaSolidUndo, FaSolidSave } from 'solid-icons/fa';

const DEFAULT_GATEWAY_NAME = 'MinGo API Gateway';

export default function SettingsPage() {
  const [gatewayName, setGatewayName] = createSignal(DEFAULT_GATEWAY_NAME);
  const [logLevel, setLogLevel] = createSignal('INFO');
  const [healthInterval, setHealthInterval] = createSignal('10');
  const [timeout, setTimeout_] = createSignal('30');

  const handleSave = () => {
    addToast('warning', '设置保存功能尚未开放，敬请期待');
  };

  const handleReset = () => {
    setGatewayName(DEFAULT_GATEWAY_NAME);
    setLogLevel('INFO');
    setHealthInterval('10');
    setTimeout_('30');
    addToast('success', '已恢复默认设置');
  };

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
                <select class="input" value={logLevel()} onChange={(e) => setLogLevel(e.currentTarget.value)}>
                  <option value="INFO">INFO</option>
                  <option value="DEBUG">DEBUG</option>
                  <option value="WARN">WARN</option>
                  <option value="ERROR">ERROR</option>
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
                  <input type="number" class="input" value={healthInterval()} onInput={(e) => setHealthInterval(e.currentTarget.value)} />
                  <span class="text-secondary">秒</span>
                </div>
              </div>
              <div>
                <label class="block text-sm font-medium mb-1">超时时间</label>
                <div class="flex items-center space-x-2">
                  <input type="number" class="input" value={timeout()} onInput={(e) => setTimeout_(e.currentTarget.value)} />
                  <span class="text-secondary">秒</span>
                </div>
              </div>
            </div>
          </div>
        </div>
        <div class="mt-6 flex justify-end space-x-2">
          <button class="btn btn-secondary" onClick={handleReset}><FaSolidUndo class="mr-2" />重置</button>
          <button class="btn btn-primary" onClick={handleSave}><FaSolidSave class="mr-2" />保存设置</button>
        </div>
      </div>
    </div>
  );
}
