using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MinGo.Core.Entities;
using MinGo.Core.Interfaces;
using MinGo.Core.Models;

namespace MinGo.Infrastructure.Data;

/// <summary>
/// CertManager 远程接入配置存储（单行，Id = default）。
/// ApiKey 用 DataProtection 加密落库（与证书密码同机制），读取时脱敏。
/// </summary>
public class CertManagerSettingsService : ICertManagerSettingsService
{
    private readonly AppDbContext _dbContext;
    private readonly IDataProtector _dataProtector;
    private readonly ILogger<CertManagerSettingsService> _logger;

    public CertManagerSettingsService(
        AppDbContext dbContext,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<CertManagerSettingsService> logger)
    {
        _dbContext = dbContext;
        _dataProtector = dataProtectionProvider.CreateProtector("MinGo.CertManagerApiKey");
        _logger = logger;
    }

    public async Task<CertManagerSettingsDto> GetAsync()
    {
        var entity = await FindAsync();
        return MapToDto(entity, maskApiKey: true);
    }

    public async Task<CertManagerSettingsDto> GetWithApiKeyAsync()
    {
        var entity = await FindAsync();
        return MapToDto(entity, maskApiKey: false);
    }

    public async Task<CertManagerSettingsDto> SaveAsync(CertManagerSettingsDto dto)
    {
        var entity = await FindAsync();
        var isNew = entity is null;
        entity ??= new CertManagerSettingsEntity();

        entity.BaseUrl = (dto.BaseUrl ?? string.Empty).Trim();
        entity.Enabled = dto.Enabled;
        entity.TimeoutSeconds = dto.TimeoutSeconds is >= 1 and <= 120 ? dto.TimeoutSeconds : 30;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        // 空白或脱敏值表示不修改
        if (!IsMaskedOrBlank(dto.ApiKey))
        {
            entity.ApiKey = _dataProtector.Protect(dto.ApiKey.Trim());
        }

        if (isNew)
        {
            await _dbContext.CertManagerSettings.AddAsync(entity);
        }

        await _dbContext.SaveChangesAsync();
        _logger.LogInformation("Saved CertManager settings (Enabled={Enabled})", entity.Enabled);

        return MapToDto(entity, maskApiKey: true);
    }

    private Task<CertManagerSettingsEntity?> FindAsync() =>
        _dbContext.CertManagerSettings.FirstOrDefaultAsync(e => e.Id == CertManagerSettingsEntity.DefaultId);

    private static bool IsMaskedOrBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Contains("***");

    private CertManagerSettingsDto MapToDto(CertManagerSettingsEntity? entity, bool maskApiKey)
    {
        if (entity is null)
        {
            return new CertManagerSettingsDto();
        }

        return new CertManagerSettingsDto
        {
            BaseUrl = entity.BaseUrl,
            ApiKey = maskApiKey ? MaskApiKey(entity.ApiKey) : DecryptApiKey(entity.ApiKey),
            Enabled = entity.Enabled,
            TimeoutSeconds = entity.TimeoutSeconds,
            Configured = !string.IsNullOrWhiteSpace(entity.BaseUrl) && !string.IsNullOrWhiteSpace(entity.ApiKey),
            UpdatedAt = entity.UpdatedAt,
        };
    }

    private string DecryptApiKey(string stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return string.Empty;
        }

        try
        {
            return _dataProtector.Unprotect(stored);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decrypt CertManager ApiKey, treating as plain text");
            return stored;
        }
    }

    private static string MaskApiKey(string stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return string.Empty;
        var visible = Math.Min(4, stored.Length);
        return stored[..visible] + "***";
    }
}
