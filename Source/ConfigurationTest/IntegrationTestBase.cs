using dotnet_etcd;
using dotnet_etcd.interfaces;
using Grpc.Core;
using ROOT.Etcd.Configuration;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace ROOT.Etcd.ConfigurationTest;

public abstract class IntegrationTestBase
{
    private const string DefaultEtcdServer = "http://127.0.0.1:2379";
    private const string DefaultTlsEtcdServer = "https://127.0.0.1:12379";
    private const string DefaultMtlsEtcdServer = "https://127.0.0.1:12389";

    private static readonly SemaphoreSlim DockerComposeGate = new(1, 1);
    private static bool _etcdContainerStarted;
    private static bool _tlsContainerStarted;
    private static bool _mtlsContainerStarted;

    private readonly ITestOutputHelper _testOutputHelper;

    protected static string EtcdServer { get; } =
        Environment.GetEnvironmentVariable("ETCD_ENDPOINT") ?? DefaultEtcdServer;

    protected static string TlsEtcdServer { get; } =
        Environment.GetEnvironmentVariable("ETCD_TLS_ENDPOINT") ?? DefaultTlsEtcdServer;

    protected static string CaCertificatePath { get; } =
        Environment.GetEnvironmentVariable("ETCD_TLS_CA_CERT")
        ?? TestCertificates.CaCertificatePath;

    protected static string MtlsEtcdServer { get; } =
        Environment.GetEnvironmentVariable("ETCD_MTLS_ENDPOINT") ?? DefaultMtlsEtcdServer;

    protected static string ClientCertificatePath { get; } =
        Environment.GetEnvironmentVariable("ETCD_MTLS_CLIENT_CERT")
        ?? TestCertificates.ClientCertificatePath;

    protected static string ClientCertificateKeyPath { get; } =
        Environment.GetEnvironmentVariable("ETCD_MTLS_CLIENT_KEY")
        ?? TestCertificates.ClientCertificateKeyPath;

    protected static string UntrustedClientCertificatePath => TestCertificates.UntrustedClientCertificatePath;

    protected static string UntrustedClientKeyPath => TestCertificates.UntrustedClientKeyPath;

    protected static string NoClientAuthEkuCertificatePath => TestCertificates.NoClientAuthEkuCertificatePath;

    protected static string NoClientAuthEkuKeyPath => TestCertificates.NoClientAuthEkuKeyPath;

    protected static string ExpiredClientCertificatePath => TestCertificates.ExpiredClientCertificatePath;

    protected static string ExpiredClientKeyPath => TestCertificates.ExpiredClientKeyPath;

    protected static bool IsExternalMtlsEndpoint =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ETCD_MTLS_ENDPOINT"));

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

        await DockerComposeGate.WaitAsync(cancellationToken);
        try
        {
            if (_etcdContainerStarted)
            {
                return true;
            }

            _etcdContainerStarted = await RunDockerComposeAsync("up -d", cancellationToken);
            return _etcdContainerStarted;
        }
        finally
        {
            DockerComposeGate.Release();
        }
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
        // when fresh certificates were generated during this test run. Serialize the
        // operation and only do it once per process: parallel test classes must not
        // run concurrent compose recreate operations against the same stack.
        await DockerComposeGate.WaitAsync(cancellationToken);
        try
        {
            if (!_tlsContainerStarted)
            {
                var upArguments = TestCertificates.WasRegenerated
                    ? "-f docker-compose.tls.yml up -d --force-recreate"
                    : "-f docker-compose.tls.yml up -d";

                if (!await RunDockerComposeAsync(upArguments, cancellationToken))
                {
                    return false;
                }

                _tlsContainerStarted = true;
            }
        }
        finally
        {
            DockerComposeGate.Release();
        }

