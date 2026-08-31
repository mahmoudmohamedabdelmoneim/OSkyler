using Skyler.Core;

namespace Skyler.Infrastructure;

public sealed class ConfiguredOutlookEvidenceSource(
    MicrosoftGraphOutlookEvidenceSource liveSource) : IWorkEvidenceSource
{
    public Task<IReadOnlyList<WorkEvidence>> GetEvidenceAsync(
        CancellationToken cancellationToken = default) =>
        liveSource.GetEvidenceAsync(cancellationToken);
}
