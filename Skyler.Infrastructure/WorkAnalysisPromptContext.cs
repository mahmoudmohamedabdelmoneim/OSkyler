using System.Reflection;

namespace Skyler.Infrastructure;

internal static class WorkAnalysisPromptContext
{
    private const string PromptResourceName =
        "Skyler.Infrastructure.Prompts.OutlookWorkAnalysisSystem.md";
    private const string TaxonomyResourceName =
        "Skyler.Infrastructure.ReferenceMaterials.WorkAnalysisTaxonomy.json";

    private static readonly Lazy<string> CachedContext = new(Load);

    public static string Value => CachedContext.Value;

    private static string Load()
    {
        var assembly = typeof(WorkAnalysisPromptContext).Assembly;
        return $"""
            {ReadResource(assembly, PromptResourceName)}

            <neutral-work-taxonomy>
            {ReadResource(assembly, TaxonomyResourceName)}
            </neutral-work-taxonomy>
            """;
    }

    private static string ReadResource(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded analysis resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
