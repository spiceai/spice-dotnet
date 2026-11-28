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


using Apache.Arrow;
using Apache.Arrow.Flight;
using Apache.Arrow.Flight.Server;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Spice;
using Spice.Flight;

namespace SpiceTest;

/// <summary>
/// A runtime sends each record batch as one Flight message, so a batch of wide rows can be
/// several MiB. gRPC's stock 4 MiB receive limit rejected those with RESOURCE_EXHAUSTED.
/// </summary>
[TestFixture]
[NonParallelizable]
public class FlightMessageSizeTest
{
    private const int Rows = 25_000;
    private const int PayloadBytes = 240;

    [Test]
    public void DefaultLimit_IsTheRuntimes100MiB()
    {
        Assert.That(SpiceFlightClient.MaxReceiveMessageSize, Is.EqualTo(100 * 1024 * 1024));
    }

    [Test]
    public async Task SqlAsync_ReadsABatchLargerThan4MiB()
    {
        await using var app = await StartServerAsync();
        var hostPort = new Uri(app.Urls.First()).Authority;

        using var client = new SpiceClientBuilder()
            .WithFlightAddress($"grpc://{hostPort}")
            .WithMaxRetries(0)
            .Build();

        var reader = await client.SqlAsync("SELECT * FROM wide");
        long rows = 0;
        await foreach (var batch in reader)
        {
            rows += batch.Length;
        }

        Assert.That(rows, Is.EqualTo(Rows));
    }

    private static async Task<WebApplication> StartServerAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o =>
            o.Listen(System.Net.IPAddress.Loopback, 0, lo => lo.Protocols = HttpProtocols.Http2));
        builder.Services.AddGrpc().AddFlightServer<WideBatchFlightServer>();
        var app = builder.Build();
        app.MapFlightEndpoint();
        await app.StartAsync().ConfigureAwait(false);
        return app;
    }

    /// <summary>Returns one ~6 MiB batch as a single message, as an unsplit runtime does.</summary>
    private sealed class WideBatchFlightServer : FlightServer
    {
        private static readonly RecordBatch Batch = BuildBatch();

        private static RecordBatch BuildBatch()
        {
            var ids = new Int64Array.Builder();
            var payloads = new StringArray.Builder();
            var payload = new string('x', PayloadBytes);
            for (var i = 0; i < Rows; i++)
            {
                ids.Append(i);
                payloads.Append(payload);
            }

            var schema = new Schema.Builder()
                .Field(f => f.Name("id").DataType(Apache.Arrow.Types.Int64Type.Default))
                .Field(f => f.Name("payload").DataType(Apache.Arrow.Types.StringType.Default))
                .Build();
            return new RecordBatch(schema, new IArrowArray[] { ids.Build(), payloads.Build() }, Rows);
        }

        public override Task<FlightInfo> GetFlightInfo(FlightDescriptor request, ServerCallContext context)
        {
            var endpoint = new FlightEndpoint(new FlightTicket("wide"), System.Array.Empty<FlightLocation>());
            return Task.FromResult(new FlightInfo(Batch.Schema, request, new[] { endpoint }, Rows, -1));
        }

        public override async Task DoGet(FlightTicket ticket, FlightServerRecordBatchStreamWriter responseStream,
            ServerCallContext context)
        {
            await responseStream.WriteAsync(Batch).ConfigureAwait(false);
        }
    }
}
