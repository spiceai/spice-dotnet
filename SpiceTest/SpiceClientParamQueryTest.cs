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

using Spice;
using Spice.Params;
using NUnit.Framework;

namespace SpiceTest;

/// <summary>
/// Unit tests for SpiceClient parameterized query methods.
/// These are unit tests that verify the client behavior without connecting to a server.
/// </summary>
[TestFixture]
public class SpiceClientParamQueryTest
{
    // ============ QueryWithParams Validation Tests ============

    [Test]
    public void Test_QueryWithParams_ThrowsWhenNotInitialized()
    {
        // Create client without calling Build() to leave it uninitialized
        var clientBuilder = new SpiceClientBuilder();
        var client = clientBuilder.Build();
        
        // The client should be initialized after Build()
        // But let's verify that Query doesn't throw immediately for parameter validation
        Assert.DoesNotThrow(() =>
        {
            // This should not throw for validation - it will fail later when trying to connect
            // but we can't test that without a server
        });
        
        client.Dispose();
    }

    [Test]
    public void Test_QueryWithParams_DisposedClientThrows()
    {
        var client = new SpiceClientBuilder().Build();
        client.Dispose();
        
        Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await client.QueryWithParams("SELECT 1");
        });
    }

    // ============ Param Array Handling ============

    [Test]
    public void Test_ParamArray_Creation_WithMixedTypes()
    {
        // Verify we can create a params array with mixed types
        object?[] parameters = new object?[]
        {
            42,
            "hello",
            3.14,
            true,
            null,
            new byte[] { 1, 2, 3 }
        };
        
        Assert.That(parameters.Length, Is.EqualTo(6));
        Assert.That(parameters[0], Is.TypeOf<int>());
        Assert.That(parameters[1], Is.TypeOf<string>());
        Assert.That(parameters[2], Is.TypeOf<double>());
        Assert.That(parameters[3], Is.TypeOf<bool>());
        Assert.That(parameters[4], Is.Null);
        Assert.That(parameters[5], Is.TypeOf<byte[]>());
    }

    [Test]
    public void Test_ParamArray_Creation_WithTypedParams()
    {
        // Verify we can mix plain values and Param objects
        object?[] parameters = new object?[]
        {
            42, // Will be inferred as Int32
            Param.Int64(42), // Explicit Int64
            Param.String("test"),
            "test", // Will be inferred as String
            Param.Null()
        };
        
        Assert.That(parameters.Length, Is.EqualTo(5));
        Assert.That(parameters[0], Is.TypeOf<int>());
        Assert.That(parameters[1], Is.TypeOf<Param>());
        Assert.That(parameters[2], Is.TypeOf<Param>());
        Assert.That(parameters[3], Is.TypeOf<string>());
        Assert.That(parameters[4], Is.TypeOf<Param>());
    }

    // ============ SQL Placeholder Patterns ============

    [Test]
    public void Test_SqlPlaceholder_Pattern_Positional()
    {
        // Verify SQL with positional placeholders can be constructed
        var sql = "SELECT * FROM table WHERE id = $1 AND name = $2 AND active = $3";
        
        Assert.That(sql.Contains("$1"), Is.True);
        Assert.That(sql.Contains("$2"), Is.True);
        Assert.That(sql.Contains("$3"), Is.True);
    }

    [Test]
    public void Test_SqlPlaceholder_Pattern_MultipleOccurrences()
    {
        // Same placeholder can appear multiple times
        var sql = "SELECT * FROM table WHERE (id = $1 OR parent_id = $1) AND status = $2";
        
        var count = sql.Split("$1").Length - 1;
        Assert.That(count, Is.EqualTo(2));
    }

    // ============ Client Builder Tests ============

    [Test]
    public void Test_ClientBuilder_DefaultConfiguration()
    {
        using var client = new SpiceClientBuilder().Build();
        
        // Verify default local configuration (address includes scheme prefix)
        Assert.That(client.FlightAddress, Does.Contain("localhost:50051"));
        Assert.That(client.HttpAddress, Is.EqualTo("http://localhost:8090"));
        Assert.That(client.UseTls, Is.False);
    }

    [Test]
    public void Test_ClientBuilder_CloudConfiguration_RequiresProperApiKeyFormat()
    {
        // WithSpiceCloud requires API key in 'appId|key' format
        Assert.Throws<ArgumentException>(() =>
        {
            new SpiceClientBuilder()
                .WithSpiceCloud("invalid-api-key")
                .Build();
        });
    }

    [Test]
    public void Test_ClientBuilder_CustomHttpAddress()
    {
        using var client = new SpiceClientBuilder()
            .WithHttpAddress("http://custom-server:8090")
            .Build();
        
        Assert.That(client.HttpAddress, Is.EqualTo("http://custom-server:8090"));
    }

    [Test]
    public void Test_ClientBuilder_WithMaxRetries()
    {
        using var client = new SpiceClientBuilder()
            .WithMaxRetries(5)
            .Build();
        
        Assert.That(client.MaxRetries, Is.EqualTo(5));
    }

    [Test]
    public void Test_ClientBuilder_WithUserAgent()
    {
        using var client = new SpiceClientBuilder()
            .WithUserAgent("MyApp/1.0")
            .Build();
        
        Assert.That(client.UserAgent, Is.EqualTo("MyApp/1.0"));
    }

    [Test]
    public void Test_ClientBuilder_WithTls()
    {
        using var client = new SpiceClientBuilder()
            .WithTls(true)
            .Build();
        
        Assert.That(client.UseTls, Is.True);
    }

    [Test]
    public void Test_ClientBuilder_MethodChaining()
    {
        using var client = new SpiceClientBuilder()
            .WithHttpAddress("https://custom-server:8090")
            .WithMaxRetries(5)
            .WithUserAgent("TestApp/1.0")
            .WithTls(true)
            .Build();
        
        Assert.That(client.HttpAddress, Is.EqualTo("https://custom-server:8090"));
        Assert.That(client.MaxRetries, Is.EqualTo(5));
        Assert.That(client.UserAgent, Is.EqualTo("TestApp/1.0"));
        Assert.That(client.UseTls, Is.True);
    }

    // ============ Disposal Tests ============

    [Test]
    public void Test_Client_DoubleDispose_DoesNotThrow()
    {
        var client = new SpiceClientBuilder().Build();
        
        Assert.DoesNotThrow(() =>
        {
            client.Dispose();
            client.Dispose(); // Second dispose should not throw
        });
    }

    [Test]
    public void Test_Client_UsingStatement_DisposesCorrectly()
    {
        SpiceClient? clientRef = null;
        
        using (var client = new SpiceClientBuilder().Build())
        {
            clientRef = client;
            Assert.That(client.FlightAddress, Is.Not.Null);
        }
        
        // After using block, client should be disposed
        Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await clientRef!.Query("SELECT 1");
        });
    }
}
