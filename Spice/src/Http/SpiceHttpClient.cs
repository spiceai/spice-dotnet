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

using System.Net.Http.Headers;
using System.Text;
using Spice.Auth;

namespace Spice.Http;

/// <summary>
/// HTTP client for Spice runtime operations.
/// </summary>
internal class SpiceHttpClient : ISpiceHttpClient
{
    private readonly HttpClient _httpClient;
    private readonly string _httpAddress;
    private bool _disposed;

    internal SpiceHttpClient(string httpAddress, string? appId, string? apiKey, string? userAgent,
        string? tlsClientCertFile = null, string? tlsClientKeyFile = null, string? tlsRootCertFile = null)
    {
#if NET8_0_OR_GREATER
        ArgumentException.ThrowIfNullOrWhiteSpace(httpAddress);
#else
        ThrowHelper.ThrowIfNullOrWhiteSpace(httpAddress, nameof(httpAddress));
#endif

        _httpAddress = httpAddress;

#if NET8_0_OR_GREATER
        if (tlsClientCertFile != null || tlsRootCertFile != null)
        {
            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            };
            if (tlsClientCertFile != null && tlsClientKeyFile != null)
            {
                var clientCert = System.Security.Cryptography.X509Certificates.X509Certificate2.CreateFromPemFile(
                    tlsClientCertFile, tlsClientKeyFile);
                handler.SslOptions.ClientCertificates =
                    new System.Security.Cryptography.X509Certificates.X509Certificate2Collection { clientCert };
            }
            if (tlsRootCertFile != null)
            {
#pragma warning disable SYSLIB0057
                var caCert = new System.Security.Cryptography.X509Certificates.X509Certificate2(tlsRootCertFile);
#pragma warning restore SYSLIB0057
                handler.SslOptions.RemoteCertificateValidationCallback = (sender, cert, chain, errors) =>
                {
                    if (errors == System.Net.Security.SslPolicyErrors.None) return true;
                    if (cert == null || chain == null) return false;
                    chain.ChainPolicy.TrustMode = System.Security.Cryptography.X509Certificates.X509ChainTrustMode.CustomRootTrust;
                    chain.ChainPolicy.CustomTrustStore.Add(caCert);
                    return chain.Build(new System.Security.Cryptography.X509Certificates.X509Certificate2(cert));
                };
            }
            _httpClient = new HttpClient(handler);
        }
        else
#endif
        {
            _httpClient = new HttpClient();
        }

        // Set authorization if credentials provided
        if (!string.IsNullOrEmpty(appId) && !string.IsNullOrEmpty(apiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization = AuthHeaderBuilder.BasicAuth(appId!, apiKey!);
        }

        // Set user agent
        if (!string.IsNullOrEmpty(userAgent))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", userAgent);
        }
    }

    /// <summary>
    /// Runs the query against the HTTP endpoint (for future use).
    /// </summary>
    /// <param name="sql">SQL to be executed against Spice</param>
    /// <returns>A task representing asynchronous operation</returns>
    /// <exception cref="System.ArgumentException">Thrown when provided sql is null or empty</exception>
    /// <exception cref="System.Net.Http.HttpRequestException">Thrown when the HTTP request fails</exception>
    public Task<string> QueryAsync(string sql)
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
#else
        if (_disposed) throw new ObjectDisposedException(GetType().FullName);
        ThrowHelper.ThrowIfNullOrWhiteSpace(sql, nameof(sql));
#endif
        throw new NotImplementedException("HTTP query endpoint not yet implemented");
    }

    /// <summary>
    /// Refreshes a dataset in the Spice runtime.
    /// </summary>
    /// <param name="datasetName">The name of the dataset to refresh</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <exception cref="System.ArgumentException">Thrown when datasetName is null or empty</exception>
    /// <exception cref="System.Net.Http.HttpRequestException">Thrown when the HTTP request fails</exception>
    public async Task RefreshDatasetAsync(string datasetName)
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetName);
#else
        if (_disposed) throw new ObjectDisposedException(GetType().FullName);
        ThrowHelper.ThrowIfNullOrWhiteSpace(datasetName, nameof(datasetName));
#endif

        var url = $"{_httpAddress}/v1/datasets/{datasetName}/acceleration/refresh";
        var response = await _httpClient.PostAsync(url, null).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Releases all resources used by the <see cref="SpiceHttpClient"/>.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the unmanaged resources used by the <see cref="SpiceHttpClient"/> and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            _httpClient?.Dispose();
        }

        _disposed = true;
    }
}
