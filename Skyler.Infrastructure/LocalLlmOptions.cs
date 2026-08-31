namespace Skyler.Infrastructure;

public sealed record LocalLlmOptions(
    string BaseUrl,
    string Model,
    int HealthTimeoutSeconds,
    int InferenceTimeoutSeconds);
