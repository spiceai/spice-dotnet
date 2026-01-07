/*
Copyright 2024 The Spice.ai OSS Authors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using Grpc.Core;
using Polly;
using Polly.Retry;

namespace Spice.Common;

/// <summary>
/// Factory for creating retry policies used by Spice clients.
/// Centralizes retry logic configuration for consistency across Flight and ADBC clients.
/// </summary>
internal static class RetryPolicyFactory
{
    /// <summary>
    /// Status codes that should trigger a retry.
    /// </summary>
    private static readonly HashSet<string> RetryableErrorMessages = new(StringComparer.OrdinalIgnoreCase)
    {
        "unavailable",
        "deadline exceeded",
        "aborted",
        "internal",
        "unknown"
    };

    /// <summary>
    /// Creates an async retry policy for RPC exceptions (used by Flight client).
    /// </summary>
    public static AsyncRetryPolicy CreateRpcRetryPolicy(int maxRetries, Action<Exception, TimeSpan, int>? onRetry = null)
    {
        return Policy
            .Handle<RpcException>(ex =>
                ex.Status.StatusCode is StatusCode.Unavailable
                    or StatusCode.DeadlineExceeded
                    or StatusCode.Aborted
                    or StatusCode.Internal
                    or StatusCode.Unknown)
            .WaitAndRetryAsync(
                retryCount: maxRetries,
                sleepDurationProvider: CalculateSleepDuration,
                onRetry: (exception, timespan, retryAttempt, _) =>
                {
                    onRetry?.Invoke(exception, timespan, retryAttempt);
                });
    }

    /// <summary>
    /// Creates an async retry policy for general exceptions (used by ADBC client).
    /// </summary>
    public static AsyncRetryPolicy CreateGeneralRetryPolicy<TException>(
        int maxRetries,
        Func<TException, bool>? additionalPredicate = null,
        Action<Exception, TimeSpan, int>? onRetry = null) where TException : Exception
    {
        return Policy
            .Handle<TException>(ex => additionalPredicate?.Invoke(ex) ?? true)
            .Or<Exception>(IsRetryableException)
            .WaitAndRetryAsync(
                retryCount: maxRetries,
                sleepDurationProvider: CalculateSleepDuration,
                onRetry: (exception, timespan, retryAttempt, _) =>
                {
                    onRetry?.Invoke(exception, timespan, retryAttempt);
                });
    }

    /// <summary>
    /// Calculates the sleep duration for exponential backoff.
    /// </summary>
    public static TimeSpan CalculateSleepDuration(int retryAttempt)
    {
        return TimeSpan.FromSeconds(retryAttempt * 1.5);
    }

    /// <summary>
    /// Determines if an exception is retryable based on its message.
    /// </summary>
    public static bool IsRetryableException(Exception ex)
    {
        var message = ex.Message.ToLowerInvariant();
        foreach (var errorMessage in RetryableErrorMessages)
        {
            if (message.Contains(errorMessage))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Default retry logging action.
    /// </summary>
    public static void LogRetry(string clientType, Exception exception, TimeSpan timespan, int retryAttempt)
    {
        Console.WriteLine($"{clientType} request failed. Waiting {timespan} before next retry. Retry attempt {retryAttempt}. Error: {exception.Message}");
    }
}
