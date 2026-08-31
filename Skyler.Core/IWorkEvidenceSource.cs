namespace Skyler.Core;

public interface IWorkEvidenceSource
{
    Task<IReadOnlyList<WorkEvidence>> GetEvidenceAsync(
        CancellationToken cancellationToken = default);
}
