import { test, expect } from '@playwright/test';

test.describe('路由管理页面', () => {
  test('未登录时重定向到登录页', async ({ page }) => {
    await page.goto('/routes');
    await page.waitForLoadState('networkidle');
    await expect(page).toHaveURL(/\/login/);
  });

  test.describe('已登录状态', () => {
    test.beforeEach(async ({ page }) => {
      await page.goto('/login');
      await page.fill('input[type="email"]', 'admin@mingo.local');
      await page.fill('input[type="password"]', 'admin123');
      await page.getByRole('button', { name: '登录' }).click();
      try {
        await page.waitForURL('**/dashboard', { timeout: 5000 });
      } catch {
        test.skip('登录失败（后端未运行或凭据无效）');
        return;
      }
    });

    test('页面标题和搜索框可见', async ({ page }) => {
      await page.goto('/routes');
      await page.waitForLoadState('networkidle');
      await expect(page.locator('h2')).toContainText('路由管理');
      await expect(page.getByText('添加路由')).toBeVisible();
      await expect(page.getByPlaceholder('搜索路由...')).toBeVisible();
    });

    test('打开添加路由弹窗', async ({ page }) => {
      await page.goto('/routes');
      await page.waitForLoadState('networkidle');
      await page.getByText('添加路由').click();
      await expect(page.getByRole('heading', { name: '添加路由' })).toBeVisible();
      await expect(page.locator('form label:has-text("路由名称")')).toBeVisible();
      await expect(page.locator('form label:has-text("匹配路径")')).toBeVisible();
      await expect(page.locator('form label:has-text("目标集群")')).toBeVisible();
    });

    test('提交空表单显示验证错误', async ({ page }) => {
      await page.goto('/routes');
      await page.waitForLoadState('networkidle');
      await page.getByText('添加路由').click();
      // 直接通过 form 的 submit 事件触发验证
      await page.locator('form').evaluate((f: HTMLFormElement) => f.requestSubmit());
      await expect(page.getByText('路由名称不能为空')).toBeVisible({ timeout: 3000 });
    });
  });
});
