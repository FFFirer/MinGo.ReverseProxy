import { createSignal } from 'solid-js';
import { useNavigate } from '@solidjs/router';
import { login, register } from '../store/auth';

export default function Login() {
  const navigate = useNavigate();
  const [email, setEmail] = createSignal('');
  const [password, setPassword] = createSignal('');
  const [error, setError] = createSignal('');
  const [isRegister, setIsRegister] = createSignal(false);

  const handleSubmit = async (e: Event) => {
    e.preventDefault();
    setError('');

    const result = isRegister()
      ? await register(email(), password())
      : await login(email(), password());

    if (result.success) {
      navigate('/dashboard');
    } else {
      setError(result.message);
    }
  };

  return (
    <div class="min-h-screen flex items-center justify-center bg-gray-50 dark:bg-dark-200 px-4">
      <div class="card max-w-md w-full">
        <div class="text-center mb-8">
          <h1 class="text-2xl font-bold">MinGo API网关</h1>
          <p class="text-secondary mt-2">{isRegister() ? '创建新账户' : '登录到控制平面'}</p>
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
          <button type="submit" class="btn btn-primary w-full">
            {isRegister() ? '注册' : '登录'}
          </button>
        </form>

        <div class="mt-4 text-center text-sm text-secondary">
          {isRegister() ? (
            <>已有账户？<button onClick={() => setIsRegister(false)} class="text-primary hover:underline cursor-pointer">登录</button></>
          ) : (
            <>没有账户？<button onClick={() => setIsRegister(true)} class="text-primary hover:underline cursor-pointer">注册</button></>
          )}
        </div>
      </div>
    </div>
  );
}
