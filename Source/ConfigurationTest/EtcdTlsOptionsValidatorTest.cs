using Microsoft.Extensions.Options;
using ROOT.Etcd.Configuration;
using System;
using System.IO;

namespace ROOT.Etcd.ConfigurationTest;

public class EtcdTlsOptionsValidatorTest
{
    private const string HttpsEndpoint = "https://127.0.0.1:2379";
    private const string HttpEndpoint = "http://127.0.0.1:2379";

    [Fact]
    public void DefaultOptions_AreSelfConsistent()
    {
        Assert.Empty(EtcdTlsOptionsValidator.Validate(new EtcdOptions()));
    }

    [Fact]
    public void UseSslFalseWithHttpEndpointAndNoTlsSettings_Succeeds()
    {
        var options = new EtcdOptions
        {
            EndPoints = HttpEndpoint,
            SslOptions = new SslOptions { UseSsl = false }
        };

        Assert.Empty(EtcdTlsOptionsValidator.Validate(options));
    }

    [Fact]
    public void UseSslFalseWithCaCertificate_Fails()
    {
        var options = new EtcdOptions
        {
            EndPoints = HttpEndpoint,
            SslOptions = new SslOptions
            {
                UseSsl = false,
                ServerCertificateAuthorityPath = TestCertificates.CaCertificatePath
            }
        };

        Assert.Contains(
            EtcdTlsOptionsValidator.Validate(options),
            failure => failure.Contains("UseSsl is false", StringComparison.Ordinal));
    }

