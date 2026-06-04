import { test, expect } from '@playwright/test';

test.describe('证书管理页面', () => {
  test('未登录时重定向到登录页', async ({ page }) => {
    await page.goto('/certificates');
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

    test('页面标题和操作按钮可见', async ({ page }) => {
      await page.goto('/certificates');
      await page.waitForLoadState('networkidle');
      await expect(page.locator('h2')).toContainText('证书管理');
      await expect(page.getByText('上传证书')).toBeVisible();
      await expect(page.getByText('手动添加')).toBeVisible();
    });

    test('打开上传证书弹窗', async ({ page }) => {
      await page.goto('/certificates');
      await page.waitForLoadState('networkidle');
      await page.getByText('上传证书').click();
      await expect(page.getByRole('heading', { name: '上传证书' })).toBeVisible();
      await expect(page.getByText('证书文件')).toBeVisible();
      await expect(page.getByText('密码')).toBeVisible();
    });

    test('打开手动添加证书弹窗', async ({ page }) => {
      await page.goto('/certificates');
      await page.waitForLoadState('networkidle');
      await page.getByText('手动添加').click();
      await expect(page.getByRole('heading', { name: '手动添加证书' })).toBeVisible();
      await expect(page.locator('form label:has-text("证书类型")')).toBeVisible();
      await expect(page.locator('form label:has-text("指纹")')).toBeVisible();
      await expect(page.locator('form label:has-text("过期时间")')).toBeVisible();
    });

    test('证书表格显示所有列', async ({ page }) => {
      await page.goto('/certificates');
      await page.waitForLoadState('networkidle');
      const headers = page.locator('table thead th');
      await expect(headers).toContainText(['域名', '类型', '主题', '过期时间', '过期倒计时', '状态', '操作']);
    });
  });
});
