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

using System.Text.Json;
using NUnit.Framework;
using Spice.Search;

namespace SpiceTest;

/// <summary>
/// Unit tests for search request serialization and response deserialization.
/// </summary>
/// <remarks>
/// These cover the wire contract of <c>/v1/search</c>, which is where the SDK and the
/// runtime have to agree exactly.
/// </remarks>
[TestFixture]
public class SearchTest
{
    private static readonly string[] ExpectedDatasetOrder = { "a", "b" };

    private static string Serialize(SearchRequest request) => JsonSerializer.Serialize(request);

    private static SearchResponse Deserialize(string json) =>
        JsonSerializer.Deserialize<SearchResponse>(json)!;

    // ==================== Request serialization ====================

    [Test]
    public void Test_Request_TextOnly_OmitsOptionalFields()
    {
        var json = Serialize(new SearchRequest("tickets to Tokyo"));

        Assert.That(json, Is.EqualTo("{\"text\":\"tickets to Tokyo\"}"));
    }

    [Test]
    public void Test_Request_AllOptions_UseWireNames()
    {
        var request = new SearchRequest("tickets to Tokyo")
        {
            Datasets = new[] { "app_messages" },
            Limit = 3,
            Where = "city = 'Tokyo'",
            AdditionalColumns = new[] { "timestamp" },
            Keywords = new[] { "plane", "tickets" },
        };

        using var document = JsonDocument.Parse(Serialize(request));
        var root = document.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("text").GetString(), Is.EqualTo("tickets to Tokyo"));
            Assert.That(root.GetProperty("datasets").GetArrayLength(), Is.EqualTo(1));
            Assert.That(root.GetProperty("limit").GetInt32(), Is.EqualTo(3));
            Assert.That(root.GetProperty("where").GetString(), Is.EqualTo("city = 'Tokyo'"));
            Assert.That(root.GetProperty("additional_columns").GetArrayLength(), Is.EqualTo(1));
            Assert.That(root.GetProperty("keywords").GetArrayLength(), Is.EqualTo(2));
        });
    }

    [Test]
    public void Test_Request_LimitZero_IsPreserved()
    {
        // 0 is a real value the caller supplied, distinct from unset.
        var json = Serialize(new SearchRequest("tickets") { Limit = 0 });

        Assert.That(json, Does.Contain("\"limit\":0"));
    }

    [Test]
    public void Test_Request_EmptyCollections_AreSentNotDropped()
    {
        var json = Serialize(new SearchRequest("tickets") { Datasets = Array.Empty<string>() });

        Assert.That(json, Does.Contain("\"datasets\":[]"));
    }

    // ==================== Response deserialization ====================

    [Test]
    public void Test_Response_DeserializesWireFormat()
    {
        const string body = """
        {
            "results": [
                {
                    "matches": {"message": ["I booked us some tickets"]},
                    "dataset": "app_messages",
                    "primary_key": {"id": "6fd5a215"},
                    "data": {"timestamp": 1724716542},
                    "_score": 0.914321
                }
            ],
            "duration_ms": 42
        }
        """;

        var response = Deserialize(body);

        Assert.Multiple(() =>
        {
            Assert.That(response.DurationMs, Is.EqualTo(42));
            Assert.That(response.Results, Has.Count.EqualTo(1));
            Assert.That(response.Results[0].Dataset, Is.EqualTo("app_messages"));
            Assert.That(response.Results[0].Score, Is.EqualTo(0.914321).Within(1e-9));
            Assert.That(response.Results[0].PrimaryKey["id"].GetString(), Is.EqualTo("6fd5a215"));
            Assert.That(response.Results[0].Data["timestamp"].GetInt64(), Is.EqualTo(1724716542));
            Assert.That(response.Results[0].Matches["message"], Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void Test_Response_ScoreReadsUnderscorePrefixedWireField()
    {
        // The runtime serializes the similarity as `_score`, not `score`.
        var response = Deserialize("""{"results":[{"dataset":"a","_score":0.75}],"duration_ms":1}""");

        Assert.That(response.Results[0].Score, Is.EqualTo(0.75).Within(1e-9));
    }

    [Test]
    public void Test_Response_OmittedObjects_DefaultToEmptyNotNull()
    {
        // The runtime omits data/primary_key/metadata entirely when empty.
        var response = Deserialize("""{"results":[{"dataset":"a","_score":0.5}],"duration_ms":1}""");
        var match = response.Results[0];

        Assert.Multiple(() =>
        {
            Assert.That(match.Data, Is.Not.Null.And.Empty);
            Assert.That(match.PrimaryKey, Is.Not.Null.And.Empty);
            Assert.That(match.Metadata, Is.Not.Null.And.Empty);
            Assert.That(match.Matches, Is.Not.Null.And.Empty);
        });
    }

    [Test]
    public void Test_Response_EmptyResults()
    {
        var response = Deserialize("""{"results":[],"duration_ms":0}""");

        Assert.That(response.Results, Is.Empty);
    }

    [Test]
    public void Test_Response_MissingResultsKey_YieldsEmptyList()
    {
        var response = Deserialize("""{"duration_ms":7}""");

        Assert.Multiple(() =>
        {
            Assert.That(response.Results, Is.Not.Null.And.Empty);
            Assert.That(response.DurationMs, Is.EqualTo(7));
        });
    }

    [Test]
    public void Test_Response_RoundTripsMultipleMatchesInOrder()
    {
        var response = Deserialize("""
        {"results":[{"dataset":"a","_score":0.9},{"dataset":"b","_score":0.8}],"duration_ms":2}
        """);

        Assert.That(response.Results.Select(m => m.Dataset), Is.EqualTo(ExpectedDatasetOrder));
    }
}
