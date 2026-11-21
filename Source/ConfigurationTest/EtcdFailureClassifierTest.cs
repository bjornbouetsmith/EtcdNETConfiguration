using Grpc.Core;
using Microsoft.Extensions.Options;
using ROOT.Etcd.Configuration;
using System;
using System.Security.Authentication;

namespace ROOT.Etcd.ConfigurationTest;

public class EtcdFailureClassifierTest
{
    [Fact]
    public void IsPermanent_WhenOptionsValidationFails_ReturnsTrue()
    {
        var exception = new OptionsValidationException(
            nameof(EtcdOptions),
            typeof(EtcdOptions),
            ["invalid"]);

        Assert.True(EtcdFailureClassifier.IsPermanent(exception));
    }

    [Theory]
    [InlineData(StatusCode.Unauthenticated)]
    [InlineData(StatusCode.PermissionDenied)]
    [InlineData(StatusCode.InvalidArgument)]
    [InlineData(StatusCode.FailedPrecondition)]
    [InlineData(StatusCode.Unimplemented)]
    public void IsPermanent_WhenGrpcStatusIsPermanent_ReturnsTrue(StatusCode statusCode)
    {
        var exception = new RpcException(new Status(statusCode, "permanent"));

        Assert.True(EtcdFailureClassifier.IsPermanent(exception));
    }

    [Theory]
    [InlineData(StatusCode.Unavailable)]
    [InlineData(StatusCode.DeadlineExceeded)]
    [InlineData(StatusCode.Cancelled)]
    [InlineData(StatusCode.Internal)]
    public void IsPermanent_WhenGrpcStatusIsTransient_ReturnsFalse(StatusCode statusCode)
    {
        var exception = new RpcException(new Status(statusCode, "transient"));

        Assert.False(EtcdFailureClassifier.IsPermanent(exception));
    }

    [Fact]
    public void IsPermanent_WhenInnerExceptionIsPermanent_ReturnsTrue()
    {
        var exception = new InvalidOperationException("wrapped", new AuthenticationException("handshake failed"));

        Assert.True(EtcdFailureClassifier.IsPermanent(exception));
    }

    [Fact]
    public void IsPermanent_WhenAggregateContainsPermanentException_ReturnsTrue()
    {
        var exception = new AggregateException(
            new InvalidOperationException("transient"),
            new AuthenticationException("permanent"));

        Assert.True(EtcdFailureClassifier.IsPermanent(exception));
    }

    [Fact]
    public void IsPermanent_WhenExceptionIsOrdinaryTransientError_ReturnsFalse()
    {
        Assert.False(EtcdFailureClassifier.IsPermanent(new InvalidOperationException("boom")));
    }
}
