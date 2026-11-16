using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MinGo.Core.Entities;
using MinGo.Core.Interfaces;
using MinGo.Infrastructure.Data;

namespace MinGo.Infrastructure.Data;

/// <summary>
/// Cloudflare 接入配置存储（单行，Id = default）。
/// </summary>
public class CloudflareSettingsService : ICloudflareSettingsService
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<CloudflareSettingsService> _logger;

    public CloudflareSettingsService(AppDbContext dbContext, ILogger<CloudflareSettingsService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<CloudflareSettingsDto> GetAsync()
    {
        var entity = await FindAsync();
        return MapToDto(entity, maskToken: true);
    }

    public async Task<CloudflareSettingsDto> GetWithTokenAsync()
    {
        var entity = await FindAsync();
        return MapToDto(entity, maskToken: false);
    }

    public async Task<CloudflareSettingsDto> SaveAsync(CloudflareSettingsDto dto)
    {
        var entity = await FindAsync();
        var isNew = entity is null;
        entity ??= new CloudflareSettingsEntity();

        // 空白或脱敏值表示不修改；新建时保留空 Token（由配置节兜底）
        if (!IsMaskedOrBlank(dto.ApiToken))
        {
            entity.ApiToken = dto.ApiToken.Trim();
        }

        entity.AccountId = (dto.AccountId ?? string.Empty).Trim();
        entity.TimeoutSeconds = dto.TimeoutSeconds is >= 1 and <= 120 ? dto.TimeoutSeconds : 10;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        if (isNew)
        {
            await _dbContext.CloudflareSettings.AddAsync(entity);
        }

        await _dbContext.SaveChangesAsync();
        _logger.LogInformation("Saved Cloudflare settings");

        return MapToDto(entity, maskToken: true);
    }

    private Task<CloudflareSettingsEntity?> FindAsync() =>
        _dbContext.CloudflareSettings.FirstOrDefaultAsync(e => e.Id == CloudflareSettingsEntity.DefaultId);

    private static bool IsMaskedOrBlank(string? token) =>
        string.IsNullOrWhiteSpace(token) || token.Contains("***");

    private static CloudflareSettingsDto MapToDto(CloudflareSettingsEntity? entity, bool maskToken)
    {
        if (entity is null)
        {
            return new CloudflareSettingsDto();
        }

        return new CloudflareSettingsDto
        {
            ApiToken = maskToken ? MaskToken(entity.ApiToken) : entity.ApiToken,
            AccountId = entity.AccountId,
            TimeoutSeconds = entity.TimeoutSeconds,
            Configured = !string.IsNullOrWhiteSpace(entity.ApiToken),
            UpdatedAt = entity.UpdatedAt,
        };
    }

    private static string MaskToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return string.Empty;
        var visible = Math.Min(4, token.Length);
        return token[..visible] + "***";
    }
}