using System.Net.Http.Json;
using Skyler.Contracts;

namespace Skyler.Portal.Services;

public sealed class DashboardApiClient(HttpClient httpClient)
{
    public async Task<DashboardSummaryDto> GetSummaryAsync(
        string period = "week",
        CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<DashboardSummaryDto>(
                   $"api/dashboard?period={Uri.EscapeDataString(period)}",
                   cancellationToken)
               ?? throw new InvalidOperationException("The dashboard API returned an empty response.");
    }

    public async Task SetAutomationApprovalAsync(
        Guid evidenceId,
        bool approved,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PutAsJsonAsync(
            $"api/dashboard/evidence/{evidenceId}/automation-approval",
            new AutomationApprovalRequestDto(approved),
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
