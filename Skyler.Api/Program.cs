using Skyler.Core;
using Skyler.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options => options.TimestampFormat = "HH:mm:ss ");

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddSkylerDatabase(
    builder.Configuration.GetConnectionString("SkylerDatabase")
        ?? throw new InvalidOperationException("Connection string 'SkylerDatabase' was not found."),
    builder.Environment.ContentRootPath);

var localLlmOptions = new LocalLlmOptions(
    builder.Configuration["LocalLlm:BaseUrl"] ?? "http://localhost:11434/",
    builder.Configuration["LocalLlm:Model"] ?? "mistral",
    builder.Configuration.GetValue("LocalLlm:HealthTimeoutSeconds", 2),
    builder.Configuration.GetValue("LocalLlm:InferenceTimeoutSeconds", 90));
var outlookOptions = new OutlookOptions(
    builder.Configuration["Outlook:Mode"] ?? "Live",
    builder.Configuration["Outlook:ClientId"] ?? string.Empty,
    builder.Configuration["Outlook:Mailbox"] ?? string.Empty,
    builder.Configuration["Outlook:Authority"] ?? "https://login.microsoftonline.com/consumers",
    builder.Configuration.GetValue("Outlook:SyncDays", 30),
    builder.Configuration.GetValue("Outlook:MaxItems", 50),
    builder.Configuration.GetSection("Outlook:MentorshipIndicators").Get<string[]>()
        ?? ["mentor", "coaching", "career", "development", "one-on-one", "1:1"],
    builder.Configuration.GetSection("Outlook:MentorshipMeetingLinkIndicators").Get<string[]>()
        ?? []);

builder.Services.AddSingleton(localLlmOptions);
builder.Services.AddSingleton(outlookOptions);
builder.Services.AddSingleton(_ => new HttpClient
{
    BaseAddress = new Uri(localLlmOptions.BaseUrl),
    Timeout = Timeout.InfiniteTimeSpan
});
builder.Services.AddSingleton<OllamaWorkEvidenceAnalyzer>();
builder.Services.AddSingleton<ScenarioEvidenceAnalyzer>();
builder.Services.AddSingleton<IWorkEvidenceAnalyzer, ResilientWorkEvidenceAnalyzer>();
builder.Services.AddSingleton<OutlookTokenProvider>();
builder.Services.AddSingleton<MicrosoftGraphOutlookEvidenceSource>();
builder.Services.AddSingleton<IWorkEvidenceSource, ConfiguredOutlookEvidenceSource>();
builder.Services.AddSingleton<OutlookAnalysisWorker>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<OutlookAnalysisWorker>());

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<SkylerDbContext>();
    await database.EnsureCreatedSafelyAsync();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthorization();

app.MapControllers();

app.Run();
