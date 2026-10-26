using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MinGo.Core.Entities;
using MinGo.Core.Interfaces;
using MinGo.Infrastructure.Data;

namespace MinGo.Infrastructure.Data;

/// <summary>
/// Portainer 实例管理服务实现。
/// </summary>
public class PortainerInstanceService : IPortainerInstanceService
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<PortainerInstanceService> _logger;

    public PortainerInstanceService(AppDbContext dbContext, ILogger<PortainerInstanceService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<IEnumerable<PortainerInstanceDto>> GetAllAsync()
    {
        var entities = await _dbContext.PortainerInstances
            .OrderBy(e => e.Id)
            .ToListAsync();
        return entities.Select(MapToDto);
    }

    public async Task<PortainerInstanceDto?> GetByIdAsync(string id)
    {
        var entity = await _dbContext.PortainerInstances.FirstOrDefaultAsync(e => e.Id == id);
        return entity is null ? null : MapToDto(entity);
    }

    public async Task<PortainerInstanceDto?> GetWithApiKeyAsync(string id)
    {
        var entity = await _dbContext.PortainerInstances.FirstOrDefaultAsync(e => e.Id == id);
        return entity is null ? null : MapToDtoWithFullApiKey(entity);
    }

    public async Task<PortainerInstanceDto> CreateAsync(PortainerInstanceDto dto)
    {
        var entity = MapToEntity(dto);
        entity.CreatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.PortainerInstances.AddAsync(entity);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Created Portainer instance: {InstanceId}", entity.Id);
        return MapToDto(entity);
    }

    public async Task<PortainerInstanceDto?> UpdateAsync(string id, PortainerInstanceDto dto)
    {
        var existing = await _dbContext.PortainerInstances.FirstOrDefaultAsync(e => e.Id == id);
        if (existing is null) return null;

        existing.Name = dto.Name;
        existing.Enabled = dto.Enabled;
        existing.BaseUrl = dto.BaseUrl;
        existing.HostAddress = dto.HostAddress;
        existing.DataPlaneService = string.IsNullOrWhiteSpace(dto.DataPlaneService) ? "data-plane" : dto.DataPlaneService;
        existing.TimeoutSeconds = dto.TimeoutSeconds > 0 ? dto.TimeoutSeconds : 10;
        existing.CacheSeconds = dto.CacheSeconds >= 0 ? dto.CacheSeconds : 30;
        existing.SelfStackNamesJson = JsonSerializer.Serialize(dto.SelfStackNames ?? []);
        existing.SelfImagePatternsJson = JsonSerializer.Serialize(dto.SelfImagePatterns ?? ["reverseproxy-cp"]);
        existing.ExtraExcludedStacksJson = JsonSerializer.Serialize(dto.ExtraExcludedStacks ?? []);
        existing.UpdatedAt = DateTimeOffset.UtcNow;

        // ApiKey: 如果前端传的是脱敏值（包含 ***），则不更新
        if (!string.IsNullOrWhiteSpace(dto.ApiKey) && !dto.ApiKey.Contains("***"))
        {
            existing.ApiKey = dto.ApiKey;
        }

        await _dbContext.SaveChangesAsync();
        _logger.LogInformation("Updated Portainer instance: {InstanceId}", id);

        return MapToDto(existing);
    }

    public async Task DeleteAsync(string id)
    {
        var entity = await _dbContext.PortainerInstances.FirstOrDefaultAsync(e => e.Id == id);
        if (entity is not null)
        {
            _dbContext.PortainerInstances.Remove(entity);
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Deleted Portainer instance: {InstanceId}", id);
        }
    }

    private static PortainerInstanceDto MapToDto(PortainerInstanceEntity entity)
    {
        return new PortainerInstanceDto
        {
            Id = entity.Id,
            Name = entity.Name,
            Enabled = entity.Enabled,
            BaseUrl = entity.BaseUrl,
            ApiKey = MaskApiKey(entity.ApiKey),
            TimeoutSeconds = entity.TimeoutSeconds,
            CacheSeconds = entity.CacheSeconds,
            HostAddress = entity.HostAddress,
            DataPlaneService = entity.DataPlaneService,
            SelfStackNames = entity.GetSelfStackNames(),
            SelfImagePatterns = entity.GetSelfImagePatterns(),
            ExtraExcludedStacks = entity.GetExtraExcludedStacks(),
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
        };
    }

    private static PortainerInstanceDto MapToDtoWithFullApiKey(PortainerInstanceEntity entity)
    {
        var dto = MapToDto(entity);
        dto.ApiKey = entity.ApiKey;
        return dto;
    }

    private static PortainerInstanceEntity MapToEntity(PortainerInstanceDto dto)
    {
        return new PortainerInstanceEntity
        {
            Id = dto.Id,
            Name = dto.Name,
            Enabled = dto.Enabled,
            BaseUrl = dto.BaseUrl,
            ApiKey = dto.ApiKey,
            TimeoutSeconds = dto.TimeoutSeconds > 0 ? dto.TimeoutSeconds : 10,
            CacheSeconds = dto.CacheSeconds >= 0 ? dto.CacheSeconds : 30,
            HostAddress = dto.HostAddress,
            DataPlaneService = string.IsNullOrWhiteSpace(dto.DataPlaneService) ? "data-plane" : dto.DataPlaneService,
            SelfStackNamesJson = JsonSerializer.Serialize(dto.SelfStackNames ?? []),
            SelfImagePatternsJson = JsonSerializer.Serialize(dto.SelfImagePatterns ?? ["reverseproxy-cp"]),
            ExtraExcludedStacksJson = JsonSerializer.Serialize(dto.ExtraExcludedStacks ?? []),
        };
    }

    private static string MaskApiKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return string.Empty;
        if (apiKey.Length <= 4) return apiKey + "***";
        return apiKey[..4] + "***";
    }
}
