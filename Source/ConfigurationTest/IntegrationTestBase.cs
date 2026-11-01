using dotnet_etcd;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Grpc.Core;
using Xunit.Abstractions;

namespace ROOT.Etcd.ConfigurationTest;

public abstract class IntegrationTestBase
{
    private readonly ITestOutputHelper _testOutputHelper;
    protected readonly string EtcdServer = Environment.GetEnvironmentVariable("ETCD_ENDPOINT") ?? "http://127.0.0.1:2379";

    protected IntegrationTestBase(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    public async Task<bool> StartEtcdContainer()
    {
        // On GitHub Actions etcd is provided by the workflow's service container.
        // Locally, an already running instance can be used by setting ETCD_ENDPOINT.
        if (IsRunningInGitHubActions() ||
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ETCD_ENDPOINT")))
        {
            _testOutputHelper.WriteLine("Using the externally provided etcd instance; skipping docker-compose.");
            return true;
        }

        var sourceDir = GetSourceDir();

        var processInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe", "/c docker-compose up -d")
            : new ProcessStartInfo("docker", "compose up -d");
        processInfo.WorkingDirectory = sourceDir;
        processInfo.RedirectStandardOutput = true;
        processInfo.RedirectStandardError = true;

        var process = Process.Start(processInfo);

        if (process == null) 
        {
            throw new InvalidOperationException("could not create docker compose command line - please check logs");
        }
        await process.WaitForExitAsync();
        var result = await process.StandardOutput.ReadToEndAsync();
        var err = await process.StandardError.ReadToEndAsync();
        Trace.WriteLine("ETCD is up");
        Trace.WriteLine(result);
        Trace.WriteLine(err);
        return process.ExitCode == 0;
    }

    private static bool IsRunningInGitHubActions() =>
        string.Equals(
            Environment.GetEnvironmentVariable("GITHUB_ACTIONS"),
            "true",
            StringComparison.OrdinalIgnoreCase);

    private static string GetSourceDir()
    {
        var thisFile = new System.IO.FileInfo(typeof(IntegrationTestBase).Assembly.Location).Directory;
        while (thisFile != null && thisFile.Name != "Source")
        {
            thisFile = thisFile.Parent;
        }
        return thisFile?.FullName ?? throw new InvalidOperationException("Could not determine source dir");
    }

    public EtcdClient CreateClient()
    {
        var client = new EtcdClient(EtcdServer, configureChannelOptions: (channelOptions) =>
        {
            channelOptions.Credentials = ChannelCredentials.Insecure;
        });

        return client;
    }
}
