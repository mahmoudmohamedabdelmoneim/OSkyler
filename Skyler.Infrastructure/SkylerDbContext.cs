using Microsoft.EntityFrameworkCore;
using Skyler.Core;

namespace Skyler.Infrastructure;

public sealed class SkylerDbContext(DbContextOptions<SkylerDbContext> options)
    : DbContext(options)
{
    public DbSet<WorkActivity> WorkActivities => Set<WorkActivity>();

    public DbSet<WorkEvidence> WorkEvidence => Set<WorkEvidence>();

    public DbSet<WorkEvidenceAnalysis> WorkEvidenceAnalyses => Set<WorkEvidenceAnalysis>();

    public DbSet<DimensionAssessment> DimensionAssessments => Set<DimensionAssessment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var activity = modelBuilder.Entity<WorkActivity>();

        activity.ToTable("WorkActivities");
        activity.HasKey(item => item.Id);
        activity.Property(item => item.Description).HasMaxLength(300).IsRequired();
        activity.Property(item => item.Classification).HasMaxLength(100).IsRequired();
        activity.Property(item => item.HumanOpportunity).HasMaxLength(200).IsRequired();
        activity.Ignore(item => item.TimeFreedMinutes);
        activity.HasIndex(item => item.OccurredAtUtc);

        var evidence = modelBuilder.Entity<WorkEvidence>();
        evidence.ToTable("WorkEvidence");
        evidence.HasKey(item => item.Id);
        evidence.Property(item => item.ExternalId).HasMaxLength(200).IsRequired();
        evidence.Property(item => item.EmployeeId).HasMaxLength(200).IsRequired();
        evidence.Property(item => item.Subject).HasMaxLength(500).IsRequired();
        evidence.Property(item => item.Content).HasMaxLength(8000).IsRequired();
        evidence.Property(item => item.Participants).HasMaxLength(2000).IsRequired();
        evidence.HasIndex(item => new { item.Source, item.ExternalId }).IsUnique();
        evidence.HasIndex(item => item.OccurredAtUtc);
        evidence.HasOne(item => item.Analysis)
            .WithOne(item => item.Evidence)
            .HasForeignKey<WorkEvidenceAnalysis>(item => item.WorkEvidenceId)
            .OnDelete(DeleteBehavior.Cascade);

        var analysis = modelBuilder.Entity<WorkEvidenceAnalysis>();
        analysis.ToTable("WorkEvidenceAnalyses");
        analysis.HasKey(item => item.Id);
        analysis.Property(item => item.Analyzer).HasMaxLength(200).IsRequired();
        analysis.Property(item => item.Summary).HasMaxLength(2000).IsRequired();
        analysis.Property(item => item.InferredRole).HasMaxLength(200);
        analysis.Property(item => item.RoleRationale).HasMaxLength(1000).IsRequired();
        analysis.Property(item => item.AutomationOpportunity).HasMaxLength(2000);
        analysis.Property(item => item.Warning).HasMaxLength(1000);
        analysis.Ignore(item => item.TimeFreedMinutes);
        analysis.HasMany(item => item.Dimensions)
            .WithOne(item => item.Analysis)
            .HasForeignKey(item => item.WorkEvidenceAnalysisId)
            .OnDelete(DeleteBehavior.Cascade);

        var dimension = modelBuilder.Entity<DimensionAssessment>();
        dimension.ToTable("DimensionAssessments");
        dimension.HasKey(item => item.Id);
        dimension.Property(item => item.Rationale).HasMaxLength(1000).IsRequired();
        dimension.HasIndex(item => new { item.WorkEvidenceAnalysisId, item.Dimension }).IsUnique();

    }
}
