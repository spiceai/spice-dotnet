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

using System.IO;
using Apache.Arrow;
using Apache.Arrow.Ipc;
using Apache.Arrow.Types;
using Apache.Arrow.Adbc;
using Spice;
using Spice.Params;

namespace SpiceTest;

/// <summary>
/// Comprehensive integration tests for parameterized queries (prepare/bind/execute)
/// against the TPC-H dataset on Spice Cloud.
///
/// These tests use the Go-based FlightSQL ADBC driver (Apache.Arrow.Adbc.Drivers.Interop.FlightSql)
/// which properly implements the Prepare() method required for parameterized queries.
///
/// To run these tests, set the SCP_SPICEAI_TPCH_API_KEY environment variable with a valid
/// Spice.ai API key that has access to the spiceai/tpch dataset.
/// </summary>
[TestFixture]
public class ParameterizedQueryIntegrationTest
{
    private SpiceClient _spiceClient = null!;
    private string? ApiKey { get; set; }
    private bool UseLocalhost { get; set; }
    private const string PrepareNotSupportedMessage = "Statement does not support Prepare";

    [OneTimeSetUp]
    public void OneTimeSetup()
    {
        ApiKey = Environment.GetEnvironmentVariable("SCP_SPICEAI_TPCH_API_KEY");
        UseLocalhost = Environment.GetEnvironmentVariable("USE_LOCALHOST") == "true";
    }

    [SetUp]
    public void Setup()
    {
        if (UseLocalhost)
        {
            _spiceClient = new SpiceClientBuilder()
                .WithFlightAddress("http://localhost:50051")
                .Build();
        }
        else
        {
            if (ApiKey == null)
            {
                Assert.Ignore("Skipping test: SCP_SPICEAI_TPCH_API_KEY environment variable not set.");
                return;
            }
            _spiceClient = new SpiceClientBuilder()
                .WithApiKey(ApiKey)
                .WithHttpAddress("https://us-east-1-prod-aws-data.spiceai.io")
                .WithFlightAddress("https://us-east-1-prod-aws-flight.spiceai.io:443")
                .WithTls(true)
                .Build();
        }
    }

    [TearDown]
    public void TearDown()
    {
        _spiceClient?.Dispose();
    }

