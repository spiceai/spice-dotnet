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
using Spice.Nsql;

namespace SpiceTest;

/// <summary>
/// Covers the wire contract of <c>/v1/nsql</c>: request serialization, response
/// deserialization, and the HTTP request the client actually puts on the wire, using a
/// loopback <see cref="HttpListener"/> as a stand-in runtime. No Spice runtime or network
/// access is required.
/// </summary>
public class NsqlTest
{
    // ==================== Request serialization ====================

    private static string Serialize(NsqlRequest request) => JsonSerializer.Serialize(request);

    [Test]
    public void Test_Request_QueryOnly_OmitsOptionalFields()
    {
        var json = Serialize(new NsqlRequest("top 5 customers by revenue"));

        Assert.That(json, Is.EqualTo("{\"query\":\"top 5 customers by revenue\"}"));
    }

    [Test]
    public void Test_Request_AllOptions_UseWireNames()
    {
        var request = new NsqlRequest("top 5 customers by revenue")
        {
            Model = "nsql-model",
            Datasets = new[] { "sales" },
            SampleDataEnabled = true,
            PromptCacheKey = "sales-dashboard",
        };

        using var document = JsonDocument.Parse(Serialize(request));
        var root = document.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("query").GetString(), Is.EqualTo("top 5 customers by revenue"));
            Assert.That(root.GetProperty("model").GetString(), Is.EqualTo("nsql-model"));
            Assert.That(root.GetProperty("datasets").GetArrayLength(), Is.EqualTo(1));
            Assert.That(root.GetProperty("sample_data_enabled").GetBoolean(), Is.True);
            Assert.That(root.GetProperty("prompt_cache_key").GetString(), Is.EqualTo("sales-dashboard"));
        });
    }

    [Test]
    public void Test_Request_FalseSampling_IsOmitted()
    {
        // sample_data_enabled defaults to false server-side, so omitting it rather than
        // sending false keeps the body minimal.
        var json = Serialize(new NsqlRequest("how many orders") { SampleDataEnabled = false });

        Assert.That(json, Does.Not.Contain("sample_data_enabled"));
    }

    [Test]
    public void Test_Request_EmptyDatasets_IsSentNotDropped()
    {
        // Empty is a real value the caller supplied, distinct from unset - matches how
        // SearchRequest treats empty collections.
        var json = Serialize(new NsqlRequest("how many orders") { Datasets = Array.Empty<string>() });

        Assert.That(json, Does.Contain("\"datasets\":[]"));
    }

    // ==================== Response deserialization ====================

    private static NsqlResponse Deserialize(string json) =>
        JsonSerializer.Deserialize<NsqlResponse>(json)!;

    [Test]
    public void Test_Response_DeserializesWireFormat()
    {
        const string body = """
        {
            "row_count": 2,
            "schema": {
                "fields": [
                    {"name": "customer_id", "data_type": "Utf8", "nullable": false},
                    {"name": "ts", "data_type": {"Timestamp": ["Nanosecond", null]}, "nullable": true}
                ]
            },
            "data": [
                {"customer_id": "12345", "ts": 1724716542},
                {"customer_id": "67890", "ts": 1724716543}
            ],
            "sql": "SELECT customer_id, ts FROM sales LIMIT 2"
        }
        """;

        var response = Deserialize(body);

        Assert.Multiple(() =>
        {
            Assert.That(response.SQL, Is.EqualTo("SELECT customer_id, ts FROM sales LIMIT 2"));
            Assert.That(response.RowCount, Is.EqualTo(2));
            Assert.That(response.Data, Has.Count.EqualTo(2));
            Assert.That(response.Data[0]["customer_id"].GetString(), Is.EqualTo("12345"));
            Assert.That(response.Schema.Fields, Has.Count.EqualTo(2));
            Assert.That(response.Schema.Fields[0].Name, Is.EqualTo("customer_id"));
            // A simple Arrow type encodes as a quoted string, a parameterized one as an
            // object - which is why DataType stays raw JSON.
            Assert.That(response.Schema.Fields[0].DataType.GetString(), Is.EqualTo("Utf8"));
            Assert.That(response.Schema.Fields[1].DataType.ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(response.Schema.Fields[1].Nullable, Is.True);
        });
    }

    [Test]
    public void Test_Response_EmptySchema_YieldsEmptyFieldsNotNull()
    {
        // The runtime serializes schema as {} when the generated query returned no rows, so
        // decoding must not depend on a fields key being present.
        var response = Deserialize("""{"row_count":0,"schema":{},"data":[],"sql":"SELECT 1 WHERE false"}""");

        Assert.Multiple(() =>
        {
            Assert.That(response.RowCount, Is.EqualTo(0));
            Assert.That(response.Schema.Fields, Is.Not.Null.And.Empty);
            Assert.That(response.SQL, Is.EqualTo("SELECT 1 WHERE false"));
        });
    }

    // ==================== HTTP request/response wiring ====================

    private sealed record CapturedRequest(string HttpMethod, string Path, string? Accept, string Body);

    /// <summary>
    /// Runs <paramref name="action"/> against a loopback listener and returns the single
    /// request it received. The listener answers with <paramref name="responseBody"/> at
    /// <paramref name="statusCode"/>.
    /// </summary>
    private static async Task<CapturedRequest> CaptureAsync(
        Func<SpiceClient, Task> action, HttpStatusCode statusCode = HttpStatusCode.OK, string responseBody = "")
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
                context.Request.Headers["Accept"],
                body);

            context.Response.StatusCode = (int)statusCode;
            var payload = Encoding.UTF8.GetBytes(responseBody);
            context.Response.ContentLength64 = payload.Length;
            await context.Response.OutputStream.WriteAsync(payload).ConfigureAwait(false);
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

    private static readonly string EmptyNsqlResponseBody =
        """{"row_count":0,"schema":{},"data":[],"sql":"SELECT 1"}""";

    [Test]
    public async Task Test_NsqlAsync_PostsToNsqlEndpoint_WithJsonEnvelopeAccept()
    {
        var request = await CaptureAsync(
            client => client.NsqlAsync(new NsqlRequest("how many orders")),
            responseBody: EmptyNsqlResponseBody);

        Assert.Multiple(() =>
        {
            Assert.That(request.HttpMethod, Is.EqualTo("POST"));
            Assert.That(request.Path, Is.EqualTo("/v1/nsql"));
            Assert.That(request.Accept, Is.EqualTo("application/vnd.spiceai.nsql.v1+json"));
        });
    }

    [Test]
    public async Task Test_NsqlGenerateSqlAsync_PostsToNsqlEndpoint_WithSqlAccept()
    {
        var request = await CaptureAsync(
            client => client.NsqlGenerateSqlAsync(new NsqlRequest("how many orders")),
            responseBody: "SELECT count(*) FROM orders");

        Assert.Multiple(() =>
        {
            Assert.That(request.HttpMethod, Is.EqualTo("POST"));
            Assert.That(request.Path, Is.EqualTo("/v1/nsql"));
            Assert.That(request.Accept, Is.EqualTo("application/sql"));
        });
    }

    [Test]
    public async Task Test_NsqlGenerateSqlAsync_TrimsRuntimeWhitespace()
    {
        string? sql = null;
        await CaptureAsync(
            async client => sql = await client.NsqlGenerateSqlAsync(new NsqlRequest("how many orders")),
            responseBody: "\n  SELECT count(*) FROM orders\n");

        Assert.That(sql, Is.EqualTo("SELECT count(*) FROM orders"));
    }

    // A missing or ambiguous model is the most common NSQL failure and the runtime explains
    // it in the body. Losing that leaves the caller with a bare status code.
    [Test]
    public void Test_NsqlAsync_ErrorSurfacesResponseBody()
    {
        var ex = Assert.ThrowsAsync<HttpRequestException>(async () => await CaptureAsync(
            client => client.NsqlAsync(new NsqlRequest("how many orders")),
            statusCode: HttpStatusCode.BadRequest,
            responseBody: "No model specified and no compatible LLM model is configured."));

        Assert.Multiple(() =>
        {
            Assert.That(ex!.Message, Does.Contain("No model specified"));
            Assert.That(ex.Message, Does.Contain("400"));
        });
    }

    [Test]
    public void Test_NsqlAsync_NullRequest_Throws()
    {
        using var client = new SpiceClientBuilder().Build();

        Assert.ThrowsAsync<ArgumentNullException>(async () => await client.NsqlAsync(null!));
    }

    [Test]
    public void Test_NsqlAsync_BlankQuery_Throws()
    {
        using var client = new SpiceClientBuilder().Build();

        Assert.ThrowsAsync<ArgumentException>(async () => await client.NsqlAsync(new NsqlRequest("  ")));
    }

    [Test]
    public void Test_NsqlGenerateSqlAsync_NullRequest_Throws()
    {
        using var client = new SpiceClientBuilder().Build();

        Assert.ThrowsAsync<ArgumentNullException>(async () => await client.NsqlGenerateSqlAsync(null!));
    }

    [Test]
    public void Test_NsqlGenerateSqlAsync_BlankQuery_Throws()
    {
        using var client = new SpiceClientBuilder().Build();

        Assert.ThrowsAsync<ArgumentException>(async () => await client.NsqlGenerateSqlAsync(new NsqlRequest("  ")));
    }
}
