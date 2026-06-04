import { test, expect } from '@playwright/test';

test.describe('登录流程', () => {
  test('登录页面加载正常', async ({ page }) => {
    await page.goto('/login');
    await expect(page.locator('h1')).toContainText('MinGo API网关');
    await expect(page.getByRole('button', { name: '登录' })).toBeVisible();
    await expect(page.locator('input[type="email"]')).toBeVisible();
    await expect(page.locator('input[type="password"]')).toBeVisible();
  });

  test('切换到注册模式', async ({ page }) => {
    await page.goto('/login');
    // 点击"注册"链接按钮切换注册模式（"没有账户？注册"中的"注册"按钮）
    await page.locator('div.mt-4 button').click();
    // 验证切换到注册模式
    await expect(page.getByText('创建新账户')).toBeVisible({ timeout: 3000 });
    await expect(page.getByRole('button', { name: '注册' })).toBeVisible();
    // 切回登录模式
    await page.locator('div.mt-4 button').click();
    await expect(page.getByText('登录到控制平面')).toBeVisible();
  });
});
