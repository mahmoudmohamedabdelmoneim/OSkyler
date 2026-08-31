using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Skyler.Core;

namespace Skyler.Infrastructure;

public sealed class OutlookAnalysisWorker(
    IServiceScopeFactory scopeFactory,
    IWorkEvidenceSource evidenceSource,
    IWorkEvidenceAnalyzer analyzer,
    ILogger<OutlookAnalysisWorker> logger) : BackgroundService
{
    private readonly SemaphoreSlim _synchronizationGate = new(1, 1);
    private readonly SemaphoreSlim _analysisGate = new(1, 1);
    private readonly object _queuedAnalysisLock = new();
    private CancellationToken _stoppingToken;
    private Task? _queuedAnalysis;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshNowAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Outlook authorization and transient Graph failures must never stop the
                // API. The dashboard remains available and the next interval retries.
                logger.LogError(exception, "Scheduled Outlook refresh failed; the API remains available");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
            {
                break;
            }
        }
    }

    public async Task RefreshNowAsync(CancellationToken cancellationToken = default)
    {
        await SynchronizeNowAsync(cancellationToken);
        await AnalyzeNowAsync(cancellationToken);
    }

    public async Task SynchronizeNowAsync(CancellationToken cancellationToken = default)
    {
        await _synchronizationGate.WaitAsync(cancellationToken);
        try
        {
            await SynchronizeEvidenceAsync(cancellationToken);
        }
        finally
        {
            _synchronizationGate.Release();
        }
    }

    public void QueueAnalysis()
    {
        lock (_queuedAnalysisLock)
        {
            if (_queuedAnalysis is { IsCompleted: false })
            {
                return;
            }

            _queuedAnalysis = AnalyzeInBackgroundAsync();
        }
    }

    private async Task AnalyzeInBackgroundAsync()
    {
        try
        {
            await AnalyzeNowAsync(_stoppingToken);
        }
        catch (OperationCanceledException) when (_stoppingToken.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Queued Outlook analysis failed");
        }
    }

    private async Task AnalyzeNowAsync(CancellationToken cancellationToken)
    {
        await _analysisGate.WaitAsync(cancellationToken);
        try
        {
            await AnalyzePendingEvidenceAsync(cancellationToken);
        }
        finally
        {
            _analysisGate.Release();
        }
    }

    private async Task SynchronizeEvidenceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<SkylerDbContext>();

        await database.EnsureCreatedSafelyAsync(cancellationToken);

        var existingEvidence = await database.WorkEvidence
            .Include(evidence => evidence.Analysis)
            .ToListAsync(cancellationToken);
        var existingByExternalId = existingEvidence.ToDictionary(
            evidence => $"{evidence.Source}:{evidence.ExternalId}",
            StringComparer.OrdinalIgnoreCase);
        var sourceEvidence = await evidenceSource.GetEvidenceAsync(cancellationToken);
        var newEvidence = new List<WorkEvidence>();
        var updatedCount = 0;

        foreach (var sourceItem in sourceEvidence)
        {
            var key = $"{sourceItem.Source}:{sourceItem.ExternalId}";
            if (!existingByExternalId.TryGetValue(key, out var existing))
            {
                newEvidence.Add(sourceItem);
                continue;
            }

            if (sourceItem.IsSynthetic || !UpdateFromSource(existing, sourceItem))
            {
                continue;
            }

            updatedCount++;
        }

        if (newEvidence.Count == 0 && updatedCount == 0)
        {
            logger.LogInformation("No new or changed Outlook evidence was found");
            return;
        }

        database.WorkEvidence.AddRange(newEvidence);
        await database.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Imported {NewCount} and refreshed {UpdatedCount} Outlook evidence records",
            newEvidence.Count,
            updatedCount);
    }

    private static bool UpdateFromSource(WorkEvidence target, WorkEvidence source)
    {
        var changed =
            target.Subject != source.Subject ||
            target.Content != source.Content ||
            target.Participants != source.Participants ||
            target.OccurredAtUtc != source.OccurredAtUtc ||
            target.DurationMinutes != source.DurationMinutes ||
            target.BaselineMinutes != source.BaselineMinutes ||
            target.ActualMinutes != source.ActualMinutes ||
            target.IsMentorship != source.IsMentorship ||
            target.IsAbsence != source.IsAbsence;

        if (!changed)
        {
            return false;
        }

        target.Subject = source.Subject;
        target.Content = source.Content;
        target.Participants = source.Participants;
        target.OccurredAtUtc = source.OccurredAtUtc;
        target.DurationMinutes = source.DurationMinutes;
        target.BaselineMinutes = source.BaselineMinutes;
        target.ActualMinutes = source.ActualMinutes;
        target.IsMentorship = source.IsMentorship;
        target.IsAbsence = source.IsAbsence;

        return true;
    }

    private async Task AnalyzePendingEvidenceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<SkylerDbContext>();
        var pendingEvidence = await database.WorkEvidence
            .Include(evidence => evidence.Analysis)
            .Where(evidence => evidence.Analysis == null)
            .OrderBy(evidence => evidence.Id)
            .ToListAsync(cancellationToken);

        foreach (var evidence in pendingEvidence)
        {
            WorkEvidenceAnalysis analysis;
            try
            {
                analysis = await analyzer.AnalyzeAsync(evidence, cancellationToken);
            }
            catch (Exception exception) when (
                !cancellationToken.IsCancellationRequested &&
                exception is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException)
            {
                logger.LogWarning(
                    "Analysis remains pending for evidence {EvidenceId}: {Reason}",
                    evidence.Id,
                    exception.Message);
                continue;
            }

            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

            database.WorkEvidenceAnalyses.Add(analysis);
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation(
                "Analyzed {Kind} evidence '{Subject}' using {Analyzer}",
                evidence.Kind,
                evidence.Subject,
                analysis.Analyzer);
        }

        if (pendingEvidence.Count == 0)
        {
            logger.LogInformation("No Outlook evidence is waiting for analysis");
        }
    }
}
