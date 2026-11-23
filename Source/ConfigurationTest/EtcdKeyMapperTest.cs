using ROOT.Etcd.Configuration;
using System;

namespace ROOT.Etcd.ConfigurationTest;

public class EtcdKeyMapperTest
{
    [Theory]
    [InlineData("/configuration/one", "/configuration:one")]
    [InlineData("/configuration/database/host", "/configuration:database:host")]
    [InlineData("/configuration/connectionstrings:0", "/configuration:connectionstrings:0")]
    [InlineData("/configuration/connectionstrings:10", "/configuration:connectionstrings:10")]
    [InlineData("/configuration/services:0/endpoints:1", "/configuration:services:0:endpoints:1")]
    [InlineData("/prefix/key/subkey/deepkey", "/prefix:key:subkey:deepkey")]
    [InlineData("/configuration/key:0:1", "/configuration:key:0:1")]
    [InlineData("/configuration/n\u00f8kkel/verd\u00ef", "/configuration:n\u00f8kkel:verd\u00ef")]
    [InlineData("/configuration/key with space/some-key/some.key", "/configuration:key with space:some-key:some.key")]
    [InlineData("configuration/one", "/configuration:one")]
    public void ToConfigurationKey_MapsValidKeys(string etcdKey, string expected)
    {
        Assert.Equal(expected, EtcdKeyMapper.ToConfigurationKey(etcdKey));
    }

    [Theory]
    [InlineData("/configuration/prefix:key")]
    [InlineData("/configuration/key:foo")]
    [InlineData("/configuration/key:")]
    [InlineData("/configuration/key::0")]
    [InlineData("/configuration/key:-1")]
    [InlineData("/configuration/key:+1")]
    [InlineData("/configuration/key:01")]
    [InlineData("/configuration/key:1.0")]
    public void ToConfigurationKey_WhenColonIsNotAnIndex_Throws(string etcdKey)
    {
        var exception = Assert.Throws<EtcdConfigurationKeyFormatException>(
            () => EtcdKeyMapper.ToConfigurationKey(etcdKey));

        Assert.Contains(etcdKey, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/configuration//key")]
    [InlineData("/configuration/key/")]
    public void ToConfigurationKey_WhenSegmentIsEmpty_Throws(string etcdKey)
    {
        var exception = Assert.Throws<EtcdConfigurationKeyFormatException>(
            () => EtcdKeyMapper.ToConfigurationKey(etcdKey));

        Assert.Contains("empty path segment", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    public void ToConfigurationKey_WhenKeyHasNoPath_Throws(string etcdKey)
    {
        Assert.Throws<EtcdConfigurationKeyFormatException>(
            () => EtcdKeyMapper.ToConfigurationKey(etcdKey));
    }

    [Theory]
    [InlineData("key", true)]
    [InlineData("key:0", true)]
    [InlineData("key:1", true)]
    [InlineData("key:10", true)]
    [InlineData("key:0:1", true)]
    [InlineData("key with space", true)]
    [InlineData("some-key.some_name", true)]
    [InlineData("n\u00f8kkel", true)]
    [InlineData("prefix:key", false)]
    [InlineData("prefix:key:0", false)]
    [InlineData("key:foo", false)]
    [InlineData("key:", false)]
    [InlineData("key::0", false)]
    [InlineData("key:-1", false)]
    [InlineData("key:+1", false)]
    [InlineData("key:01", false)]
    [InlineData("key:1.0", false)]
    [InlineData("", false)]
    public void IsValidSegment_EnforcesTheColonRule(string segment, bool expected)
    {
        Assert.Equal(expected, EtcdKeyMapper.IsValidSegment(segment));
    }

    [Theory]
    [InlineData("/prefix/key", "/prefix", true)]
    [InlineData("/prefix/key/subkey", "/prefix", true)]
    [InlineData("/prefix/key:0", "/prefix", true)]
    [InlineData("/prefix-other/key", "/prefix", false)]
    [InlineData("/prefix", "/prefix", false)]
    [InlineData("/prefix:key", "/prefix", false)]
    [InlineData("/Prefix/key", "/prefix", false)]
    [InlineData("/prefix/key", " /prefix ", true)]
    [InlineData("/prefix/key", "/prefix/", true)]
    public void IsInScope_UsesThePathBoundary(string etcdKey, string rootKey, bool expected)
    {
        Assert.Equal(expected, EtcdKeyMapper.IsInScope(etcdKey, rootKey));
    }

    [Theory]
    [InlineData("/prefix:key", "/prefix", true)]
    [InlineData("/prefix:key:0", "/prefix", true)]
    [InlineData("/prefix/key", "/prefix", false)]
    [InlineData("/prefix-other:key", "/prefix", false)]
    [InlineData("/prefix:key", " /prefix ", true)]
    public void IsOldFormat_DetectsTheRemovedHierarchy(string etcdKey, string rootKey, bool expected)
    {
        Assert.Equal(expected, EtcdKeyMapper.IsOldFormat(etcdKey, rootKey));
    }

    [Theory]
    [InlineData("/prefix", "/prefix")]
    [InlineData(" /prefix ", "/prefix")]
    [InlineData("/prefix/", "/prefix")]
    [InlineData("//prefix", "/prefix")]
    [InlineData("prefix", "/prefix")]
    [InlineData("", "/")]
    [InlineData(null, "/")]
    public void NormalizeRootKey_ReturnsASingleLeadingSlash(string? rootKey, string expected)
    {
        Assert.Equal(expected, EtcdKeyMapper.NormalizeRootKey(rootKey));
    }

    [Fact]
    public void CollisionException_IsAnInvalidOperationException()
    {
        var exception = new EtcdConfigurationKeyCollisionException("collision");

        Assert.IsAssignableFrom<InvalidOperationException>(exception);
    }

    [Fact]
    public void FormatException_IsAnInvalidOperationException()
    {
        var exception = new EtcdConfigurationKeyFormatException("format");

        Assert.IsAssignableFrom<InvalidOperationException>(exception);
    }
}
