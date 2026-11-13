using dotnet_etcd;
using dotnet_etcd.interfaces;
using Grpc.Core;
using ROOT.Etcd.Configuration;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ROOT.Etcd.ConfigurationTest;

public abstract class IntegrationTestBase
{
    private const string DefaultEtcdServer = "http://127.0.0.1:2379";
    private const string DefaultTlsEtcdServer = "https://127.0.0.1:12379";

    private readonly ITestOutputHelper _testOutputHelper;

    protected string EtcdServer { get; } =
        Environment.GetEnvironmentVariable("ETCD_ENDPOINT") ?? DefaultEtcdServer;

    protected string TlsEtcdServer { get; } =
        Environment.GetEnvironmentVariable("ETCD_TLS_ENDPOINT") ?? DefaultTlsEtcdServer;

    protected string CaCertificatePath { get; } =
        Environment.GetEnvironmentVariable("ETCD_TLS_CA_CERT")
        ?? TestCertificates.CaCertificatePath;

    protected IntegrationTestBase(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    public async Task<bool> StartEtcdContainer(CancellationToken cancellationToken)
    {
        // On GitHub Actions etcd is provided by the workflow's service container.
        // Locally, an already running instance can be used by setting ETCD_ENDPOINT.
        if (IsRunningInGitHubActions() ||
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ETCD_ENDPOINT")))
        {
            _testOutputHelper.WriteLine("Using the externally provided etcd instance; skipping docker-compose.");
            return true;
        }

        return await RunDockerComposeAsync("up -d", cancellationToken);
    }

    /// <summary>
    /// Starts the TLS-enabled etcd container unless an external TLS endpoint was provided
    /// through ETCD_TLS_ENDPOINT, then waits until it responds.
    /// </summary>
    public async Task<bool> StartTlsEtcdContainer(CancellationToken cancellationToken)
    {
        TestCertificates.EnsureGenerated();

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ETCD_TLS_ENDPOINT")))
        {
            _testOutputHelper.WriteLine("Using the externally provided TLS etcd instance; skipping docker-compose.");
            return await WaitForTlsEtcdAsync(cancellationToken);
        }

        // etcd reads the certificate files only at startup, so recreate the container
        // when fresh certificates were generated during this test run.
        var upArguments = TestCertificates.WasRegenerated
            ? "-f docker-compose.tls.yml up -d --force-recreate"
            : "-f docker-compose.tls.yml up -d";

        if (!await RunDockerComposeAsync(upArguments, cancellationToken))
        {
            return false;
        }

        return await WaitForTlsEtcdAsync(cancellationToken);
    }

    private async Task<bool> RunDockerComposeAsync(string arguments, CancellationToken cancellationToken)
    {
        var sourceDir = TestCertificates.SourceDirectory;

        var processInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe", $"/c docker-compose {arguments}")
            : new ProcessStartInfo("docker", $"compose {arguments}");
        processInfo.WorkingDirectory = sourceDir;
        processInfo.RedirectStandardOutput = true;
        processInfo.RedirectStandardError = true;

        var process = Process.Start(processInfo) ?? throw new InvalidOperationException("could not create docker compose command line - please check logs");

        var result = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var err = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        Trace.WriteLine("ETCD is up");
        Trace.WriteLine(result);
        Trace.WriteLine(err);
        _testOutputHelper.WriteLine(result);
        _testOutputHelper.WriteLine(err);

        return process.ExitCode == 0;
    }

    private async Task<bool> WaitForTlsEtcdAsync(CancellationToken cancellationToken)
    {
        using var handler = new HttpClientHandler
        {
            // This is only a readiness probe. The actual client connection below
            // validates the server certificate against certs/ca.crt.
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        using var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };

        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var response = await httpClient.GetAsync($"{TlsEtcdServer}/health", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    _testOutputHelper.WriteLine("TLS-enabled etcd is healthy.");
                    return true;
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                _testOutputHelper.WriteLine($"Waiting for TLS-enabled etcd: {exception.Message}");
            }

            await Task.Delay(500, cancellationToken);
        }

        _testOutputHelper.WriteLine("Timed out waiting for the TLS-enabled etcd instance.");
        return false;
    }

    private static bool IsRunningInGitHubActions() =>
        string.Equals(
            Environment.GetEnvironmentVariable("GITHUB_ACTIONS"),
            "true",
            StringComparison.OrdinalIgnoreCase);

    public EtcdClient CreateClient()
    {
        var client = new EtcdClient(EtcdServer, configureChannelOptions: (channelOptions) =>
        {
            channelOptions.Credentials = ChannelCredentials.Insecure;
        });

        return client;
    }

    /// <summary>
    /// Creates a client through the library factory with insecure channels disabled and
    /// the test CA used to validate the server certificate.
    /// </summary>
    public IEtcdClient CreateTlsClient()
    {
        return new EtcdClientFactory().CreateClient(new EtcdOptions
        {
            EndPoints = TlsEtcdServer,
            UseInsecureChannels = false,
            ServerCertificateAuthorityPath = CaCertificatePath
        });
    }
}
