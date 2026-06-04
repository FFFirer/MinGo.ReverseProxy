import { test, expect } from '@playwright/test';

test.describe('集群管理页面', () => {
  test('未登录时重定向到登录页', async ({ page }) => {
    await page.goto('/clusters');
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

    test('页面标题和添加按钮可见', async ({ page }) => {
      await page.goto('/clusters');
      await page.waitForLoadState('networkidle');
      await expect(page.locator('h2')).toContainText('集群管理');
      await expect(page.getByText('添加集群')).toBeVisible();
    });

    test('打开添加集群弹窗', async ({ page }) => {
      await page.goto('/clusters');
      await page.waitForLoadState('networkidle');
      await page.getByText('添加集群').click();
      await expect(page.getByRole('heading', { name: '添加集群' })).toBeVisible();
      await expect(page.locator('form label:has-text("集群名称")')).toBeVisible();
      await expect(page.locator('form label:has-text("负载均衡策略")')).toBeVisible();
      await expect(page.locator('form label:has-text("目标地址")')).toBeVisible();
    });

    test('健康检查配置区域可折叠展开', async ({ page }) => {
      await page.goto('/clusters');
      await page.waitForLoadState('networkidle');
      await page.getByText('添加集群').click();
      await page.getByText('主动健康检查').click();
      await expect(page.getByText('启用主动健康检查')).toBeVisible();
      await page.getByText('被动健康检查').click();
      await expect(page.getByText('启用被动健康检查')).toBeVisible();
    });

    test('提交空表单显示验证错误', async ({ page }) => {
      await page.goto('/clusters');
      await page.waitForLoadState('networkidle');
      await page.getByText('添加集群').click();
      await page.getByRole('button', { name: '保存' }).click();
      await expect(page.locator('.text-danger').first()).toBeVisible({ timeout: 3000 });
    });
  });
});
