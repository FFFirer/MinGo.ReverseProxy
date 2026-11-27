using Microsoft.EntityFrameworkCore;
using MinGo.Core.Entities;
using MinGo.Core.Interfaces;
using MinGo.Core.Models;

namespace MinGo.Infrastructure.Data;

/// <summary>
/// 证书自动接入登记存储。
/// </summary>
public class CertificateEnrollmentStore : ICertificateEnrollmentStore
{
    private readonly AppDbContext _dbContext;

    public CertificateEnrollmentStore(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<CertificateEnrollmentDto>> GetAllAsync()
    {
        var entities = await _dbContext.CertificateEnrollments
            .OrderBy(e => e.Domain)
            .ToListAsync();
        return entities.Select(MapToDto).ToList();
    }

    public async Task<CertificateEnrollmentDto?> GetByIdAsync(string id)
    {
        var entity = await _dbContext.CertificateEnrollments.FirstOrDefaultAsync(e => e.Id == id);
        return entity is null ? null : MapToDto(entity);
    }

    public async Task<CertificateEnrollmentDto?> GetByDomainAsync(string domain)
    {
        var normalized = CertificateEnrollmentPolicy.NormalizeDomain(domain);
        var entity = await _dbContext.CertificateEnrollments
            .FirstOrDefaultAsync(e => e.Domain.ToLower() == normalized);
        return entity is null ? null : MapToDto(entity);
    }

    public async Task<CertificateEnrollmentDto> CreateAsync(CertificateEnrollmentDto dto)
    {
        var entity = new CertificateEnrollmentEntity
        {
            Id = dto.Id,
            Domain = CertificateEnrollmentPolicy.NormalizeDomain(dto.Domain),
            IsWildcard = dto.IsWildcard,
            UseStaging = dto.UseStaging,
            ExternalCertificateId = dto.ExternalCertificateId,
            State = dto.State,
            ExpiresAt = dto.ExpiresAt,
            LastSyncAt = dto.LastSyncAt,
            LastThumbprint = dto.LastThumbprint,
            LastError = dto.LastError,
            Enabled = dto.Enabled,
            CreatedAt = dto.CreatedAt,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await _dbContext.CertificateEnrollments.AddAsync(entity);
        await _dbContext.SaveChangesAsync();
        return MapToDto(entity);
    }

    public async Task<CertificateEnrollmentDto?> UpdateAsync(string id, CertificateEnrollmentDto dto)
    {
        var entity = await _dbContext.CertificateEnrollments.FirstOrDefaultAsync(e => e.Id == id);
        if (entity is null)
        {
            return null;
        }

        entity.Domain = CertificateEnrollmentPolicy.NormalizeDomain(dto.Domain);
        entity.IsWildcard = dto.IsWildcard;
        entity.UseStaging = dto.UseStaging;
        entity.ExternalCertificateId = dto.ExternalCertificateId;
        entity.State = dto.State;
        entity.ExpiresAt = dto.ExpiresAt;
        entity.LastSyncAt = dto.LastSyncAt;
        entity.LastThumbprint = dto.LastThumbprint;
        entity.LastError = dto.LastError;
        entity.Enabled = dto.Enabled;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync();
        return MapToDto(entity);
    }

    public async Task DeleteAsync(string id)
    {
        var entity = await _dbContext.CertificateEnrollments.FirstOrDefaultAsync(e => e.Id == id);
        if (entity is null)
        {
            return;
        }

        _dbContext.CertificateEnrollments.Remove(entity);
        await _dbContext.SaveChangesAsync();
    }

    private static CertificateEnrollmentDto MapToDto(CertificateEnrollmentEntity entity) => new()
    {
        Id = entity.Id,
        Domain = entity.Domain,
        IsWildcard = entity.IsWildcard,
        UseStaging = entity.UseStaging,
        ExternalCertificateId = entity.ExternalCertificateId,
        State = entity.State,
        ExpiresAt = entity.ExpiresAt,
        LastSyncAt = entity.LastSyncAt,
        LastThumbprint = entity.LastThumbprint,
        LastError = entity.LastError,
        Enabled = entity.Enabled,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt,
    };
}
