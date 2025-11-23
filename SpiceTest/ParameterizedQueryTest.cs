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

namespace SpiceTest;

public class ParameterizedQueryTest
{
    private SpiceClient _spiceClient;
    private string? ApiKey { get; set; }

    [OneTimeSetUp]
    public void OneTimeSetup()
    {
        ApiKey = Environment.GetEnvironmentVariable("API_KEY");
    }

    [SetUp]
    public void Setup()
    {
        if (ApiKey == null)
        {
            throw new Exception("No API_KEY provided");
        }
        _spiceClient = new SpiceClientBuilder()
            .WithApiKey(ApiKey)
            .WithSpiceCloud()
            .Build();
    }

    [TearDown]
    public void TearDown()
    {
        _spiceClient?.Dispose();
    }

    [Test]
    public async Task TestParameterizedQueryWithStringParameter()
    {
        var parameters = new Dictionary<string, object>
        {
            { "nation_name", "CHINA" }
        };

        var result = await _spiceClient.Query(
            "SELECT n_nationkey, n_name, n_regionkey FROM tpch.nation WHERE n_name = :nation_name",
            parameters);

        var enumerator = result.GetAsyncEnumerator();
        var totalRows = 0;
        while (await enumerator.MoveNextAsync())
        {
            var batch = enumerator.Current;
            totalRows += batch.Length;
            Assert.That(batch.ColumnCount, Is.EqualTo(3));
            
            // Validate actual returned values
            if (batch.Length > 0)
            {
                var nationKeyCol = batch.Column(0);
                var nameCol = batch.Column(1) as Apache.Arrow.StringArray;
                var regionKeyCol = batch.Column(2);
                
                Assert.That(nameCol, Is.Not.Null, "n_name should be string");
                
                for (int i = 0; i < batch.Length; i++)
                {
                    // Validate the name is exactly "CHINA"
                    var nationName = nameCol.GetString(i);
                    Assert.That(nationName, Is.EqualTo("CHINA"), "All returned nations should be CHINA");
                    
                    // Validate nation key is 18 for CHINA in standard TPC-H data
                    var nationKey = GetNumericValue(nationKeyCol, i);
                    Assert.That(nationKey, Is.EqualTo(18), "CHINA should have nation key 18");
                    
                    // Validate region key is 2 (ASIA) for CHINA
                    var regionKey = GetNumericValue(regionKeyCol, i);
                    Assert.That(regionKey, Is.EqualTo(2), "CHINA should be in ASIA region (key 2)");
                }
            }
        }
        Assert.That(totalRows, Is.EqualTo(1), "Should return exactly one row for CHINA");
    }

    [Test]
    public async Task TestParameterizedQueryWithIntParameter()
    {
        var parameters = new Dictionary<string, object>
        {
            { "region_key", 2 }
        };

        var result = await _spiceClient.Query(
            "SELECT n_name FROM tpch.nation WHERE n_regionkey = :region_key ORDER BY n_name",
            parameters);

        var enumerator = result.GetAsyncEnumerator();
        var totalRows = 0;
        var asianNations = new HashSet<string> { "INDIA", "INDONESIA", "JAPAN", "CHINA", "VIETNAM" };
        var seenNations = new HashSet<string>();
        
        while (await enumerator.MoveNextAsync())
        {
            var batch = enumerator.Current;
            totalRows += batch.Length;
            Assert.That(batch.ColumnCount, Is.EqualTo(1));
            
            // Validate actual returned values
            if (batch.Length > 0)
            {
                var nameCol = batch.Column(0) as Apache.Arrow.StringArray;
                Assert.That(nameCol, Is.Not.Null, "n_name should be string");
                
                for (int i = 0; i < batch.Length; i++)
                {
                    var nationName = nameCol.GetString(i);
                    Assert.That(asianNations, Does.Contain(nationName), 
                        $"Nation {nationName} should be one of the 5 Asian nations (region 2)");
                    seenNations.Add(nationName);
                }
            }
        }
        Assert.That(totalRows, Is.EqualTo(5), "Should return exactly 5 nations for ASIA region");
        Assert.That(seenNations.Count, Is.EqualTo(5), "Should see all 5 unique Asian nations");
    }

