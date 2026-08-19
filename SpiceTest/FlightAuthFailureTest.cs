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
using NUnit.Framework;
using Spice.Flight;

namespace SpiceTest;

/// <summary>
/// Unit tests for how <see cref="SpiceFlightClient"/> describes why a handshake
/// failed to yield an authorization token.
/// </summary>
[TestFixture]
public class FlightAuthFailureTest
{
    [Test]
    public void NoFailure_ReturnsNoTokenMessage()
    {
        var reason = SpiceFlightClient.DescribeAuthFailure(null);

        Assert.That(reason, Is.EqualTo("the runtime returned no authorization token."));
    }

    [Test]
    public void RpcFailure_DescribesStatusAndDetail()
    {
        var ex = new RpcException(new Status(StatusCode.Unauthenticated, "invalid api key"));

        var reason = SpiceFlightClient.DescribeAuthFailure(ex);

        Assert.That(reason, Is.EqualTo("Unauthenticated - invalid api key."));
    }

    [Test]
    public void IncompleteHandshake_ReturnsIncompleteHandshakeMessage()
    {
        var ex = new InvalidOperationException("trailers are not available");

        var reason = SpiceFlightClient.DescribeAuthFailure(ex);

        Assert.That(reason, Is.EqualTo("the handshake did not complete before trailers could be read."));
    }
}
