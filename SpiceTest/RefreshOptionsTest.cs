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
using Spice.Datasets;

namespace SpiceTest;

/// <summary>
/// Unit tests for <see cref="RefreshOptions"/> and the JSON body it produces.
/// The wire format is shared with the other Spice SDKs: the runtime accepts
/// <c>refresh_sql</c>, <c>refresh_mode</c> and <c>refresh_jitter_max</c> on
/// POST /v1/datasets/{name}/acceleration/refresh.
/// </summary>
public class RefreshOptionsTest
{
    private static Dictionary<string, string> Deserialize(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;

    [Test]
    public void Test_EmptyOptions_SerializeToEmptyObject()
    {
        var json = new RefreshOptions().ToJson();

        Assert.That(json, Is.EqualTo("{}"));
    }

    [Test]
    public void Test_AllOptions_SerializeToRuntimeFieldNames()
    {
        var json = new RefreshOptions()
            .WithRefreshSql("SELECT * FROM taxi_trips WHERE tip_amount > 10.0")
            .WithRefreshMode(RefreshMode.Append)
            .WithMaxJitter(TimeSpan.FromSeconds(10))
            .ToJson();

        var body = Deserialize(json);

        Assert.Multiple(() =>
        {
            Assert.That(body["refresh_sql"], Is.EqualTo("SELECT * FROM taxi_trips WHERE tip_amount > 10.0"));
            Assert.That(body["refresh_mode"], Is.EqualTo("append"));
            Assert.That(body["refresh_jitter_max"], Is.EqualTo("10s"));
            Assert.That(body, Has.Count.EqualTo(3));
        });
    }

    [Test]
    public void Test_UnsetOptions_AreOmitted()
    {
        var json = new RefreshOptions().WithRefreshMode(RefreshMode.Full).ToJson();

        var body = Deserialize(json);

        Assert.Multiple(() =>
        {
            Assert.That(body, Has.Count.EqualTo(1));
            Assert.That(body["refresh_mode"], Is.EqualTo("full"));
            Assert.That(body.ContainsKey("refresh_sql"), Is.False);
            Assert.That(body.ContainsKey("refresh_jitter_max"), Is.False);
        });
    }

    [Test]
    public void Test_ObjectInitializerSyntax_IsEquivalentToBuilder()
    {
        var builder = new RefreshOptions()
            .WithRefreshSql("SELECT 1")
            .WithRefreshMode(RefreshMode.Full)
            .WithMaxJitter(TimeSpan.FromMilliseconds(1500))
            .ToJson();

        var initializer = new RefreshOptions
        {
            RefreshSql = "SELECT 1",
            RefreshMode = RefreshMode.Full,
            MaxJitter = TimeSpan.FromMilliseconds(1500),
        }.ToJson();

        Assert.That(initializer, Is.EqualTo(builder));
    }

    [Test]
    public void Test_RefreshSqlWithQuotes_IsJsonEscaped()
    {
        // Refresh SQL is user-supplied and must not be able to break the JSON body.
        const string sql = "SELECT * FROM t WHERE name = 'O\"Brien' AND note = 'a\\b'";

        var json = new RefreshOptions().WithRefreshSql(sql).ToJson();

        Assert.That(Deserialize(json)["refresh_sql"], Is.EqualTo(sql));
    }

    [TestCase(0, "0s")]
    [TestCase(1000, "1s")]
    [TestCase(10000, "10s")]
    [TestCase(1500, "1500ms")]
    [TestCase(250, "250ms")]
    [TestCase(90000, "90s")]
    public void Test_MaxJitter_FormatsAsRuntimeDuration(int milliseconds, string expected)
    {
        var json = new RefreshOptions().WithMaxJitter(TimeSpan.FromMilliseconds(milliseconds)).ToJson();

        Assert.That(Deserialize(json)["refresh_jitter_max"], Is.EqualTo(expected));
    }

    [Test]
    public void Test_RefreshMode_MapsToLowercaseWireValues()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RefreshOptions.ToWireValue(RefreshMode.Full), Is.EqualTo("full"));
            Assert.That(RefreshOptions.ToWireValue(RefreshMode.Append), Is.EqualTo("append"));
        });
    }

    [Test]
    public void Test_NegativeMaxJitter_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RefreshOptions().WithMaxJitter(TimeSpan.FromSeconds(-1)));
    }

    [TestCase("")]
    [TestCase("   ")]
    public void Test_BlankRefreshSql_Throws(string refreshSql)
    {
        Assert.Throws<ArgumentException>(() => new RefreshOptions().WithRefreshSql(refreshSql));
    }

    [Test]
    public void Test_NullRefreshSql_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new RefreshOptions().WithRefreshSql(null!));
    }

    [Test]
    public void Test_WhitespaceRefreshSqlProperty_IsOmittedRatherThanSentBlank()
    {
        var json = new RefreshOptions { RefreshSql = "  " }.ToJson();

        Assert.That(json, Is.EqualTo("{}"));
    }

    [Test]
    public void Test_NegativeMaxJitterProperty_ThrowsOnSerialize()
    {
        // WithMaxJitter rejects negative values, but the property is also settable
        // directly via object initializer syntax - ToJson must guard that path too.
        var options = new RefreshOptions { MaxJitter = TimeSpan.FromSeconds(-1) };

        Assert.Throws<ArgumentOutOfRangeException>(() => options.ToJson());
    }
}