        return await WaitForTlsEtcdAsync(cancellationToken);
    }

    /// <summary>
    /// Starts the mutual-TLS etcd container unless an external mTLS endpoint was provided
    /// through ETCD_MTLS_ENDPOINT, then waits until it responds with a client certificate.
    /// </summary>
    public async Task<bool> StartMtlsEtcdContainer(CancellationToken cancellationToken)
    {
        TestCertificates.EnsureGenerated();

        if (IsExternalMtlsEndpoint)
        {
            _testOutputHelper.WriteLine("Using the externally provided mTLS etcd instance; skipping docker-compose.");
            return await WaitForMtlsEtcdAsync(cancellationToken);
        }

        // See StartTlsEtcdContainer: serialize and only recreate the stack once per
        // process so parallel test classes cannot race on Compose's temporary names.
        await DockerComposeGate.WaitAsync(cancellationToken);
        try
        {
            if (!_mtlsContainerStarted)
            {
                var upArguments = TestCertificates.WasRegenerated
                    ? "-f docker-compose.mtls.yml up -d --force-recreate"
                    : "-f docker-compose.mtls.yml up -d";

                if (!await RunDockerComposeAsync(upArguments, cancellationToken))
                {
                    return false;
                }

                _mtlsContainerStarted = true;
            }
        }
        finally
        {
            DockerComposeGate.Release();
        }

        return await WaitForMtlsEtcdAsync(cancellationToken);
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

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var output = await Task.WhenAll(standardOutputTask, standardErrorTask);
        await process.WaitForExitAsync(cancellationToken);

        var result = output[0];
        var err = output[1];

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

    private async Task<bool> WaitForMtlsEtcdAsync(CancellationToken cancellationToken)
    {
        using var clientCertificate = EtcdTlsCertificateLoader.LoadClientCertificate(new SslOptions
        {
            ClientCertificatePath = ClientCertificatePath,
            ClientCertificateKeyPath = ClientCertificateKeyPath
        });

        using var handler = new HttpClientHandler
        {
            // This is only a readiness probe. The client certificate is required for the
            // TLS handshake because the server runs with --client-cert-auth=true.
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        handler.ClientCertificates.Add(clientCertificate);

        using var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };

        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var response = await httpClient.GetAsync($"{MtlsEtcdServer}/health", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    _testOutputHelper.WriteLine("mTLS-enabled etcd is healthy.");
                    return true;
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                _testOutputHelper.WriteLine($"Waiting for mTLS-enabled etcd: {exception.Message}");
            }

            await Task.Delay(500, cancellationToken);
        }

        _testOutputHelper.WriteLine("Timed out waiting for the mTLS-enabled etcd instance.");
        return false;
    }

    private static bool IsRunningInGitHubActions() =>
        string.Equals(
            Environment.GetEnvironmentVariable("GITHUB_ACTIONS"),
            "true",
            StringComparison.OrdinalIgnoreCase);

    public static EtcdClient CreateClient()
    {
        var client = new EtcdClient(EtcdServer, configureChannelOptions: (channelOptions) =>
        {
            channelOptions.Credentials = ChannelCredentials.Insecure;
        });

        return client;
    }

    /// <summary>
    /// Creates a client through the library factory with SSL enabled and
    /// the test CA used to validate the server certificate.
    /// </summary>
    public static IEtcdClient CreateTlsClient()
    {
        return new EtcdClientFactory().CreateClient(new EtcdOptions
        {
            EndPoints = TlsEtcdServer,
            SslOptions = new SslOptions
            {
                UseSsl = true,
                ServerCertificateAuthorityPath = CaCertificatePath
            }
        });
    }

    /// <summary>
    /// Creates an mTLS client with the CA and the file-based test client certificate.
    /// </summary>
    public static IEtcdClient CreateMtlsClient()
    {
        return new EtcdClientFactory().CreateClient(new EtcdOptions
        {
            EndPoints = MtlsEtcdServer,
            SslOptions = new SslOptions
            {
                UseSsl = true,
                ServerCertificateAuthorityPath = CaCertificatePath,
                ClientCertificatePath = ClientCertificatePath,
                ClientCertificateKeyPath = ClientCertificateKeyPath
            }
        });
    }

    /// <summary>
    /// Creates an mTLS client with the CA and a caller-owned in-memory certificate.
    /// </summary>
    public static IEtcdClient CreateMtlsClient(X509Certificate2 certificate)
    {
        return new EtcdClientFactory().CreateClient(new EtcdOptions
        {
            EndPoints = MtlsEtcdServer,
            SslOptions = new SslOptions
            {
                UseSsl = true,
                ServerCertificateAuthorityPath = CaCertificatePath,
                ClientCertificate = certificate
            }
        });
    }
}
