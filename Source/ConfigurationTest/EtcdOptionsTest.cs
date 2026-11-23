using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using ROOT.Etcd.Configuration;

namespace ROOT.Etcd.ConfigurationTest;

public class EtcdOptionsTest
{
    [Fact]
    public void Defaults_AreExpected()
    {
        var options = new EtcdOptions();

        Assert.Equal("http://127.0.0.1:2379", options.EndPoints);
        Assert.Equal("/configuration", options.RootKey);
        Assert.Equal(TimeSpan.FromMinutes(1), options.RefreshInterval);
        Assert.True(options.RedactConfigurationValues);
        Assert.Equal(10_000, options.MaximumConfigurationKeyCount);
        Assert.Equal(8 * 1024 * 1024, options.MaximumConfigurationSizeBytes);
        Assert.Equal(5, options.MaximumConsecutivePermanentFailures);
        Assert.Null(options.SslOptions);
    }

    [Fact]
    public void Properties_CanBeSet()
    {
        var options = new EtcdOptions
        {
            EndPoints = "http://localhost:2379,http://localhost:2380",
            RootKey = "/custom-root",
            RefreshInterval = TimeSpan.FromSeconds(30),
            RedactConfigurationValues = false,
            MaximumConfigurationKeyCount = 25,
            MaximumConfigurationSizeBytes = 1024,
            MaximumConsecutivePermanentFailures = 2,
            SslOptions = new SslOptions
            {
                UseSsl = true,
                ServerCertificateAuthorityPath = "/certs/ca.crt"
            }
        };

        Assert.Equal("http://localhost:2379,http://localhost:2380", options.EndPoints);
        Assert.Equal("/custom-root", options.RootKey);
        Assert.Equal(TimeSpan.FromSeconds(30), options.RefreshInterval);
        Assert.False(options.RedactConfigurationValues);
        Assert.Equal(25, options.MaximumConfigurationKeyCount);
        Assert.Equal(1024, options.MaximumConfigurationSizeBytes);
        Assert.Equal(2, options.MaximumConsecutivePermanentFailures);
        Assert.NotNull(options.SslOptions);
        Assert.True(options.SslOptions.UseSsl);
        Assert.Equal("/certs/ca.crt", options.SslOptions.ServerCertificateAuthorityPath);
    }

