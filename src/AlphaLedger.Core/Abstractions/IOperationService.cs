using AlphaLedger.Core.Entities;
using AlphaLedger.Core.Enums;

namespace AlphaLedger.Core.Abstractions;

public interface IOperationService
{
    Task<IReadOnlyList<OfficeOperation>> GetOperationsAsync(Guid? companyId = null, CancellationToken cancellationToken = default);
    Task<OfficeOperation> AddOperationAsync(OfficeOperation operation, CancellationToken cancellationToken = default);
    Task<OfficeOperation> UpdateOperationAsync(Guid id, OfficeOperation operation, CancellationToken cancellationToken = default);
    Task DeleteOperationAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OfficeOperation>> GetOverdueOperationsAsync(CancellationToken cancellationToken = default);
    Task<OfficeOperation?> GetOperationAsync(Guid id, CancellationToken cancellationToken = default);
}