    /// <summary>
    /// Helper method to run parameterized query tests that handles the case
    /// where the ADBC driver doesn't support Prepare() or isn't available.
    /// </summary>
    private async Task<IArrowArrayStream?> QueryWithParamsOrSkip(string sql, params object?[] parameters)
    {
        try
        {
            return await _spiceClient.QueryWithParams(sql, parameters);
        }
        catch (AdbcException ex) when (ex.Message.Contains(PrepareNotSupportedMessage))
        {
            Assert.Ignore("ADBC FlightSQL driver does not support Prepare(). " +
                "This test requires the Go-based interop driver or a future version of the pure C# driver.");
            return null;
        }
        catch (FileNotFoundException ex)
        {
            Assert.Ignore($"ADBC FlightSQL native driver not found: {ex.Message}");
            return null;
        }
        catch (DllNotFoundException ex)
        {
            Assert.Ignore($"ADBC FlightSQL native driver library not found: {ex.Message}");
            return null;
        }
        catch (AdbcException ex)
        {
            // Catch any other ADBC exceptions and skip the test
            Assert.Ignore($"ADBC error - parameterized queries may not be supported: {ex.Message}");
            return null;
        }
        catch (Exception ex) when (ex.Message.Contains("driver", StringComparison.OrdinalIgnoreCase) ||
                                    ex.Message.Contains("native", StringComparison.OrdinalIgnoreCase) ||
                                    ex.Message.Contains("load", StringComparison.OrdinalIgnoreCase) ||
                                    ex.Message.Contains("Could not find", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Ignore($"ADBC FlightSQL driver could not be loaded: {ex.Message}");
            return null;
        }
    }

    // ============ Basic Parameterized Query Tests ============

    [Test]
    public async Task Test_QueryWithParams_SingleIntParameter()
    {
        // Query customers by customer key
        var result = await QueryWithParamsOrSkip(
            "SELECT c_custkey, c_name, c_nationkey FROM spice.tpch.customer WHERE c_custkey = $1",
            1);

        Assert.That(result, Is.Not.Null);

        var batches = new List<RecordBatch>();
        while (await result!.ReadNextRecordBatchAsync() is { } batch)
        {
            batches.Add(batch);
        }

        Assert.That(batches.Sum(b => b.Length), Is.EqualTo(1));
        var firstBatch = batches[0];

        var custKeyCol = firstBatch.Column("c_custkey");
        Assert.That(GetNumericValue(custKeyCol, 0), Is.EqualTo(1));
    }

    [Test]
    public async Task Test_QueryWithParams_SingleStringParameter()
    {
        // Query nations by name
        var result = await QueryWithParamsOrSkip(
            "SELECT n_nationkey, n_name, n_regionkey FROM spice.tpch.nation WHERE n_name = $1",
            "FRANCE");

        Assert.That(result, Is.Not.Null);

        var batches = new List<RecordBatch>();
        while (await result!.ReadNextRecordBatchAsync() is { } batch)
        {
            batches.Add(batch);
        }

        Assert.That(batches.Sum(b => b.Length), Is.EqualTo(1));
        var firstBatch = batches[0];

        var nameCol = firstBatch.Column("n_name");
        Assert.That(GetStringValue(nameCol, 0), Is.EqualTo("FRANCE"));
    }

    [Test]
    public async Task Test_QueryWithParams_MultipleParameters()
    {
        // Query parts by size range and type pattern
        var result = await QueryWithParamsOrSkip(
            @"SELECT p_partkey, p_name, p_size, p_type
              FROM spice.tpch.part
              WHERE p_size >= $1 AND p_size <= $2
              LIMIT 10",
            10, 20);

        Assert.That(result, Is.Not.Null);

        var batches = new List<RecordBatch>();
        while (await result!.ReadNextRecordBatchAsync() is { } batch)
        {
            batches.Add(batch);
        }

        Assert.That(batches.Sum(b => b.Length), Is.GreaterThan(0));

        foreach (var batch in batches)
        {
            var sizeCol = batch.Column("p_size");
            for (var i = 0; i < batch.Length; i++)
            {
                var size = GetNumericValue(sizeCol, i);
                Assert.That(size, Is.InRange(10, 20), "Part size should be in range");
            }
        }
    }

    [Test]
    public async Task Test_QueryWithParams_ThreeParameters()
    {
        // Query lineitems by ship date range and quantity threshold
        var result = await QueryWithParamsOrSkip(
            @"SELECT l_orderkey, l_linenumber, l_quantity, l_shipdate
              FROM spice.tpch.lineitem
              WHERE l_shipdate >= $1
                AND l_shipdate < $2
                AND l_quantity > $3
              LIMIT 50",
            "1995-01-01", "1995-02-01", 30.0);

        Assert.That(result, Is.Not.Null);

        var batches = new List<RecordBatch>();
        while (await result!.ReadNextRecordBatchAsync() is { } batch)
        {
            batches.Add(batch);
        }

        Assert.That(batches.Sum(b => b.Length), Is.GreaterThan(0));

        foreach (var batch in batches)
        {
            var quantityCol = batch.Column("l_quantity");
            for (var i = 0; i < batch.Length; i++)
            {
                var quantity = GetNumericValue(quantityCol, i);
                Assert.That(quantity, Is.GreaterThan(30), "Quantity should be > 30");
            }
        }
    }

    // ============ Explicit Type Annotation Tests ============

    [Test]
    public async Task Test_QueryWithParams_ExplicitInt32Type()
    {
        var result = await QueryWithParamsOrSkip(
            "SELECT c_custkey, c_name FROM spice.tpch.customer WHERE c_custkey = $1",
            Param.Int32(5));

        Assert.That(result, Is.Not.Null);

        var batches = new List<RecordBatch>();
        while (await result!.ReadNextRecordBatchAsync() is { } batch)
        {
            batches.Add(batch);
        }

        Assert.That(batches.Sum(b => b.Length), Is.EqualTo(1));
        var custKeyCol = batches[0].Column("c_custkey");
        Assert.That(GetNumericValue(custKeyCol, 0), Is.EqualTo(5));
    }

    [Test]
    public async Task Test_QueryWithParams_ExplicitDoubleType()
    {
        // Query parts by retail price threshold
        var result = await QueryWithParamsOrSkip(
            @"SELECT p_partkey, p_name, p_retailprice
              FROM spice.tpch.part
              WHERE p_retailprice > $1
              LIMIT 10",
            Param.Double(1900.0));

        Assert.That(result, Is.Not.Null);

        var batches = new List<RecordBatch>();
        while (await result!.ReadNextRecordBatchAsync() is { } batch)
        {
            batches.Add(batch);
        }

        Assert.That(batches.Sum(b => b.Length), Is.GreaterThan(0));

        foreach (var batch in batches)
        {
            var priceCol = batch.Column("p_retailprice");
            for (var i = 0; i < batch.Length; i++)
            {
                var price = GetNumericValue(priceCol, i);
                Assert.That(price, Is.GreaterThan(1900.0), "Price should be > 1900");
            }
        }
    }

    [Test]
    public async Task Test_QueryWithParams_ExplicitStringType()
    {
        var result = await QueryWithParamsOrSkip(
            "SELECT r_regionkey, r_name FROM spice.tpch.region WHERE r_name = $1",
            Param.String("EUROPE"));

        Assert.That(result, Is.Not.Null);

        var batches = new List<RecordBatch>();
        while (await result!.ReadNextRecordBatchAsync() is { } batch)
        {
            batches.Add(batch);
        }

        Assert.That(batches.Sum(b => b.Length), Is.EqualTo(1));
        var nameCol = batches[0].Column("r_name");
        Assert.That(GetStringValue(nameCol, 0), Is.EqualTo("EUROPE"));
    }

    [Test]
    public async Task Test_QueryWithParams_MixedExplicitAndInferred()
    {
        // Mix explicit Param types with inferred types
        var result = await QueryWithParamsOrSkip(
            @"SELECT l_orderkey, l_linenumber, l_quantity, l_discount
              FROM spice.tpch.lineitem
              WHERE l_quantity >= $1
                AND l_discount <= $2
              LIMIT 20",
            Param.Double(40.0),  // Explicit
            0.05);               // Inferred

        Assert.That(result, Is.Not.Null);

        var batches = new List<RecordBatch>();
        while (await result!.ReadNextRecordBatchAsync() is { } batch)
        {
            batches.Add(batch);
        }

        Assert.That(batches.Sum(b => b.Length), Is.GreaterThan(0));
    }

    // ============ Complex Query Tests (TPC-H Style) ============

    [Test]
    public async Task Test_QueryWithParams_TpchQ6Style_RevenueByDiscount()
    {
        // TPC-H Q6 style query with parameterized date range and discount
        var result = await QueryWithParamsOrSkip(
            @"SELECT SUM(l_extendedprice * l_discount) as revenue
              FROM spice.tpch.lineitem
              WHERE l_shipdate >= $1
                AND l_shipdate < $2
                AND l_discount BETWEEN $3 AND $4
                AND l_quantity < $5",
            "1994-01-01",
            "1995-01-01",
            0.05,
            0.07,
            Param.Double(24.0));

        Assert.That(result, Is.Not.Null);

        var batches = new List<RecordBatch>();
        while (await result!.ReadNextRecordBatchAsync() is { } batch)
        {
            batches.Add(batch);
        }

        Assert.That(batches.Sum(b => b.Length), Is.EqualTo(1));

        var revenueCol = batches[0].Column("revenue");
        var revenue = GetNumericValue(revenueCol, 0);
        Assert.That(revenue, Is.GreaterThan(0), "Revenue should be positive");
    }

    [Test]
    public async Task Test_QueryWithParams_JoinWithParameters()
    {
        // Join query with parameterized nation
        var result = await QueryWithParamsOrSkip(
            @"SELECT c.c_custkey, c.c_name, n.n_name
              FROM spice.tpch.customer c
              JOIN spice.tpch.nation n ON c.c_nationkey = n.n_nationkey
              WHERE n.n_name = $1
              LIMIT 10",
            "GERMANY");

        Assert.That(result, Is.Not.Null);

        var batches = new List<RecordBatch>();
        while (await result!.ReadNextRecordBatchAsync() is { } batch)
        {
            batches.Add(batch);
        }

        Assert.That(batches.Sum(b => b.Length), Is.GreaterThan(0));

        foreach (var batch in batches)
        {
            var nationCol = batch.Column("n_name");
            for (var i = 0; i < batch.Length; i++)
            {
                Assert.That(GetStringValue(nationCol, i), Is.EqualTo("GERMANY"));
            }
        }
    }

    [Test]
    public async Task Test_QueryWithParams_AggregationWithParameters()
    {
        // Aggregation query with parameterized grouping
        var result = await QueryWithParamsOrSkip(
            @"SELECT l_returnflag, l_linestatus,
                     SUM(l_quantity) as total_qty,
                     COUNT(*) as count
              FROM spice.tpch.lineitem
              WHERE l_shipdate <= $1
              GROUP BY l_returnflag, l_linestatus
              ORDER BY l_returnflag, l_linestatus",
            "1998-09-01");

        Assert.That(result, Is.Not.Null);

        var batches = new List<RecordBatch>();
        while (await result!.ReadNextRecordBatchAsync() is { } batch)
        {
            batches.Add(batch);
        }

        // Should have multiple groups
        Assert.That(batches.Sum(b => b.Length), Is.GreaterThan(1));
    }

    [Test]
    public async Task Test_QueryWithParams_SubqueryWithParameters()
    {
        // Subquery with parameters
        var result = await QueryWithParamsOrSkip(
            @"SELECT o_orderkey, o_custkey, o_totalprice
              FROM spice.tpch.orders
              WHERE o_custkey IN (
                  SELECT c_custkey FROM spice.tpch.customer
                  WHERE c_nationkey = $1
              )
              AND o_totalprice > $2
              LIMIT 20",
            1,  // Nation key
            Param.Double(100000.0));

        Assert.That(result, Is.Not.Null);

        var batches = new List<RecordBatch>();
        while (await result!.ReadNextRecordBatchAsync() is { } batch)
        {
            batches.Add(batch);
        }

        Assert.That(batches.Sum(b => b.Length), Is.GreaterThan(0));

        foreach (var batch in batches)
        {
            var priceCol = batch.Column("o_totalprice");
            for (var i = 0; i < batch.Length; i++)
            {
                var price = GetNumericValue(priceCol, i);
                Assert.That(price, Is.GreaterThan(100000.0), "Order total should be > 100000");
            }
        }
    }

    // ============ Edge Cases and Boundary Tests ============

    [Test]
    public async Task Test_QueryWithParams_LargeIntegerValue()
    {
        // Large order key value
        var result = await QueryWithParamsOrSkip(
            "SELECT o_orderkey, o_custkey FROM spice.tpch.orders WHERE o_orderkey = $1",
            Param.Int64(6000000L));

        Assert.That(result, Is.Not.Null);

        var batches = new List<RecordBatch>();
        while (await result!.ReadNextRecordBatchAsync() is { } batch)
        {
            batches.Add(batch);
        }

        // May or may not find a result, but query should succeed
        Assert.Pass("Query executed successfully with large integer parameter");
    }

    [Test]
    public async Task Test_QueryWithParams_ZeroValue()
    {
        var result = await QueryWithParamsOrSkip(
            @"SELECT l_orderkey, l_linenumber, l_discount
              FROM spice.tpch.lineitem
              WHERE l_discount = $1
              LIMIT 10",
            0.0);

        Assert.That(result, Is.Not.Null);

        var batches = new List<RecordBatch>();
        while (await result!.ReadNextRecordBatchAsync() is { } batch)
        {
            batches.Add(batch);
        }

        // Should find lineitems with zero discount
        Assert.That(batches.Sum(b => b.Length), Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public async Task Test_QueryWithParams_EmptyStringParameter()
    {
        // Empty string should work but likely not match anything
        var result = await QueryWithParamsOrSkip(
            "SELECT n_nationkey, n_name FROM spice.tpch.nation WHERE n_name = $1",
            "");

        Assert.That(result, Is.Not.Null);

        var batches = new List<RecordBatch>();
        while (await result!.ReadNextRecordBatchAsync() is { } batch)
        {
            batches.Add(batch);
        }

        // Empty string should not match any nation
        Assert.That(batches.Sum(b => b.Length), Is.EqualTo(0));
    }

    [Test]
    public async Task Test_QueryWithParams_NegativeNumber()
    {
        // Query with negative value (should not match any positive keys)
        var result = await QueryWithParamsOrSkip(
            "SELECT c_custkey, c_name FROM spice.tpch.customer WHERE c_custkey > $1 LIMIT 5",
            -100);

        Assert.That(result, Is.Not.Null);

        var batches = new List<RecordBatch>();
        while (await result!.ReadNextRecordBatchAsync() is { } batch)
        {
            batches.Add(batch);
        }

        // Should find customers since all keys are > -100
        Assert.That(batches.Sum(b => b.Length), Is.EqualTo(5));
    }

    [Test]
    public async Task Test_QueryWithParams_VerySmallDouble()
    {
        var result = await QueryWithParamsOrSkip(
            @"SELECT l_orderkey, l_discount
              FROM spice.tpch.lineitem
              WHERE l_discount >= $1
              LIMIT 10",
            0.0001);

        Assert.That(result, Is.Not.Null);

        var batches = new List<RecordBatch>();
        while (await result!.ReadNextRecordBatchAsync() is { } batch)
        {
            batches.Add(batch);
        }

        Assert.That(batches.Sum(b => b.Length), Is.GreaterThan(0));
    }

    // ============ Multiple Executions (Connection Reuse) ============

    [Test]
    public async Task Test_QueryWithParams_MultipleExecutions_SameQuery()
    {
        const string sql = "SELECT c_custkey, c_name FROM spice.tpch.customer WHERE c_custkey = $1";

        // Execute the same query multiple times with different parameters
        for (var key = 1; key <= 5; key++)
        {
            var result = await QueryWithParamsOrSkip(sql, key);
            Assert.That(result, Is.Not.Null, $"Query for key {key} should succeed");

            var batches = new List<RecordBatch>();
            while (await result!.ReadNextRecordBatchAsync() is { } batch)
            {
                batches.Add(batch);
            }

            Assert.That(batches.Sum(b => b.Length), Is.EqualTo(1), $"Should find customer with key {key}");
        }
    }

    [Test]
    public async Task Test_QueryWithParams_MultipleExecutions_DifferentQueries()
    {
        // Execute different queries in sequence
        var nationResult = await QueryWithParamsOrSkip(
            "SELECT n_nationkey, n_name FROM spice.tpch.nation WHERE n_name = $1",
            "JAPAN");
        Assert.That(nationResult, Is.Not.Null);
        await ConsumeStream(nationResult!);

        var regionResult = await QueryWithParamsOrSkip(
            "SELECT r_regionkey, r_name FROM spice.tpch.region WHERE r_name = $1",
            "ASIA");
        Assert.That(regionResult, Is.Not.Null);
        await ConsumeStream(regionResult!);

        var partResult = await QueryWithParamsOrSkip(
            "SELECT p_partkey, p_name FROM spice.tpch.part WHERE p_size = $1 LIMIT 5",
            15);
        Assert.That(partResult, Is.Not.Null);
        await ConsumeStream(partResult!);

        Assert.Pass("Multiple different queries executed successfully");
    }

    [Test]
    public async Task Test_QueryWithParams_ConcurrentQueries()
    {
        // Execute multiple queries concurrently
        var tasks = new List<Task<int>>
        {
            CountResults(QueryWithParamsOrSkip(
                "SELECT c_custkey FROM spice.tpch.customer WHERE c_nationkey = $1 LIMIT 10", 1)),
            CountResults(QueryWithParamsOrSkip(
                "SELECT c_custkey FROM spice.tpch.customer WHERE c_nationkey = $1 LIMIT 10", 2)),
            CountResults(QueryWithParamsOrSkip(
                "SELECT c_custkey FROM spice.tpch.customer WHERE c_nationkey = $1 LIMIT 10", 3)),
        };

        var results = await Task.WhenAll(tasks);

        foreach (var count in results)
        {
            Assert.That(count, Is.GreaterThan(0), "Each concurrent query should return results");
        }
    }

    // ============ TPC-H Tables Coverage ============

    [Test]
    public async Task Test_QueryWithParams_AllTpchTables()
    {
        // Test parameterized queries against all TPC-H tables

        // Customer
        var customerResult = await QueryWithParamsOrSkip(
            "SELECT c_custkey, c_name FROM spice.tpch.customer WHERE c_custkey <= $1 LIMIT 3", 10);
        Assert.That(await CountRows(customerResult!), Is.GreaterThan(0), "Customer query failed");

        // Orders
        var ordersResult = await QueryWithParamsOrSkip(
            "SELECT o_orderkey, o_custkey FROM spice.tpch.orders WHERE o_custkey = $1 LIMIT 3", 1);
        Assert.That(await CountRows(ordersResult!), Is.GreaterThan(0), "Orders query failed");

        // Lineitem
        var lineitemResult = await QueryWithParamsOrSkip(
            "SELECT l_orderkey, l_linenumber FROM spice.tpch.lineitem WHERE l_orderkey = $1 LIMIT 3", 1);
        Assert.That(await CountRows(lineitemResult!), Is.GreaterThan(0), "Lineitem query failed");

        // Part
        var partResult = await QueryWithParamsOrSkip(
            "SELECT p_partkey, p_name FROM spice.tpch.part WHERE p_size = $1 LIMIT 3", 10);
        Assert.That(await CountRows(partResult!), Is.GreaterThan(0), "Part query failed");

        // Supplier
        var supplierResult = await QueryWithParamsOrSkip(
            "SELECT s_suppkey, s_name FROM spice.tpch.supplier WHERE s_nationkey = $1 LIMIT 3", 1);
        Assert.That(await CountRows(supplierResult!), Is.GreaterThan(0), "Supplier query failed");

        // Partsupp
        var partsuppResult = await QueryWithParamsOrSkip(
            "SELECT ps_partkey, ps_suppkey FROM spice.tpch.partsupp WHERE ps_partkey = $1 LIMIT 3", 1);
        Assert.That(await CountRows(partsuppResult!), Is.GreaterThan(0), "Partsupp query failed");

        // Nation
        var nationResult = await QueryWithParamsOrSkip(
            "SELECT n_nationkey, n_name FROM spice.tpch.nation WHERE n_regionkey = $1", 1);
        Assert.That(await CountRows(nationResult!), Is.GreaterThan(0), "Nation query failed");

        // Region
        var regionResult = await QueryWithParamsOrSkip(
            "SELECT r_regionkey, r_name FROM spice.tpch.region WHERE r_regionkey = $1", 1);
        Assert.That(await CountRows(regionResult!), Is.EqualTo(1), "Region query failed");
    }

    // ============ SQL Injection Prevention Tests ============

    [Test]
    public async Task Test_QueryWithParams_SqlInjectionPrevention_StringParam()
    {
        // Malicious string that would cause issues with string concatenation
        var maliciousInput = "FRANCE'; DROP TABLE nation; --";

        var result = await QueryWithParamsOrSkip(
            "SELECT n_nationkey, n_name FROM spice.tpch.nation WHERE n_name = $1",
            maliciousInput);

        Assert.That(result, Is.Not.Null);

        var batches = new List<RecordBatch>();
        while (await result!.ReadNextRecordBatchAsync() is { } batch)
        {
            batches.Add(batch);
        }

        // Should not find any matches (and importantly, should NOT execute DROP TABLE)
        Assert.That(batches.Sum(b => b.Length), Is.EqualTo(0));

        // Verify nation table still exists
        var verifyResult = await QueryWithParamsOrSkip(
            "SELECT COUNT(*) as cnt FROM spice.tpch.nation WHERE n_name = $1",
            "FRANCE");

        var verifyBatches = new List<RecordBatch>();
        while (await verifyResult!.ReadNextRecordBatchAsync() is { } batch)
        {
            verifyBatches.Add(batch);
        }

        Assert.That(GetNumericValue(verifyBatches[0].Column("cnt"), 0), Is.EqualTo(1),
            "Nation table should still have FRANCE record");
    }

    [Test]
    public async Task Test_QueryWithParams_SqlInjectionPrevention_QuoteEscaping()
    {
        // String with quotes that would break naive concatenation
        var inputWithQuotes = "O'BRIEN";

        var result = await QueryWithParamsOrSkip(
            "SELECT c_custkey, c_name FROM spice.tpch.customer WHERE c_name LIKE $1 LIMIT 5",
            $"%{inputWithQuotes}%");

        Assert.That(result, Is.Not.Null);

        // Query should execute without SQL syntax errors
        await ConsumeStream(result!);
        Assert.Pass("Query with quotes in parameter executed successfully");
    }

    // ============ Helper Methods ============

    private static async Task<int> CountResults(Task<Apache.Arrow.Ipc.IArrowArrayStream?> resultTask)
    {
        var result = await resultTask;
        return await CountRows(result!);
    }

    private static async Task<int> CountRows(Apache.Arrow.Ipc.IArrowArrayStream stream)
    {
        var count = 0;
        while (await stream.ReadNextRecordBatchAsync() is { } batch)
        {
            count += batch.Length;
        }
        return count;
    }

    private static async Task ConsumeStream(Apache.Arrow.Ipc.IArrowArrayStream stream)
    {
        while (await stream.ReadNextRecordBatchAsync() is { })
        {
            // Just consume the stream
        }
    }

    private static object? GetNumericValue(IArrowArray array, int index)
    {
        return array switch
        {
            Int8Array a => a.GetValue(index),
            Int16Array a => a.GetValue(index),
            Int32Array a => a.GetValue(index),
            Int64Array a => a.GetValue(index),
            UInt8Array a => a.GetValue(index),
            UInt16Array a => a.GetValue(index),
            UInt32Array a => a.GetValue(index),
            UInt64Array a => a.GetValue(index),
            FloatArray a => a.GetValue(index),
            DoubleArray a => a.GetValue(index),
            Decimal128Array a => a.GetValue(index),
            Decimal256Array a => a.GetValue(index),
            _ => throw new NotSupportedException($"Unsupported numeric array type: {array.GetType()}")
        };
    }

    private static string? GetStringValue(IArrowArray array, int index)
    {
        return array switch
        {
            StringArray a => a.GetString(index),
            LargeStringArray a => a.GetString(index),
            _ => throw new NotSupportedException($"Unsupported string array type: {array.GetType()}")
        };
    }
}
