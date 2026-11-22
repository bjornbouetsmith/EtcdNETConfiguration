using Grpc.Core;
using Microsoft.Extensions.Options;
using System;
using System.Security.Authentication;
using System.Security.Cryptography;

namespace ROOT.Etcd.Configuration;

/// <summary>
/// Classifies polling and startup failures as transient or permanent. Permanent failures
/// usually need a configuration or credential change and will not recover by retrying.
/// </summary>
internal static class EtcdFailureClassifier
{
    /// <summary>
    /// Gets whether the exception represents a permanent configuration, authentication,
    /// authorization, or TLS failure.
    /// </summary>
    internal static bool IsPermanent(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is OptionsValidationException or
            EtcdConfigurationLimitExceededException or
            AuthenticationException or
            CryptographicException or
            UnauthorizedAccessException)
        {
            return true;
        }

        if (exception is RpcException rpcException && IsPermanentStatus(rpcException.StatusCode))
        {
            return true;
        }

        if (exception is AggregateException aggregateException)
        {
            foreach (var innerException in aggregateException.InnerExceptions)
            {
                if (IsPermanent(innerException))
                {
                    return true;
                }
            }

            return false;
        }

        return exception.InnerException is not null && IsPermanent(exception.InnerException);
    }

    private static bool IsPermanentStatus(StatusCode statusCode) =>
        statusCode is StatusCode.Unauthenticated
            or StatusCode.PermissionDenied
            or StatusCode.InvalidArgument
            or StatusCode.FailedPrecondition
            or StatusCode.Unimplemented;
}
