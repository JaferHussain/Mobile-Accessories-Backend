using MoizPos.Domain.Enums;

namespace MoizPos.Application.Abstractions;

/// <summary>A share token as stored. Only the hash is ever persisted.</summary>
public sealed record StoredDocumentToken
{
    public long Id { get; init; }

    public DocumentType DocumentType { get; init; }

    public long ReferenceId { get; init; }

    public DateTime ExpiresAtUtc { get; init; }

    public DateTime? RevokedAtUtc { get; init; }

    public bool IsUsable(DateTime nowUtc) => RevokedAtUtc is null && ExpiresAtUtc > nowUtc;
}

public interface IDocumentTokenRepository
{
    Task<long> CreateAsync(
        string tokenHash,
        DocumentType documentType,
        long referenceId,
        DateTime expiresAtUtc,
        long createdByUserId,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    Task<StoredDocumentToken?> FindAsync(string tokenHash, CancellationToken cancellationToken = default);

    Task RecordAccessAsync(long id, DateTime nowUtc, CancellationToken cancellationToken = default);

    Task RevokeAsync(long id, DateTime nowUtc, CancellationToken cancellationToken = default);
}
