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
        while (await enumerator.MoveNextAsync())
        {
            var batch = enumerator.Current;
            totalRows += batch.Length;
            Assert.That(batch.ColumnCount, Is.EqualTo(10));
            
            // Validate column types and data
            if (batch.Length > 0)
            {
                hasData = true;
                var returnFlagCol = batch.Column(0);
                var lineStatusCol = batch.Column(1);
                var sumQtyCol = batch.Column(2);
                var countOrderCol = batch.Column(9);
                
                Assert.That(returnFlagCol, Is.Not.Null, "l_returnflag column should exist");
                Assert.That(lineStatusCol, Is.Not.Null, "l_linestatus column should exist");
                Assert.That(sumQtyCol, Is.Not.Null, "sum_qty column should exist");
                Assert.That(countOrderCol, Is.Not.Null, "count_order column should exist");
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
        while (await enumerator.MoveNextAsync())
        {
            var batch = enumerator.Current;
            totalRows += batch.Length;
            Assert.That(batch.ColumnCount, Is.EqualTo(8));
            
            // Validate column types and data
            if (batch.Length > 0)
            {
                var acctBalCol = batch.Column(0);
                var nameCol = batch.Column(1);
                Assert.That(acctBalCol, Is.Not.Null, "s_acctbal column should exist");
                Assert.That(nameCol, Is.Not.Null, "s_name column should exist");
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
        while (await enumerator.MoveNextAsync())
        {
            var batch = enumerator.Current;
            totalRows += batch.Length;
            Assert.That(batch.ColumnCount, Is.EqualTo(4));
            
            // Validate column types and data
            if (batch.Length > 0)
            {
                var orderKeyCol = batch.Column(0);
                var revenueCol = batch.Column(1);
                Assert.That(orderKeyCol, Is.Not.Null, "l_orderkey column should exist");
                Assert.That(revenueCol, Is.Not.Null, "revenue column should exist");
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
        while (await enumerator.MoveNextAsync())
        {
            var batch = enumerator.Current;
            totalRows += batch.Length;
            Assert.That(batch.ColumnCount, Is.EqualTo(2));
            
            // Validate column types and data
            if (batch.Length > 0)
            {
                hasData = true;
                var priorityCol = batch.Column(0);
                var countCol = batch.Column(1);
                Assert.That(priorityCol, Is.Not.Null, "o_orderpriority column should exist");
                Assert.That(countCol, Is.Not.Null, "order_count column should exist");
            }
        }
        Assert.That(hasData, Is.True, "Q4 should return at least one row");
        Assert.That(totalRows, Is.EqualTo(5), "Q4 should return 5 priority levels");
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
        while (await enumerator.MoveNextAsync())
        {
            var batch = enumerator.Current;
            totalRows += batch.Length;
            Assert.That(batch.ColumnCount, Is.EqualTo(2));
            
            // Validate column types and data
            if (batch.Length > 0)
            {
                hasData = true;
                var nationCol = batch.Column(0);
                var revenueCol = batch.Column(1);
                Assert.That(nationCol, Is.Not.Null, "n_name column should exist");
                Assert.That(revenueCol, Is.Not.Null, "revenue column should exist");
            }
        }
        Assert.That(hasData, Is.True, "Q5 should return at least one row");
        Assert.That(totalRows, Is.EqualTo(5), "Q5 should return 5 Asian nations");
    }
}