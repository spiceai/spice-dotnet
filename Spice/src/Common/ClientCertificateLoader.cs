/*
Copyright 2026 The Spice.ai OSS Authors

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

// mTLS client certificates are only supported on NET8_0_OR_GREATER (see the guards
// around both call sites in SpiceFlightClient/SpiceHttpClient); netstandard2.0 has
// neither X509Certificate2.CreateFromPemFile nor OperatingSystem.IsWindows.
#if NET8_0_OR_GREATER
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Spice.Common;

/// <summary>
/// Loads a PEM-encoded client certificate for use as a TLS client (mTLS) credential.
/// </summary>
internal static class ClientCertificateLoader
{
    /// <summary>
    /// Loads the certificate and key at the given paths, in a form usable as an
    /// <see cref="SslStream"/>/Schannel client certificate on every platform this
    /// SDK targets.
    /// </summary>
    /// <remarks>
    /// <see cref="X509Certificate2.CreateFromPemFile(string, string?)"/> attaches the private
    /// key as an ephemeral, in-memory key. That's fine for the OpenSSL-backed TLS stack on
    /// macOS/Linux, but Windows' native Schannel provider refuses to present an ephemeral-keyed
    /// certificate for client authentication — <c>AuthenticateAsClientAsync</c> fails with
    /// "Authentication failed because the platform does not support ephemeral keys." This is a
    /// by-design Windows/Schannel limitation, not a .NET bug (see
    /// https://github.com/dotnet/runtime/issues/23749, closed as external/by-design) — there is
    /// no flag or alternate loading API that avoids it. The fix is the same one ASP.NET Core's
    /// Kestrel applies when loading a PEM certificate+key on Windows: round-trip the certificate
    /// through a PKCS#12 export/import so the private key is backed by a real (non-ephemeral)
    /// key container. On non-Windows platforms this round trip is unnecessary, so it's skipped
    /// to keep behavior byte-identical to a direct load.
    /// </remarks>
    public static X509Certificate2 LoadForClientAuth(string certPemFilePath, string keyPemFilePath)
    {
        var certificate = X509Certificate2.CreateFromPemFile(certPemFilePath, keyPemFilePath);
        if (!OperatingSystem.IsWindows())
        {
            return certificate;
        }

        using (certificate)
        {
            var pkcs12Bytes = certificate.Export(X509ContentType.Pkcs12, password: (string?)null);
#if NET9_0_OR_GREATER
            return X509CertificateLoader.LoadPkcs12(pkcs12Bytes, password: (string?)null, X509KeyStorageFlags.DefaultKeySet);
#else
#pragma warning disable SYSLIB0057 // X509CertificateLoader isn't available pre-net9.0; the pattern below matches this SDK's existing root-certificate loading.
            return new X509Certificate2(pkcs12Bytes, (string?)null, X509KeyStorageFlags.DefaultKeySet);
#pragma warning restore SYSLIB0057
#endif
        }
    }
}
#endif
