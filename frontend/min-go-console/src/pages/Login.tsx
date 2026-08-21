import { createSignal } from 'solid-js';
import { useNavigate } from '@solidjs/router';
import { login } from '../store/auth';

export default function Login() {
  const navigate = useNavigate();
  const [email, setEmail] = createSignal('');
  const [password, setPassword] = createSignal('');
  const [error, setError] = createSignal('');
  const [submitting, setSubmitting] = createSignal(false);

  const handleSubmit = async (e: Event) => {
    e.preventDefault();
    setError('');
    setSubmitting(true);

    try {
      const result = await login(email(), password());
      if (result.success) {
        navigate('/dashboard');
      } else {
        setError(result.message);
      }
    } catch {
      setError('服务器响应异常，请稍后重试');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div class="min-h-screen flex items-center justify-center bg-gray-50 dark:bg-dark-200 px-4">
      <div class="card max-w-md w-full">
        <div class="text-center mb-8">
          <h1 class="text-2xl font-bold">MinGo API网关</h1>
          <p class="text-secondary mt-2">登录到控制平面</p>
        </div>

        {error() && (
          <div class="mb-4 p-3 bg-danger/10 text-danger rounded-lg text-sm">
            {error()}
          </div>
        )}

        <form onSubmit={handleSubmit} class="space-y-4">
          <div>
            <label class="block text-sm font-medium mb-1">邮箱</label>
            <input
              type="email"
              class="input"
              value={email()}
              onInput={(e) => setEmail(e.currentTarget.value)}
              required
              placeholder="请输入邮箱"
            />
          </div>
          <div>
            <label class="block text-sm font-medium mb-1">密码</label>
            <input
              type="password"
              class="input"
              value={password()}
              onInput={(e) => setPassword(e.currentTarget.value)}
              required
              placeholder="请输入密码"
            />
          </div>
          <button type="submit" class="btn btn-primary w-full" disabled={submitting()}>
            {submitting() ? '登录中...' : '登录'}
          </button>
        </form>
      </div>
    </div>
  );
}