    [Test]
    public async Task TestParameterizedQueryWithMultipleParameters()
    {
        var parameters = new Dictionary<string, object>
        {
            { "min_key", 0 },
            { "max_key", 10 }
        };

        var result = await _spiceClient.Query(
            "SELECT n_nationkey, n_name FROM tpch.nation WHERE n_nationkey >= :min_key AND n_nationkey < :max_key ORDER BY n_nationkey",
            parameters);

        var enumerator = result.GetAsyncEnumerator();
        var totalRows = 0;
        var expectedNations = new HashSet<string> 
        { 
            "ALGERIA", "ARGENTINA", "BRAZIL", "CANADA", "EGYPT", 
            "ETHIOPIA", "FRANCE", "GERMANY", "INDIA", "INDONESIA" 
        };
        var seenNations = new HashSet<string>();
        int? prevNationKey = null;
        
        while (await enumerator.MoveNextAsync())
        {
            var batch = enumerator.Current;
            totalRows += batch.Length;
            Assert.That(batch.ColumnCount, Is.EqualTo(2));
            
            // Validate actual returned values
            if (batch.Length > 0)
            {
                var nationKeyCol = batch.Column(0);
                var nameCol = batch.Column(1) as Apache.Arrow.StringArray;
                
                Assert.That(nameCol, Is.Not.Null, "n_name should be string");
                
                for (int i = 0; i < batch.Length; i++)
                {
                    // Validate nation key is in range [0, 10)
                    var nationKey = (int)GetNumericValue(nationKeyCol, i);
                    Assert.That(nationKey, Is.GreaterThanOrEqualTo(0), "Nation key should be >= 0");
                    Assert.That(nationKey, Is.LessThan(10), "Nation key should be < 10");
                    
                    // Validate ordering (ascending by nation key)
                    if (prevNationKey.HasValue)
                    {
                        Assert.That(nationKey, Is.GreaterThan(prevNationKey.Value), 
                            "Nation keys should be ordered ascending");
                    }
                    prevNationKey = nationKey;
                    
                    // Validate nation name is expected
                    var nationName = nameCol.GetString(i);
                    Assert.That(expectedNations, Does.Contain(nationName), 
                        $"Nation {nationName} should be one of the first 10 nations");
                    seenNations.Add(nationName);
                }
            }
        }
        Assert.That(totalRows, Is.EqualTo(10), "Should return exactly 10 nations");
        Assert.That(seenNations.Count, Is.EqualTo(10), "Should see 10 unique nations");
    }

    [Test]
    public void TestParameterizedQueryThrowsOnNullParameters()
    {
        Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await _spiceClient.Query("SELECT * FROM tpch.nation WHERE n_name = :name", null!));
    }

    [Test]
    public void TestParameterizedQueryThrowsOnEmptySQL()
    {
        var parameters = new Dictionary<string, object> { { "test", "value" } };
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await _spiceClient.Query("", parameters));
    }

    private static double GetNumericValue(Apache.Arrow.IArrowArray column, int index)
    {
        return column switch
        {
            Apache.Arrow.Int32Array int32Array => int32Array.GetValue(index) ?? 0,
            Apache.Arrow.Int64Array int64Array => int64Array.GetValue(index) ?? 0,
            Apache.Arrow.DoubleArray doubleArray => doubleArray.GetValue(index) ?? 0,
            Apache.Arrow.FloatArray floatArray => floatArray.GetValue(index) ?? 0,
            Apache.Arrow.Decimal128Array decimalArray => (double)(decimalArray.GetValue(index) ?? 0),
            Apache.Arrow.Decimal256Array decimal256Array => (double)(decimal256Array.GetValue(index) ?? 0),
            _ => throw new InvalidOperationException($"Unsupported numeric column type: {column.GetType().Name}")
        };
    }
}
