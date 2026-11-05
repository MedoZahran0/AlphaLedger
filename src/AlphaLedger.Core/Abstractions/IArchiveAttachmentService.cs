namespace AlphaLedger.Core.Abstractions;

public interface IArchiveAttachmentService
{
    Task AttachAsync(Guid companyId, string companyCode, string entityType, Guid entityId, int categoryNumber, string sourcePath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ArchiveAttachment>> GetForEntityAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default);
    Task UpdatePathsAsync(string oldPath, string newPath, bool includeDescendants = false, CancellationToken cancellationToken = default);
}

public class ArchiveAttachment
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public int CategoryNumber { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTime AttachedAtUtc { get; set; }
}
