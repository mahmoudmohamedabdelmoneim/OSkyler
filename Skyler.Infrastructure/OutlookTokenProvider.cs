using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;

namespace Skyler.Infrastructure;

public sealed class OutlookTokenProvider
{
    private static readonly string[] Scopes = ["User.Read", "Mail.Read", "Calendars.Read"];

    private readonly OutlookOptions _options;
    private readonly ILogger<OutlookTokenProvider> _logger;
    private readonly SemaphoreSlim _authenticationLock = new(1, 1);
    private readonly Lazy<Task<IPublicClientApplication>> _application;
    private volatile bool _authorizationRequired;

    public bool AuthorizationRequired => _authorizationRequired;

    public OutlookTokenProvider(
        OutlookOptions options,
        ILogger<OutlookTokenProvider> logger)
    {
        _options = options;
        _logger = logger;
        _application = new Lazy<Task<IPublicClientApplication>>(CreateApplicationAsync);
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        await _authenticationLock.WaitAsync(cancellationToken);
        try
        {
            var application = await _application.Value;
            var accounts = await application.GetAccountsAsync();
            var account = accounts.FirstOrDefault(item =>
                item.Username.Equals(_options.Mailbox, StringComparison.OrdinalIgnoreCase));

            if (account is not null)
            {
                try
                {
                    var silentResult = await application
                        .AcquireTokenSilent(Scopes, account)
                        .ExecuteAsync(cancellationToken);
                    return silentResult.AccessToken;
                }
                catch (MsalUiRequiredException)
                {
                    _logger.LogInformation("The cached Outlook authorization needs interactive renewal");
                }
            }

            var result = await application
                .AcquireTokenWithDeviceCode(Scopes, message =>
                {
                    _authorizationRequired = true;
                    _logger.LogWarning(
                        "Outlook authorization required. Open {VerificationUrl} and enter code {UserCode}",
                        message.VerificationUrl,
                        message.UserCode);
                    Console.WriteLine();
                    Console.WriteLine(message.Message);
                    Console.WriteLine();
                    return Task.CompletedTask;
                })
                .ExecuteAsync(cancellationToken);

            if (!result.Account.Username.Equals(_options.Mailbox, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Skyler expected Outlook account '{_options.Mailbox}', but Microsoft authorized '{result.Account.Username}'.");
            }

            _authorizationRequired = false;
            _logger.LogInformation("Outlook authorization completed for {Mailbox}", result.Account.Username);
            return result.AccessToken;
        }
        finally
        {
            _authenticationLock.Release();
        }
    }

    private async Task<IPublicClientApplication> CreateApplicationAsync()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId))
        {
            throw new InvalidOperationException("Outlook ClientId is not configured.");
        }

        var application = PublicClientApplicationBuilder
            .Create(_options.ClientId)
            .WithAuthority(_options.Authority)
            .WithLegacyCacheCompatibility(false)
            .Build();

        var cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Skyler",
            "Authentication");
        Directory.CreateDirectory(cacheDirectory);

        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(
                cacheDirectory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var storageBuilder = new StorageCreationPropertiesBuilder(
                "outlook-msal-cache.bin",
                cacheDirectory);

        // Headless Linux services do not have a desktop keyring/D-Bus session. Keep the
        // token cache in the service account's private directory so silent renewal works
        // after restarts without making the API depend on an interactive desktop session.
        if (OperatingSystem.IsLinux())
        {
            storageBuilder.WithLinuxUnprotectedFile();
        }

        var storageProperties = storageBuilder.Build();
        var cacheHelper = await MsalCacheHelper.CreateAsync(storageProperties);
        cacheHelper.VerifyPersistence();
        cacheHelper.RegisterCache(application.UserTokenCache);

        return application;
    }
}
