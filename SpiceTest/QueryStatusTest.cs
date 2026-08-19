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
using Spice.Query;

namespace SpiceTest;

/// <summary>
/// Unit tests for <see cref="QueryStatus"/>: terminal classification and the runtime's
/// uppercase wire representation.
/// </summary>
[TestFixture]
public class QueryStatusTest
{
    [TestCase(QueryStatus.Pending, false)]
    [TestCase(QueryStatus.Running, false)]
    [TestCase(QueryStatus.Succeeded, true)]
    [TestCase(QueryStatus.Failed, true)]
    [TestCase(QueryStatus.Cancelled, true)]
    [TestCase(QueryStatus.Closed, true)]
    public void Test_IsTerminal_ClassifiesEveryStatus(QueryStatus status, bool expected)
    {
        Assert.That(status.IsTerminal(), Is.EqualTo(expected));
    }

    [TestCase(QueryStatus.Pending, "\"PENDING\"")]
    [TestCase(QueryStatus.Running, "\"RUNNING\"")]
    [TestCase(QueryStatus.Succeeded, "\"SUCCEEDED\"")]
    [TestCase(QueryStatus.Failed, "\"FAILED\"")]
    [TestCase(QueryStatus.Cancelled, "\"CANCELLED\"")]
    [TestCase(QueryStatus.Closed, "\"CLOSED\"")]
    public void Test_SerializesToRuntimeUppercaseWireValue(QueryStatus status, string expectedJson)
    {
        Assert.That(JsonSerializer.Serialize(status), Is.EqualTo(expectedJson));
    }

    [TestCase("\"PENDING\"", QueryStatus.Pending)]
    [TestCase("\"RUNNING\"", QueryStatus.Running)]
    [TestCase("\"SUCCEEDED\"", QueryStatus.Succeeded)]
    [TestCase("\"FAILED\"", QueryStatus.Failed)]
    [TestCase("\"CANCELLED\"", QueryStatus.Cancelled)]
    [TestCase("\"CLOSED\"", QueryStatus.Closed)]
    public void Test_DeserializesRuntimeUppercaseWireValue(string json, QueryStatus expected)
    {
        Assert.That(JsonSerializer.Deserialize<QueryStatus>(json), Is.EqualTo(expected));
    }

    [Test]
    public void Test_DeserializingUnrecognizedValue_Throws()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<QueryStatus>("\"NOT_A_STATUS\""));
    }
}
