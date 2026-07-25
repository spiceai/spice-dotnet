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

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Spice;
using Spice.Datasets;

namespace SpiceTest;

/// <summary>
/// Verifies the HTTP request the client actually puts on the wire for a dataset
/// refresh, using a loopback <see cref="HttpListener"/> as a stand-in runtime.
/// No Spice runtime or network access is required.
/// </summary>
public class RefreshDatasetRequestTest
{
    private sealed record CapturedRequest(string HttpMethod, string Path, string? ContentType, string Body);

    /// <summary>
    /// Runs <paramref name="action"/> against a loopback listener and returns the single
    /// request it received.
    /// </summary>
    private static async Task<CapturedRequest> CaptureAsync(Func<SpiceClient, Task> action)
    {
        var port = GetFreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        var serverTask = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync().ConfigureAwait(false);
            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync().ConfigureAwait(false);

            var captured = new CapturedRequest(
                context.Request.HttpMethod,
                context.Request.Url!.AbsolutePath,
                context.Request.ContentType,
                body);

            context.Response.StatusCode = (int)HttpStatusCode.Created;
            context.Response.Close();
            return captured;
        });

        using (var client = new SpiceClientBuilder()
                   .WithHttpAddress($"http://127.0.0.1:{port}")
                   .Build())
        {
            await action(client).ConfigureAwait(false);
        }

        return await serverTask.ConfigureAwait(false);
    }

    private static int GetFreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    [Test]
    public async Task Test_RefreshWithOptions_PostsOverridesToAccelerationRefreshEndpoint()
    {
        var request = await CaptureAsync(client => client.RefreshDatasetAsync(
            "taxi_trips",
            new RefreshOptions()
                .WithRefreshSql("SELECT * FROM taxi_trips WHERE tip_amount > 10.0")
                .WithRefreshMode(RefreshMode.Append)
                .WithMaxJitter(TimeSpan.FromSeconds(10))));

        var body = JsonSerializer.Deserialize<Dictionary<string, string>>(request.Body)!;

        Assert.Multiple(() =>
        {
            Assert.That(request.HttpMethod, Is.EqualTo("POST"));
            Assert.That(request.Path, Is.EqualTo("/v1/datasets/taxi_trips/acceleration/refresh"));
            Assert.That(request.ContentType, Does.Contain("application/json"));
            Assert.That(body["refresh_sql"], Is.EqualTo("SELECT * FROM taxi_trips WHERE tip_amount > 10.0"));
            Assert.That(body["refresh_mode"], Is.EqualTo("append"));
            Assert.That(body["refresh_jitter_max"], Is.EqualTo("10s"));
        });
    }

    [Test]
    public async Task Test_RefreshWithoutOptions_PostsEmptyJsonObject()
    {
        var request = await CaptureAsync(client => client.RefreshDatasetAsync("taxi_trips"));

        Assert.Multiple(() =>
        {
            Assert.That(request.HttpMethod, Is.EqualTo("POST"));
            Assert.That(request.Path, Is.EqualTo("/v1/datasets/taxi_trips/acceleration/refresh"));
            Assert.That(request.ContentType, Does.Contain("application/json"));
            Assert.That(request.Body, Is.EqualTo("{}"));
        });
    }

    [Test]
    public async Task Test_RefreshWithNullOptions_PostsEmptyJsonObject()
    {
        var request = await CaptureAsync(client => client.RefreshDatasetAsync("taxi_trips", null));

        Assert.That(request.Body, Is.EqualTo("{}"));
    }

    [Test]
    public async Task Test_RefreshWithPartialOptions_OmitsUnsetFields()
    {
        var request = await CaptureAsync(client => client.RefreshDatasetAsync(
            "taxi_trips",
            new RefreshOptions().WithRefreshMode(RefreshMode.Full)));

        var body = JsonSerializer.Deserialize<Dictionary<string, string>>(request.Body)!;

        Assert.Multiple(() =>
        {
            Assert.That(body, Has.Count.EqualTo(1));
            Assert.That(body["refresh_mode"], Is.EqualTo("full"));
        });
    }

    [Test]
    public void Test_RefreshWithBlankDatasetName_Throws()
    {
        using var client = new SpiceClientBuilder().Build();

        Assert.ThrowsAsync<ArgumentException>(
            async () => await client.RefreshDatasetAsync("  ", new RefreshOptions()));
    }
}