    [Fact]
    public void OptionsWithTheSameValues_AreEqual()
    {
        var first = new EtcdOptions
        {
            EndPoints = "http://localhost:2379",
            RootKey = "/custom-root",
            RefreshInterval = TimeSpan.FromSeconds(30)
        };
        var second = new EtcdOptions
        {
            EndPoints = "http://localhost:2379",
            RootKey = "/custom-root",
            RefreshInterval = TimeSpan.FromSeconds(30)
        };

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Validation_WithDefaultOptions_DoesNotReturnErrors()
    {
        var options = new EtcdOptions();

        Assert.Empty(Validate(options));
    }

    [Fact]
    public void Validation_WithValidCustomOptions_DoesNotReturnErrors()
    {
        var options = new EtcdOptions
        {
            EndPoints = "http://localhost:2379,http://localhost:2380",
            RootKey = "/custom-root",
            RefreshInterval = TimeSpan.FromSeconds(30),
            ConfigurationTimeout = TimeSpan.FromSeconds(5)
        };

        Assert.Empty(Validate(options));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Validation_WhenEndPointsIsMissing_ReturnsError(string? endPoints)
    {
        var options = new EtcdOptions { EndPoints = endPoints };

        var errors = Validate(options);

        Assert.Contains(errors, error =>
            error.ErrorMessage == "Endpoints must be set to a non empty string");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Validation_WhenRootKeyIsMissing_ReturnsError(string? rootKey)
    {
        var options = new EtcdOptions { RootKey = rootKey };

        var errors = Validate(options);

        Assert.Contains(errors, error =>
            error.ErrorMessage == "RootKey must be set to a non empty string");
    }

    [Theory]
    [InlineData("/")]
    [InlineData(" / ")]
    public void Validation_WhenRootKeyIsTheEtcdRoot_ReturnsError(string rootKey)
    {
        var options = new EtcdOptions { RootKey = rootKey };

        var errors = Validate(options);

        Assert.Contains(errors, error =>
            error.ErrorMessage == "RootKey must be a non-root prefix; '/' would load every key in etcd.");
    }

    [Fact]
    public void Validation_WhenRootKeyIsAPrefix_DoesNotReturnErrors()
    {
        var options = new EtcdOptions { RootKey = "/configuration" };

        Assert.Empty(Validate(options));
    }

    [Fact]
    public void Validation_WhenRootKeyHasSurroundingWhitespace_DoesNotReturnErrors()
    {
        var options = new EtcdOptions { RootKey = " /configuration " };

        Assert.Empty(Validate(options));
    }

    [Theory]
    [InlineData("configuration", "RootKey must start with '/'.")]
    [InlineData("/configuration/", "RootKey must not end with '/'.")]
    [InlineData("/configuration//nested", "RootKey must not contain empty path segments ('//').")]
    [InlineData("/configuration:key", "RootKey must not contain ':'; use '/' to separate etcd path segments.")]
    public void Validation_WhenRootKeyIsNotACanonicalPath_ReturnsError(string rootKey, string expectedError)
    {
        var options = new EtcdOptions { RootKey = rootKey };

        var errors = Validate(options);

        Assert.Contains(errors, error => error.ErrorMessage == expectedError);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validation_WhenMaximumConfigurationKeyCountIsNotPositive_ReturnsError(int maximumKeyCount)
    {
        var options = new EtcdOptions { MaximumConfigurationKeyCount = maximumKeyCount };

        var errors = Validate(options);

        Assert.Contains(errors, error =>
            error.ErrorMessage == "MaximumConfigurationKeyCount must be at least 1");
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void Validation_WhenMaximumConfigurationSizeIsNotPositive_ReturnsError(long maximumSize)
    {
        var options = new EtcdOptions { MaximumConfigurationSizeBytes = maximumSize };

        var errors = Validate(options);

        Assert.Contains(errors, error =>
            error.ErrorMessage == "MaximumConfigurationSizeBytes must be at least 1");
    }

    [Fact]
    public void Validation_WhenMaximumConsecutivePermanentFailuresIsNegative_ReturnsError()
    {
        var options = new EtcdOptions { MaximumConsecutivePermanentFailures = -1 };

        var errors = Validate(options);

        Assert.Contains(errors, error =>
            error.ErrorMessage == "MaximumConsecutivePermanentFailures must be zero or greater");
    }

    [Fact]
    public void Validation_WhenRefreshIntervalIsNotPositive_ReturnsError()
    {
        var options = new EtcdOptions { RefreshInterval = TimeSpan.FromSeconds(-1) };

        var errors = Validate(options);

        Assert.Contains(errors, error =>
            error.ErrorMessage ==
            "RefreshInterval must be set to a timespan greater than or equal to 0 and must not exceed 24 hours");
    }

    [Fact]
    public void Validation_WhenRefreshIntervalIsAtMinimum_DoesNotReturnErrors()
    {
        var options = new EtcdOptions
        {
            RefreshInterval = TimeSpan.Zero
        };

        Assert.Empty(Validate(options));
    }

    [Fact]
    public void Validation_WhenRefreshIntervalIsAtTimerMaximum_DoesNotReturnErrors()
    {
        var options = new EtcdOptions
        {
            RefreshInterval = TimeSpan.FromHours(24)
        };

        Assert.Empty(Validate(options));
    }

    [Fact]
    public void Validation_WhenRefreshIntervalIsOneMillisecondAboveTimerMaximum_ReturnsError()
    {
        var options = new EtcdOptions
        {
            RefreshInterval = TimeSpan.FromDays(1).Add(TimeSpan.FromMilliseconds(1)),
        };

        var errors = Validate(options);

        Assert.Contains(errors, error =>
            error.ErrorMessage ==
            "RefreshInterval must be set to a timespan greater than or equal to 0 and must not exceed 24 hours");
    }

    [Fact]
    public void Validation_WhenRefreshIntervalIsFarAboveTimerMaximum_ReturnsError()
    {
        var options = new EtcdOptions { RefreshInterval = TimeSpan.FromDays(60) };

        var errors = Validate(options);

        Assert.Contains(errors, error =>
            error.ErrorMessage ==
            "RefreshInterval must be set to a timespan greater than or equal to 0 and must not exceed 24 hours");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validation_WhenConfigurationTimeoutIsNotPositive_ReturnsError(int seconds)
    {
        var options = new EtcdOptions { ConfigurationTimeout = TimeSpan.FromSeconds(seconds) };

        var errors = Validate(options);

        Assert.Contains(errors, error =>
            error.ErrorMessage == "ConfigurationTimeout must be set to a timespan greater than 0 and less than 60 seconds");
    }

    [Fact]
    public void Validation_WithMultipleInvalidValues_ReportsEveryError()
    {
        var options = new EtcdOptions
        {
            EndPoints = null,
            RootKey = null,
            RefreshInterval = TimeSpan.FromSeconds(-1),
            ConfigurationTimeout = TimeSpan.Zero
        };

        var errors = Validate(options);

        Assert.Contains(errors, error =>
            error.ErrorMessage == "Endpoints must be set to a non empty string");
        Assert.Contains(errors, error =>
            error.ErrorMessage == "RootKey must be set to a non empty string");
        Assert.Contains(errors, error =>
            error.ErrorMessage ==
            "RefreshInterval must be set to a timespan greater than or equal to 0 and must not exceed 24 hours");
        Assert.Contains(errors, error =>
            error.ErrorMessage == "ConfigurationTimeout must be set to a timespan greater than 0 and less than 60 seconds");
    }

    private static List<ValidationResult> Validate(EtcdOptions options)
    {
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(
            options,
            new ValidationContext(options),
            results,
            validateAllProperties: true);

        return results;
    }
}
