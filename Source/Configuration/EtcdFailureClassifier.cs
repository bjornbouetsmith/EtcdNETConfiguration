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

        switch (exception)
        {
            case OptionsValidationException or
                EtcdConfigurationLimitExceededException or
                EtcdConfigurationKeyFormatException or
                EtcdConfigurationKeyCollisionException or
                AuthenticationException or
                CryptographicException or
                UnauthorizedAccessException:
            case RpcException rpcException when IsPermanentStatus(rpcException.StatusCode):
                return true;
            case AggregateException aggregateException:
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
            default:
                return exception.InnerException is not null && IsPermanent(exception.InnerException);
        }
    }

    private static bool IsPermanentStatus(StatusCode statusCode) =>
        statusCode is StatusCode.Unauthenticated
            or StatusCode.PermissionDenied
            or StatusCode.InvalidArgument
            or StatusCode.FailedPrecondition
            or StatusCode.Unimplemented;
}
