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

public class FlightQueryTest
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
    public async Task TestTpchQ1()
    {
        var result = await _spiceClient.Query(
            """
            SELECT
                l_returnflag,
                l_linestatus,
                sum(l_quantity) as sum_qty,
                sum(l_extendedprice) as sum_base_price,
                sum(l_extendedprice * (1 - l_discount)) as sum_disc_price,
                sum(l_extendedprice * (1 - l_discount) * (1 + l_tax)) as sum_charge,
                avg(l_quantity) as avg_qty,
                avg(l_extendedprice) as avg_price,
                avg(l_discount) as avg_disc,
                count(*) as count_order
            FROM
                tpch.lineitem
            WHERE
                l_shipdate <= date '1998-12-01' - interval '90' day
            GROUP BY
                l_returnflag,
                l_linestatus
            ORDER BY
                l_returnflag,
                l_linestatus
            """);

        var enumerator = result.GetAsyncEnumerator();
        var totalRows = 0;
        var hasData = false;
        var validReturnFlags = new HashSet<string> { "A", "N", "R" };
        var validLineStatuses = new HashSet<string> { "F", "O" };
        
        while (await enumerator.MoveNextAsync())
        {
            var batch = enumerator.Current;
            totalRows += batch.Length;
            Assert.That(batch.ColumnCount, Is.EqualTo(10));
            
            // Validate column types and actual data values
            if (batch.Length > 0)
            {
                hasData = true;
                var returnFlagCol = batch.Column(0) as Apache.Arrow.StringArray;
                var lineStatusCol = batch.Column(1) as Apache.Arrow.StringArray;
                var sumQtyCol = batch.Column(2);
                var sumBasePriceCol = batch.Column(3);
                var countOrderCol = batch.Column(9);
                
                Assert.That(returnFlagCol, Is.Not.Null, "l_returnflag should be string");
                Assert.That(lineStatusCol, Is.Not.Null, "l_linestatus should be string");
                
                for (int i = 0; i < batch.Length; i++)
                {
                    // Validate return flags are valid values
                    var returnFlag = returnFlagCol.GetString(i);
                    Assert.That(validReturnFlags, Does.Contain(returnFlag), 
                        $"Return flag should be A, N, or R, got {returnFlag}");
                    
                    // Validate line statuses are valid values
                    var lineStatus = lineStatusCol.GetString(i);
                    Assert.That(validLineStatuses, Does.Contain(lineStatus), 
                        $"Line status should be F or O, got {lineStatus}");
                    
                    // Validate aggregated values are positive numbers
                    var sumQty = GetNumericValue(sumQtyCol, i);
                    var sumBasePrice = GetNumericValue(sumBasePriceCol, i);
                    var countOrder = GetNumericValue(countOrderCol, i);
                    
                    Assert.That(sumQty, Is.GreaterThan(0), "sum_qty should be positive");
                    Assert.That(sumBasePrice, Is.GreaterThan(0), "sum_base_price should be positive");
                    Assert.That(countOrder, Is.GreaterThan(0), "count_order should be positive");
                }
            }
        }
        Assert.That(hasData, Is.True, "Query should return at least one row");
        Assert.That(totalRows, Is.EqualTo(4), "TPC-H Q1 should return 4 rows");
    }

    [Test]
    public async Task TestTpchQ2()
    {
        var result = await _spiceClient.Query(
            """
            SELECT
                s.s_acctbal,
                s.s_name,
                n.n_name,
                p.p_partkey,
                p.p_mfgr,
                s.s_address,
                s.s_phone,
                s.s_comment
            FROM
                tpch.part p
                JOIN tpch.partsupp ps ON p.p_partkey = ps.ps_partkey
                JOIN tpch.supplier s ON s.s_suppkey = ps.ps_suppkey
                JOIN tpch.nation n ON s.s_nationkey = n.n_nationkey
                JOIN tpch.region r ON n.n_regionkey = r.r_regionkey
                JOIN (
                    SELECT
                        ps_partkey,
                        min(ps_supplycost) as min_cost
                    FROM
                        tpch.partsupp ps2
                        JOIN tpch.supplier s2 ON s2.s_suppkey = ps2.ps_suppkey
                        JOIN tpch.nation n2 ON s2.s_nationkey = n2.n_nationkey
                        JOIN tpch.region r2 ON n2.n_regionkey = r2.r_regionkey
                    WHERE
                        r2.r_name = 'EUROPE'
                    GROUP BY
                        ps_partkey
                ) min_costs ON p.p_partkey = min_costs.ps_partkey AND ps.ps_supplycost = min_costs.min_cost
            WHERE
                p.p_size = 15
                AND p.p_type like '%BRASS'
                AND r.r_name = 'EUROPE'
            ORDER BY
                s.s_acctbal desc,
                n.n_name,
                s.s_name,
                p.p_partkey
            LIMIT 100
            """);

        var enumerator = result.GetAsyncEnumerator();
        var totalRows = 0;
        double? prevAcctBal = null;
        
        while (await enumerator.MoveNextAsync())
        {
            var batch = enumerator.Current;
            totalRows += batch.Length;
            Assert.That(batch.ColumnCount, Is.EqualTo(8));
            
            // Validate actual data values
            if (batch.Length > 0)
            {
                var acctBalCol = batch.Column(0);
                var nameCol = batch.Column(1) as Apache.Arrow.StringArray;
                
                Assert.That(nameCol, Is.Not.Null, "s_name should be string");
                
                for (int i = 0; i < batch.Length; i++)
                {
                    // Validate account balance exists and ordering (descending)
                    var acctBal = GetNumericValue(acctBalCol, i);
                    if (prevAcctBal.HasValue)
                    {
                        Assert.That(acctBal, Is.LessThanOrEqualTo(prevAcctBal.Value), 
                            "Account balances should be ordered descending");
                    }
                    prevAcctBal = acctBal;
                    
                    // Validate supplier name is not empty
                    var supplierName = nameCol.GetString(i);
                    Assert.That(supplierName, Is.Not.Null.And.Not.Empty, "Supplier name should not be empty");
                }
            }
        }
        Assert.That(totalRows, Is.LessThanOrEqualTo(100), "Q2 should return at most 100 rows");
        Assert.That(totalRows, Is.GreaterThan(0), "Q2 should return at least one row");
    }

    [Test]
    public async Task TestTpchQ3()
    {
        var result = await _spiceClient.Query(
            """
            SELECT
                l_orderkey,
                sum(l_extendedprice * (1 - l_discount)) as revenue,
                o_orderdate,
                o_shippriority
            FROM
                tpch.customer,
                tpch.orders,
                tpch.lineitem
            WHERE
                c_mktsegment = 'BUILDING'
                AND c_custkey = o_custkey
                AND l_orderkey = o_orderkey
                AND o_orderdate < date '1995-03-15'
                AND l_shipdate > date '1995-03-15'
            GROUP BY
                l_orderkey,
                o_orderdate,
                o_shippriority
            ORDER BY
                revenue desc,
                o_orderdate
            LIMIT 10
            """);

        var enumerator = result.GetAsyncEnumerator();
        var totalRows = 0;
        double? prevRevenue = null;
        
        while (await enumerator.MoveNextAsync())
        {
            var batch = enumerator.Current;
            totalRows += batch.Length;
            Assert.That(batch.ColumnCount, Is.EqualTo(4));
            
            // Validate actual data values
            if (batch.Length > 0)
            {
                var orderKeyCol = batch.Column(0);
                var revenueCol = batch.Column(1);
                var orderDateCol = batch.Column(2);
                var shipPriorityCol = batch.Column(3);
                
                for (int i = 0; i < batch.Length; i++)
                {
                    // Validate order key is positive
                    var orderKey = GetNumericValue(orderKeyCol, i);
                    Assert.That(orderKey, Is.GreaterThan(0), "Order key should be positive");
                    
                    // Validate revenue is positive and ordered descending
                    var revenue = GetNumericValue(revenueCol, i);
                    Assert.That(revenue, Is.GreaterThan(0), "Revenue should be positive");
                    if (prevRevenue.HasValue)
                    {
                        Assert.That(revenue, Is.LessThanOrEqualTo(prevRevenue.Value), 
                            "Revenue should be ordered descending");
                    }
                    prevRevenue = revenue;
                    
                    // Validate ship priority exists
                    var shipPriority = GetNumericValue(shipPriorityCol, i);
                    Assert.That(shipPriority, Is.GreaterThanOrEqualTo(0), "Ship priority should be non-negative");
                }
            }
        }
        Assert.That(totalRows, Is.LessThanOrEqualTo(10), "Q3 should return at most 10 rows");
        Assert.That(totalRows, Is.GreaterThan(0), "Q3 should return at least one row");
    }

    [Test]
    public async Task TestTpchQ4()
    {
        var result = await _spiceClient.Query(
            """
            SELECT
                o_orderpriority,
                count(*) as order_count
            FROM
                tpch.orders o
            WHERE
                o.o_orderdate >= date '1993-07-01'
                AND o.o_orderdate < date '1993-07-01' + interval '3' month
                AND exists (
                    SELECT
                        *
                    FROM
                        tpch.lineitem l
                    WHERE
                        l.l_orderkey = o.o_orderkey
                        AND l.l_commitdate < l.l_receiptdate
                )
            GROUP BY
                o_orderpriority
            ORDER BY
                o_orderpriority
            """);

        var enumerator = result.GetAsyncEnumerator();
        var totalRows = 0;
        var hasData = false;
        var validPriorities = new HashSet<string> { "1-URGENT", "2-HIGH", "3-MEDIUM", "4-NOT SPECIFIED", "5-LOW" };
        var seenPriorities = new HashSet<string>();
        
        while (await enumerator.MoveNextAsync())
        {
            var batch = enumerator.Current;
            totalRows += batch.Length;
            Assert.That(batch.ColumnCount, Is.EqualTo(2));
            
            // Validate actual data values
            if (batch.Length > 0)
            {
                hasData = true;
                var priorityCol = batch.Column(0) as Apache.Arrow.StringArray;
                var countCol = batch.Column(1);
                
                Assert.That(priorityCol, Is.Not.Null, "o_orderpriority should be string");
                
                for (int i = 0; i < batch.Length; i++)
                {
                    // Validate priority is one of the standard TPC-H priorities
                    var priority = priorityCol.GetString(i);
                    Assert.That(validPriorities, Does.Contain(priority), 
                        $"Priority should be one of the standard values, got {priority}");
                    seenPriorities.Add(priority);
                    
                    // Validate count is positive
                    var count = GetNumericValue(countCol, i);
                    Assert.That(count, Is.GreaterThan(0), "Order count should be positive");
                }
            }
        }
        Assert.That(hasData, Is.True, "Q4 should return at least one row");
        Assert.That(totalRows, Is.EqualTo(5), "Q4 should return 5 priority levels");
        Assert.That(seenPriorities.Count, Is.EqualTo(5), "Should see all 5 priority levels");
    }

    [Test]
    public async Task TestTpchQ5()
    {
        var result = await _spiceClient.Query(
            """
            SELECT
                n_name,
                sum(l_extendedprice * (1 - l_discount)) as revenue
            FROM
                tpch.customer,
                tpch.orders,
                tpch.lineitem,
                tpch.supplier,
                tpch.nation,
                tpch.region
            WHERE
                c_custkey = o_custkey
                AND l_orderkey = o_orderkey
                AND l_suppkey = s_suppkey
                AND c_nationkey = s_nationkey
                AND s_nationkey = n_nationkey
                AND n_regionkey = r_regionkey
                AND r_name = 'ASIA'
                AND o_orderdate >= date '1994-01-01'
                AND o_orderdate < date '1994-01-01' + interval '1' year
            GROUP BY
                n_name
            ORDER BY
                revenue desc
            """);

        var enumerator = result.GetAsyncEnumerator();
        var totalRows = 0;
        var hasData = false;
        var asianNations = new HashSet<string> { "INDIA", "INDONESIA", "JAPAN", "CHINA", "VIETNAM" };
        var seenNations = new HashSet<string>();
        double? prevRevenue = null;
        
        while (await enumerator.MoveNextAsync())
        {
            var batch = enumerator.Current;
            totalRows += batch.Length;
            Assert.That(batch.ColumnCount, Is.EqualTo(2));
            
            // Validate actual data values
            if (batch.Length > 0)
            {
                hasData = true;
                var nationCol = batch.Column(0) as Apache.Arrow.StringArray;
                var revenueCol = batch.Column(1);
                
                Assert.That(nationCol, Is.Not.Null, "n_name should be string");
                
                for (int i = 0; i < batch.Length; i++)
                {
                    // Validate nation is an Asian nation
                    var nation = nationCol.GetString(i);
                    Assert.That(asianNations, Does.Contain(nation), 
                        $"Nation should be one of the 5 Asian nations, got {nation}");
                    seenNations.Add(nation);
                    
                    // Validate revenue is positive and ordered descending
                    var revenue = GetNumericValue(revenueCol, i);
                    Assert.That(revenue, Is.GreaterThan(0), "Revenue should be positive");
                    if (prevRevenue.HasValue)
                    {
                        Assert.That(revenue, Is.LessThanOrEqualTo(prevRevenue.Value), 
                            "Revenue should be ordered descending");
                    }
                    prevRevenue = revenue;
                }
            }
        }
        Assert.That(hasData, Is.True, "Q5 should return at least one row");
        Assert.That(totalRows, Is.EqualTo(5), "Q5 should return 5 Asian nations");
        Assert.That(seenNations.Count, Is.EqualTo(5), "Should see all 5 Asian nations");
        Assert.That(seenNations.Count, Is.EqualTo(5), "Should see all 5 Asian nations");
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