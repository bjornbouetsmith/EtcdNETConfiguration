using System;
using ROOT.Etcd.Configuration;

namespace ROOT.Etcd.ConfigurationTest;

public class EtcdOptionsTest
{
    [Fact]
    public void Defaults_AreExpected()
    {
        var options = new EtcdOptions();

        Assert.Equal("https://127.0.0.1:2379", options.EndPoints);
        Assert.Equal("/configuration", options.RootKey);
        Assert.Equal(TimeSpan.FromMinutes(1), options.RefreshInterval);
        Assert.True(options.RedactConfigurationValues);
    }

    [Fact]
    public void Properties_CanBeSet()
    {
        var options = new EtcdOptions
        {
            EndPoints = "http://localhost:2379,http://localhost:2380",
            RootKey = "/custom-root",
            RefreshInterval = TimeSpan.FromSeconds(30),
            RedactConfigurationValues = false
        };

        Assert.Equal("http://localhost:2379,http://localhost:2380", options.EndPoints);
        Assert.Equal("/custom-root", options.RootKey);
        Assert.Equal(TimeSpan.FromSeconds(30), options.RefreshInterval);
        Assert.False(options.RedactConfigurationValues);
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
    public void Validate_WithDefaultOptions_DoesNotThrow()
    {
        var options = new EtcdOptions();

        options.Validate();
    }

    [Fact]
    public void Validate_WithValidCustomOptions_DoesNotThrow()
    {
        var options = new EtcdOptions
        {
            EndPoints = "http://localhost:2379,http://localhost:2380",
            RootKey = "/custom-root",
            RefreshInterval = TimeSpan.FromSeconds(30),
            ConfigurationTimeout = TimeSpan.FromSeconds(5)
        };

        options.Validate();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Validate_WhenEndPointsIsMissing_Throws(string? endPoints)
    {
        var options = new EtcdOptions { EndPoints = endPoints };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("Endpoints must be set to a non empty string", exception.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Validate_WhenRootKeyIsMissing_Throws(string? rootKey)
    {
        var options = new EtcdOptions { RootKey = rootKey };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("RootKey must be set to a non empty string", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_WhenRefreshIntervalIsNotPositive_Throws(int seconds)
    {
        var options = new EtcdOptions { RefreshInterval = TimeSpan.FromSeconds(seconds) };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("RefreshInterval must be set to a timespan greater than 0", exception.Message);
    }

    [Fact]
    public void Validate_WhenRefreshIntervalIsAtTimerMaximum_DoesNotThrow()
    {
        var options = new EtcdOptions
        {
            RefreshInterval = TimeSpan.FromMilliseconds(uint.MaxValue - 1)
        };

        options.Validate();
    }

    [Fact]
    public void Validate_WhenRefreshIntervalIsOneMillisecondAboveTimerMaximum_Throws()
    {
        var options = new EtcdOptions
        {
            RefreshInterval = TimeSpan.FromMilliseconds((long)uint.MaxValue)
        };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("RefreshInterval must not exceed", exception.Message);
    }

    [Fact]
    public void Validate_WhenRefreshIntervalIsFarAboveTimerMaximum_Throws()
    {
        var options = new EtcdOptions { RefreshInterval = TimeSpan.FromDays(60) };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("RefreshInterval must not exceed", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_WhenConfigurationTimeoutIsNotPositive_Throws(int seconds)
    {
        var options = new EtcdOptions { ConfigurationTimeout = TimeSpan.FromSeconds(seconds) };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("ConfigurationTimeout must be set to a timespan greater than 0", exception.Message);
    }

    [Fact]
    public void Validate_WithMultipleInvalidValues_ReportsEveryError()
    {
        var options = new EtcdOptions
        {
            EndPoints = null,
            RootKey = null,
            RefreshInterval = TimeSpan.Zero,
            ConfigurationTimeout = TimeSpan.Zero
        };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.StartsWith("Etcd configuration invalid", exception.Message);
        Assert.Contains("Endpoints must be set to a non empty string", exception.Message);
        Assert.Contains("RootKey must be set to a non empty string", exception.Message);
        Assert.Contains("RefreshInterval must be set to a timespan greater than 0", exception.Message);
        Assert.Contains("ConfigurationTimeout must be set to a timespan greater than 0", exception.Message);
    }
}
