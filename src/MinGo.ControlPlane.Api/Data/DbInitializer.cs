using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MinGo.ControlPlane.Api.Data;

/// <summary>
/// 种子数据初始化器
/// 仅在开发环境下首次运行时创建默认管理员账户
/// </summary>
public static class DbInitializer
{
    public static async Task SeedDevelopmentDataAsync(IServiceProvider serviceProvider, IConfiguration configuration)
    {
        var userManager = serviceProvider.GetRequiredService<UserManager<IdentityUser>>();

        // 幂等：已有用户则跳过
        if (await userManager.Users.AnyAsync())
            return;

        var seedSection = configuration.GetSection("SeedData");
        var adminEmail = seedSection["AdminEmail"] ?? "admin@mingo.local";
        var adminPassword = seedSection["AdminPassword"] ?? "admin123";

        var adminUser = new IdentityUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(adminUser, adminPassword);

        if (result.Succeeded)
        {
            Console.WriteLine($"[Seed] Default admin account created: {adminEmail}");
        }
        else
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            Console.WriteLine($"[Seed] Failed to create admin account: {errors}");
        }
    }
}