    [Fact]
    public void UseSslFalseWithInMemoryClientCertificate_Fails()
    {
        using var certificate = TestCertificateFactory.CreateSelfSignedClientCertificate();

        var options = new EtcdOptions
        {
            EndPoints = HttpEndpoint,
            SslOptions = new SslOptions
            {
                UseSsl = false,
                ClientCertificate = certificate
            }
        };

        Assert.Contains(
            EtcdTlsOptionsValidator.Validate(options),
            failure => failure.Contains("UseSsl is false", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("client.crt", null, null)]
    [InlineData(null, "client.key", null)]
    [InlineData(null, null, "password")]
    public void UseSslFalseWithFileBasedClientCertificateSettings_Fails(
        string? certificatePath,
        string? keyPath,
        string? password)
    {
        var options = new EtcdOptions
        {
            EndPoints = HttpEndpoint,
            SslOptions = new SslOptions
            {
                UseSsl = false,
                ClientCertificatePath = certificatePath,
                ClientCertificateKeyPath = keyPath,
                ClientCertificatePassword = password
            }
        };

        Assert.Contains(
            EtcdTlsOptionsValidator.Validate(options),
            failure => failure.Contains("UseSsl is false", StringComparison.Ordinal));
    }

    [Fact]
    public void UseSslFalseWithHttpsEndpoint_Fails()
    {
        var options = new EtcdOptions
        {
            EndPoints = HttpsEndpoint,
            SslOptions = new SslOptions { UseSsl = false }
        };

        Assert.Contains(
            EtcdTlsOptionsValidator.Validate(options),
            failure => failure.Contains("uses https://", StringComparison.Ordinal));
    }

    [Fact]
    public void UseSslTrueWithHttpEndpoint_Fails()
    {
        var options = new EtcdOptions
        {
            EndPoints = HttpEndpoint,
            SslOptions = new SslOptions { UseSsl = true }
        };

        Assert.Contains(
            EtcdTlsOptionsValidator.Validate(options),
            failure => failure.Contains("uses http://", StringComparison.Ordinal));
    }

    [Fact]
    public void MixedEndpointSchemes_Fail()
    {
        var options = new EtcdOptions
        {
            EndPoints = "http://127.0.0.1:2379,https://127.0.0.1:2380",
            SslOptions = new SslOptions { UseSsl = false }
        };

        Assert.Contains(
            EtcdTlsOptionsValidator.Validate(options),
            failure => failure.Contains("same URI scheme", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("ftp://127.0.0.1:2379")]
    [InlineData("not-a-url")]
    [InlineData("https:///missing-host")]
    public void InvalidEndpoint_Fails(string endpoint)
    {
        var options = new EtcdOptions
        {
            EndPoints = endpoint,
            SslOptions = new SslOptions { UseSsl = false }
        };

        Assert.Contains(
            EtcdTlsOptionsValidator.Validate(options),
            failure => failure.Contains("not a valid absolute", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/")]
    [InlineData(" / ")]
    public void RootKeyIsTheEtcdRoot_Fails(string rootKey)
    {
        var options = new EtcdOptions { RootKey = rootKey };

        Assert.Contains(
            EtcdTlsOptionsValidator.Validate(options),
            failure => failure.Contains("non-root prefix", StringComparison.Ordinal));
    }

    [Fact]
    public void CaBundleWithMultipleCertificates_Succeeds()
    {
        using var files = new TestCertificateFiles();
        using var firstCertificateAuthority = TestCertificateFactory.CreateCertificateAuthority();
        using var secondCertificateAuthority = TestCertificateFactory.CreateCertificateAuthority();
        var bundlePath = files.PathFor("ca-bundle.crt");
        File.WriteAllText(
            bundlePath,
            firstCertificateAuthority.ExportCertificatePem() +
            Environment.NewLine +
            secondCertificateAuthority.ExportCertificatePem());

        var options = new EtcdOptions
        {
            EndPoints = HttpsEndpoint,
            SslOptions = new SslOptions
            {
                UseSsl = true,
                ServerCertificateAuthorityPath = bundlePath
            }
        };

        Assert.Empty(EtcdTlsOptionsValidator.Validate(options));
    }

    [Fact]
    public void UseSslTrueWithoutTlsMaterial_Succeeds()
    {
        var options = new EtcdOptions
        {
            EndPoints = HttpsEndpoint,
            SslOptions = new SslOptions { UseSsl = true }
        };

        Assert.Empty(EtcdTlsOptionsValidator.Validate(options));
    }

    [Fact]
    public void UseSslTrueWithMissingCaFile_Fails()
    {
        using var files = new TestCertificateFiles();

        var options = new EtcdOptions
        {
            EndPoints = HttpsEndpoint,
            SslOptions = new SslOptions
            {
                UseSsl = true,
                ServerCertificateAuthorityPath = files.PathFor("missing-ca.crt")
            }
        };

        Assert.Contains(
            EtcdTlsOptionsValidator.Validate(options),
            failure => failure.Contains("does not exist", StringComparison.Ordinal));
    }

    [Fact]
    public void UseSslTrueWithCorruptCaFile_Fails()
    {
        using var files = new TestCertificateFiles();
        var path = files.PathFor("corrupt-ca.crt");
        File.WriteAllText(path, "this is not a certificate");

        var options = new EtcdOptions
        {
            EndPoints = HttpsEndpoint,
            SslOptions = new SslOptions
            {
                UseSsl = true,
                ServerCertificateAuthorityPath = path
            }
        };

        Assert.Contains(
            EtcdTlsOptionsValidator.Validate(options),
            failure => failure.Contains("could not be loaded", StringComparison.Ordinal));
    }

    [Fact]
    public void UseSslTrueWithMissingClientCertificateFile_Fails()
    {
        using var files = new TestCertificateFiles();

        var options = new EtcdOptions
        {
            EndPoints = HttpsEndpoint,
            SslOptions = new SslOptions
            {
                UseSsl = true,
                ClientCertificatePath = files.PathFor("missing-client.pfx")
            }
        };

        Assert.Contains(
            EtcdTlsOptionsValidator.Validate(options),
            failure => failure.Contains("does not exist", StringComparison.Ordinal));
    }

    [Fact]
    public void UseSslTrueWithMissingClientKeyFile_Fails()
    {
        using var files = new TestCertificateFiles();
        using var certificate = TestCertificateFactory.CreateSelfSignedClientCertificate();
        var certificatePath = TestCertificateFactory.WriteCertificatePem(files, "client.crt", certificate);

        var options = new EtcdOptions
        {
            EndPoints = HttpsEndpoint,
            SslOptions = new SslOptions
            {
                UseSsl = true,
                ClientCertificatePath = certificatePath,
                ClientCertificateKeyPath = files.PathFor("missing-client.key")
            }
        };

        Assert.Contains(
            EtcdTlsOptionsValidator.Validate(options),
            failure => failure.Contains("does not exist", StringComparison.Ordinal));
    }

    [Fact]
    public void InMemoryAndFileClientCertificateTogether_Fail()
    {
        using var files = new TestCertificateFiles();
        using var certificate = TestCertificateFactory.CreateSelfSignedClientCertificate();

        var options = new EtcdOptions
        {
            EndPoints = HttpsEndpoint,
            SslOptions = new SslOptions
            {
                UseSsl = true,
                ClientCertificate = certificate,
                ClientCertificatePath = files.PathFor("client.pfx")
            }
        };

        Assert.Contains(
            EtcdTlsOptionsValidator.Validate(options),
            failure => failure.Contains("cannot be combined", StringComparison.Ordinal));
    }

    [Fact]
    public void KeyPathWithoutCertificatePath_Fails()
    {
        var options = new EtcdOptions
        {
            EndPoints = HttpsEndpoint,
            SslOptions = new SslOptions
            {
                UseSsl = true,
                ClientCertificateKeyPath = "client.key"
            }
        };

        Assert.Contains(
            EtcdTlsOptionsValidator.Validate(options),
            failure => failure.Contains("requires ClientCertificatePath", StringComparison.Ordinal));
    }

    [Fact]
    public void PasswordWithoutCertificatePath_Fails()
    {
        var options = new EtcdOptions
        {
            EndPoints = HttpsEndpoint,
            SslOptions = new SslOptions
            {
                UseSsl = true,
                ClientCertificatePassword = "password"
            }
        };

        Assert.Contains(
            EtcdTlsOptionsValidator.Validate(options),
            failure => failure.Contains("requires ClientCertificatePath", StringComparison.Ordinal));
    }

    [Fact]
    public void InMemoryClientCertificateWithoutPrivateKey_Fails()
    {
        using var certificate = TestCertificateFactory.CreateSelfSignedClientCertificate();
        using var publicOnly = TestCertificateFactory.WithoutPrivateKey(certificate);

        var options = new EtcdOptions
        {
            EndPoints = HttpsEndpoint,
            SslOptions = new SslOptions
            {
                UseSsl = true,
                ClientCertificate = publicOnly
            }
        };

        Assert.Contains(
            EtcdTlsOptionsValidator.Validate(options),
            failure => failure.Contains("does not contain a private key", StringComparison.Ordinal));
    }

    [Fact]
    public void InMemoryClientCertificateWithoutClientAuthEku_Fails()
    {
        using var certificate = TestCertificateFactory.CreateSelfSignedClientCertificate(
            includeClientAuthEku: false);

        var options = new EtcdOptions
        {
            EndPoints = HttpsEndpoint,
            SslOptions = new SslOptions
            {
                UseSsl = true,
                ClientCertificate = certificate
            }
        };

        Assert.Contains(
            EtcdTlsOptionsValidator.Validate(options),
            failure => failure.Contains("clientAuth", StringComparison.Ordinal));
    }

    [Fact]
    public void PemClientCertificateWithoutClientAuthEku_Fails()
    {
        using var files = new TestCertificateFiles();
        using var certificate = TestCertificateFactory.CreateSelfSignedClientCertificate(
            includeClientAuthEku: false);
        var certificatePath = TestCertificateFactory.WriteCertificatePem(files, "client.crt", certificate);
        var keyPath = TestCertificateFactory.WritePrivateKeyPem(files, "client.key", certificate);

        var options = new EtcdOptions
        {
            EndPoints = HttpsEndpoint,
            SslOptions = new SslOptions
            {
                UseSsl = true,
                ClientCertificatePath = certificatePath,
                ClientCertificateKeyPath = keyPath
            }
        };

        Assert.Contains(
            EtcdTlsOptionsValidator.Validate(options),
            failure => failure.Contains("clientAuth", StringComparison.Ordinal));
    }

    [Fact]
    public void ExpiredInMemoryClientCertificate_Fails()
    {
        var now = DateTimeOffset.UtcNow;
        using var certificate = TestCertificateFactory.CreateSelfSignedClientCertificate(
            notBefore: now.AddDays(-2),
            notAfter: now.AddDays(-1));

        var options = new EtcdOptions
        {
            EndPoints = HttpsEndpoint,
            SslOptions = new SslOptions
            {
                UseSsl = true,
                ClientCertificate = certificate
            }
        };

        Assert.Contains(
            EtcdTlsOptionsValidator.Validate(options),
            failure => failure.Contains("is expired", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidInMemoryClientCertificate_Succeeds()
    {
        using var certificate = TestCertificateFactory.CreateSelfSignedClientCertificate();

        var options = new EtcdOptions
        {
            EndPoints = HttpsEndpoint,
            SslOptions = new SslOptions
            {
                UseSsl = true,
                ClientCertificate = certificate
            }
        };

        Assert.Empty(EtcdTlsOptionsValidator.Validate(options));
    }

    [Fact]
    public void ValidPemClientCertificateAndKey_Succeeds()
    {
        using var files = new TestCertificateFiles();
        using var certificate = TestCertificateFactory.CreateSelfSignedClientCertificate();
        var certificatePath = TestCertificateFactory.WriteCertificatePem(files, "client.crt", certificate);
        var keyPath = TestCertificateFactory.WritePrivateKeyPem(files, "client.key", certificate);

        var options = new EtcdOptions
        {
            EndPoints = HttpsEndpoint,
            SslOptions = new SslOptions
            {
                UseSsl = true,
                ClientCertificatePath = certificatePath,
                ClientCertificateKeyPath = keyPath
            }
        };

        Assert.Empty(EtcdTlsOptionsValidator.Validate(options));
    }

    [Fact]
    public void MismatchedPemCertificateAndKey_Fails()
    {
        using var files = new TestCertificateFiles();
        using var certificate = TestCertificateFactory.CreateSelfSignedClientCertificate();
        using var otherCertificate = TestCertificateFactory.CreateSelfSignedClientCertificate();
        var certificatePath = TestCertificateFactory.WriteCertificatePem(files, "client.crt", certificate);
        var keyPath = TestCertificateFactory.WritePrivateKeyPem(files, "other.key", otherCertificate);

        var options = new EtcdOptions
        {
            EndPoints = HttpsEndpoint,
            SslOptions = new SslOptions
            {
                UseSsl = true,
                ClientCertificatePath = certificatePath,
                ClientCertificateKeyPath = keyPath
            }
        };

        Assert.Contains(
            EtcdTlsOptionsValidator.Validate(options),
            failure => failure.Contains("could not be loaded", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidPkcs12ClientCertificate_Succeeds()
    {
        using var files = new TestCertificateFiles();
        using var certificate = TestCertificateFactory.CreateSelfSignedClientCertificate();
        var certificatePath = TestCertificateFactory.WritePkcs12(files, "client.pfx", certificate, "password");

        var options = new EtcdOptions
        {
            EndPoints = HttpsEndpoint,
            SslOptions = new SslOptions
            {
                UseSsl = true,
                ClientCertificatePath = certificatePath,
                ClientCertificatePassword = "password"
            }
        };

        Assert.Empty(EtcdTlsOptionsValidator.Validate(options));
    }

    [Fact]
    public void WrongPkcs12Password_FailsWithoutLeakingPassword()
    {
        using var files = new TestCertificateFiles();
        using var certificate = TestCertificateFactory.CreateSelfSignedClientCertificate();
        var certificatePath = TestCertificateFactory.WritePkcs12(files, "client.pfx", certificate, "correct-password");
        const string wrongPassword = "wrong-password";

        var options = new EtcdOptions
        {
            EndPoints = HttpsEndpoint,
            SslOptions = new SslOptions
            {
                UseSsl = true,
                ClientCertificatePath = certificatePath,
                ClientCertificatePassword = wrongPassword
            }
        };

        var failures = EtcdTlsOptionsValidator.Validate(options);

        Assert.NotEmpty(failures);
        Assert.DoesNotContain(wrongPassword, string.Join(' ', failures));
    }

    [Fact]
    public void ExpiredPemClientCertificate_Fails()
    {
        using var files = new TestCertificateFiles();
        var now = DateTimeOffset.UtcNow;
        using var certificate = TestCertificateFactory.CreateSelfSignedClientCertificate(
            notBefore: now.AddDays(-2),
            notAfter: now.AddDays(-1));
        var certificatePath = TestCertificateFactory.WriteCertificatePem(files, "client.crt", certificate);
        var keyPath = TestCertificateFactory.WritePrivateKeyPem(files, "client.key", certificate);

        var options = new EtcdOptions
        {
            EndPoints = HttpsEndpoint,
            SslOptions = new SslOptions
            {
                UseSsl = true,
                ClientCertificatePath = certificatePath,
                ClientCertificateKeyPath = keyPath
            }
        };

        Assert.Contains(
            EtcdTlsOptionsValidator.Validate(options),
            failure => failure.Contains("is expired", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateAndThrow_WhenValid_DoesNotThrow()
    {
        var options = new EtcdOptions { EndPoints = HttpsEndpoint, SslOptions = new SslOptions { UseSsl = true } };

        EtcdTlsOptionsValidator.ValidateAndThrow(options);
    }

    [Fact]
    public void ValidateAndThrow_WhenInvalid_ThrowsOptionsValidationException()
    {
        var options = new EtcdOptions { EndPoints = HttpEndpoint, SslOptions = new SslOptions { UseSsl = true } };

        var exception = Assert.Throws<OptionsValidationException>(
            () => EtcdTlsOptionsValidator.ValidateAndThrow(options));

        Assert.Equal(nameof(EtcdOptions), exception.OptionsName);
        Assert.Contains(exception.Failures, failure => failure.Contains("uses http://", StringComparison.Ordinal));
    }
}
