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
    private SpiceClient _spiceClient = null!;
    private string? ApiKey { get; set; }
    private bool UseLocalhost { get; set; }

    [OneTimeSetUp]
    public void OneTimeSetup()
    {
        ApiKey = Environment.GetEnvironmentVariable("API_KEY");
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
                throw new Exception("No API_KEY provided");
            }
            _spiceClient = new SpiceClientBuilder()
                .WithSpiceCloud(ApiKey)
                .Build();
        }
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
                var returnFlagCol = batch.Column(0);
                var lineStatusCol = batch.Column(1);
                var sumQtyCol = batch.Column(2);
                var sumBasePriceCol = batch.Column(3);
                var countOrderCol = batch.Column(9);
                
                Assert.That(returnFlagCol, Is.Not.Null, "l_returnflag should be string");
                Assert.That(lineStatusCol, Is.Not.Null, "l_linestatus should be string");
                
                for (int i = 0; i < batch.Length; i++)
                {
                    // Validate return flags are valid values
                    var returnFlag = GetStringValue(returnFlagCol, i);
                    Assert.That(validReturnFlags, Does.Contain(returnFlag), 
                        $"Return flag should be A, N, or R, got {returnFlag}");
                    
                    // Validate line statuses are valid values
                    var lineStatus = GetStringValue(lineStatusCol, i);
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
                var nameCol = batch.Column(1);
                
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
                    var supplierName = GetStringValue(nameCol, i);
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
                var priorityCol = batch.Column(0);
                var countCol = batch.Column(1);
                
                for (int i = 0; i < batch.Length; i++)
                {
                    // Validate priority is one of the standard TPC-H priorities
                    var priority = GetStringValue(priorityCol, i);
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
                var nationCol = batch.Column(0);
                var revenueCol = batch.Column(1);
                
                for (int i = 0; i < batch.Length; i++)
                {
                    // Validate nation is an Asian nation
                    var nation = GetStringValue(nationCol, i);
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
    }

    [Test]
    public async Task TestTpchQ6()
    {
        // TPC-H Q6: Forecasting Revenue Change Query
        var result = await _spiceClient.Query(@"
            SELECT SUM(l_extendedprice * l_discount) AS revenue
            FROM tpch.lineitem
            WHERE l_shipdate >= DATE '1994-01-01'
              AND l_shipdate < DATE '1995-01-01'
              AND l_discount BETWEEN 0.05 AND 0.07
              AND l_quantity < 24");

        var enumerator = result.GetAsyncEnumerator();
        var totalRows = 0;
        var hasData = false;

        while (await enumerator.MoveNextAsync())
        {
            var batch = enumerator.Current;
            totalRows += batch.Length;
            Assert.That(batch.ColumnCount, Is.EqualTo(1));
            
            if (batch.Length > 0)
            {
                hasData = true;
                var revenueCol = batch.Column(0);
                var revenue = GetNumericValue(revenueCol, 0);
                
                // Validate revenue is in expected range for TPC-H scale factor 1
                Assert.That(revenue, Is.GreaterThan(0), "Revenue should be positive");
                Assert.That(revenue, Is.GreaterThan(100_000_000), "Revenue should be significant for SF=1");
                Assert.That(revenue, Is.LessThan(150_000_000), "Revenue should be reasonable for SF=1");
            }
        }
        Assert.That(hasData, Is.True, "Q6 should return exactly one row");
        Assert.That(totalRows, Is.EqualTo(1), "Q6 should return exactly one aggregate row");
    }

    [Test]
    public async Task TestTpchQ7()
    {
        // TPC-H Q7: Volume Shipping Query
        var result = await _spiceClient.Query(@"
            SELECT supp_nation, cust_nation, l_year, SUM(volume) AS revenue
            FROM (
                SELECT n1.n_name AS supp_nation,
                       n2.n_name AS cust_nation,
                       EXTRACT(YEAR FROM l_shipdate) AS l_year,
                       l_extendedprice * (1 - l_discount) AS volume
                FROM tpch.supplier, tpch.lineitem, tpch.orders, tpch.customer, tpch.nation n1, tpch.nation n2
                WHERE s_suppkey = l_suppkey
                  AND o_orderkey = l_orderkey
                  AND c_custkey = o_custkey
                  AND s_nationkey = n1.n_nationkey
                  AND c_nationkey = n2.n_nationkey
                  AND ((n1.n_name = 'FRANCE' AND n2.n_name = 'GERMANY')
                    OR (n1.n_name = 'GERMANY' AND n2.n_name = 'FRANCE'))
                  AND l_shipdate BETWEEN DATE '1995-01-01' AND DATE '1996-12-31'
            ) AS shipping
            GROUP BY supp_nation, cust_nation, l_year
            ORDER BY supp_nation, cust_nation, l_year");

        var enumerator = result.GetAsyncEnumerator();
        var totalRows = 0;
        var hasData = false;

        while (await enumerator.MoveNextAsync())
        {
            var batch = enumerator.Current;
            totalRows += batch.Length;
            Assert.That(batch.ColumnCount, Is.EqualTo(4));
            
            if (batch.Length > 0)
            {
                hasData = true;
                var suppNationCol = batch.Column(0);
                var custNationCol = batch.Column(1);
                var yearCol = batch.Column(2);
                var revenueCol = batch.Column(3);
                
                for (int i = 0; i < batch.Length; i++)
                {
                    var suppNation = GetStringValue(suppNationCol, i);
                    var custNation = GetStringValue(custNationCol, i);
                    var year = GetNumericValue(yearCol, i);
                    var revenue = GetNumericValue(revenueCol, i);
                    
                    // Validate nations are FRANCE or GERMANY
                    Assert.That(new[] { "FRANCE", "GERMANY" }, Does.Contain(suppNation));
                    Assert.That(new[] { "FRANCE", "GERMANY" }, Does.Contain(custNation));
                    Assert.That(suppNation, Is.Not.EqualTo(custNation), "Supplier and customer nations should be different");
                    
                    // Validate year is 1995 or 1996
                    Assert.That(year, Is.InRange(1995, 1996));
                    
                    // Validate revenue is positive
                    Assert.That(revenue, Is.GreaterThan(0), "Revenue should be positive");
                }
            }
        }
        Assert.That(hasData, Is.True, "Q7 should return at least one row");
        Assert.That(totalRows, Is.EqualTo(4), "Q7 should return 4 rows (2 nations × 2 years)");
    }

    [Test]
    public async Task TestTpchQ8()
    {
        // TPC-H Q8: National Market Share Query
        var result = await _spiceClient.Query(@"
            SELECT o_year, 
                   SUM(CASE WHEN nation = 'BRAZIL' THEN volume ELSE 0 END) / SUM(volume) AS mkt_share
            FROM (
                SELECT EXTRACT(YEAR FROM o_orderdate) AS o_year,
                       l_extendedprice * (1 - l_discount) AS volume,
                       n2.n_name AS nation
                FROM tpch.part, tpch.supplier, tpch.lineitem, tpch.orders, tpch.customer, tpch.nation n1, tpch.nation n2, tpch.region
                WHERE p_partkey = l_partkey
                  AND s_suppkey = l_suppkey
                  AND l_orderkey = o_orderkey
                  AND o_custkey = c_custkey
                  AND c_nationkey = n1.n_nationkey
                  AND n1.n_regionkey = r_regionkey
                  AND r_name = 'AMERICA'
                  AND s_nationkey = n2.n_nationkey
                  AND o_orderdate BETWEEN DATE '1995-01-01' AND DATE '1996-12-31'
                  AND p_type = 'ECONOMY ANODIZED STEEL'
            ) AS all_nations
            GROUP BY o_year
            ORDER BY o_year");

        var enumerator = result.GetAsyncEnumerator();
        var totalRows = 0;
        var hasData = false;

        while (await enumerator.MoveNextAsync())
        {
            var batch = enumerator.Current;
            totalRows += batch.Length;
            Assert.That(batch.ColumnCount, Is.EqualTo(2));
            
            if (batch.Length > 0)
            {
                hasData = true;
                var yearCol = batch.Column(0);
                var mktShareCol = batch.Column(1);
                
                for (int i = 0; i < batch.Length; i++)
                {
                    var year = GetNumericValue(yearCol, i);
                    var mktShare = GetNumericValue(mktShareCol, i);
                    
                    // Validate year is 1995 or 1996
                    Assert.That(year, Is.InRange(1995, 1996));
                    
                    // Validate market share is a valid percentage (0-1)
                    Assert.That(mktShare, Is.GreaterThanOrEqualTo(0), "Market share should be >= 0");
                    Assert.That(mktShare, Is.LessThanOrEqualTo(1), "Market share should be <= 1");
                }
            }
        }
        Assert.That(hasData, Is.True, "Q8 should return at least one row");
    }

    [Test]
    public async Task TestTpchQ9()
    {
        // TPC-H Q9: Product Type Profit Measure Query
        var result = await _spiceClient.Query(@"
            SELECT nation, o_year, SUM(amount) AS sum_profit
            FROM (
                SELECT n_name AS nation,
                       EXTRACT(YEAR FROM o_orderdate) AS o_year,
                       l_extendedprice * (1 - l_discount) - ps_supplycost * l_quantity AS amount
                FROM tpch.part, tpch.supplier, tpch.lineitem, tpch.partsupp, tpch.orders, tpch.nation
                WHERE s_suppkey = l_suppkey
                  AND ps_suppkey = l_suppkey
                  AND ps_partkey = l_partkey
                  AND p_partkey = l_partkey
                  AND o_orderkey = l_orderkey
                  AND s_nationkey = n_nationkey
                  AND p_name LIKE '%green%'
            ) AS profit
            GROUP BY nation, o_year
            ORDER BY nation, o_year DESC
            LIMIT 20");

        var enumerator = result.GetAsyncEnumerator();
        var totalRows = 0;
        var hasData = false;
        var prevNation = "";

        while (await enumerator.MoveNextAsync())
        {
            var batch = enumerator.Current;
            totalRows += batch.Length;
            Assert.That(batch.ColumnCount, Is.EqualTo(3));
            
            if (batch.Length > 0)
            {
                hasData = true;
                var nationCol = batch.Column(0);
                var yearCol = batch.Column(1);
                var profitCol = batch.Column(2);
                
                for (int i = 0; i < batch.Length; i++)
                {
                    var nation = GetStringValue(nationCol, i);
                    var year = GetNumericValue(yearCol, i);
                    var profit = GetNumericValue(profitCol, i);
                    
                    // Validate nation is not empty
                    Assert.That(nation, Is.Not.Empty, "Nation should not be empty");
                    
                    // Validate year is reasonable (TPC-H data typically spans 1992-1998)
                    Assert.That(year, Is.InRange(1992, 1998));
                    
                    // Within same nation, years should be descending
                    if (nation == prevNation && i > 0)
                    {
                        var prevYear = GetNumericValue(yearCol, i - 1);
                        Assert.That(year, Is.LessThanOrEqualTo(prevYear), 
                            "Years should be descending within same nation");
                    }
                    prevNation = nation;
                }
            }
        }
        Assert.That(hasData, Is.True, "Q9 should return at least one row");
        Assert.That(totalRows, Is.LessThanOrEqualTo(20), "Q9 should return at most 20 rows (LIMIT 20)");
    }

    [Test]
    public async Task TestTpchQ10()
    {
        // TPC-H Q10: Returned Item Reporting Query
        var result = await _spiceClient.Query(@"
            SELECT c_custkey, c_name, SUM(l_extendedprice * (1 - l_discount)) AS revenue,
                   c_acctbal, n_name, c_address, c_phone, c_comment
            FROM tpch.customer, tpch.orders, tpch.lineitem, tpch.nation
            WHERE c_custkey = o_custkey
              AND l_orderkey = o_orderkey
              AND o_orderdate >= DATE '1993-10-01'
              AND o_orderdate < DATE '1994-01-01'
              AND l_returnflag = 'R'
              AND c_nationkey = n_nationkey
            GROUP BY c_custkey, c_name, c_acctbal, c_phone, n_name, c_address, c_comment
            ORDER BY revenue DESC
            LIMIT 20");

        var enumerator = result.GetAsyncEnumerator();
        var totalRows = 0;
        var hasData = false;
        var prevRevenue = double.MaxValue;

        while (await enumerator.MoveNextAsync())
        {
            var batch = enumerator.Current;
            totalRows += batch.Length;
            Assert.That(batch.ColumnCount, Is.EqualTo(8));
            
            if (batch.Length > 0)
            {
                hasData = true;
                var custkeyCol = batch.Column(0);
                var nameCol = batch.Column(1);
                var revenueCol = batch.Column(2);
                var acctbalCol = batch.Column(3);
                var nationCol = batch.Column(4);
                var addressCol = batch.Column(5);
                var phoneCol = batch.Column(6);
                var commentCol = batch.Column(7);
                
                for (int i = 0; i < batch.Length; i++)
                {
                    var custkey = GetNumericValue(custkeyCol, i);
                    var name = GetStringValue(nameCol, i);
                    var revenue = GetNumericValue(revenueCol, i);
                    var acctbal = GetNumericValue(acctbalCol, i);
                    var nation = GetStringValue(nationCol, i);
                    var address = GetStringValue(addressCol, i);
                    var phone = GetStringValue(phoneCol, i);
                    var comment = GetStringValue(commentCol, i);
                    
                    // Validate custkey is positive
                    Assert.That(custkey, Is.GreaterThan(0), "Customer key should be positive");
                    
                    // Validate customer name is not empty
                    Assert.That(name, Is.Not.Empty, "Customer name should not be empty");
                    
                    // Validate revenue is positive
                    Assert.That(revenue, Is.GreaterThan(0), "Revenue should be positive");
                    
                    // Validate revenue is ordered descending
                    Assert.That(revenue, Is.LessThanOrEqualTo(prevRevenue), 
                        "Revenue should be ordered descending");
                    prevRevenue = revenue;
                    
                    // Validate nation is not empty
                    Assert.That(nation, Is.Not.Empty, "Nation should not be empty");
                    
                    // Validate address is not empty
                    Assert.That(address, Is.Not.Empty, "Address should not be empty");
                    
                    // Validate phone is not empty
                    Assert.That(phone, Is.Not.Empty, "Phone should not be empty");
                }
            }
        }
        Assert.That(hasData, Is.True, "Q10 should return at least one row");
        Assert.That(totalRows, Is.LessThanOrEqualTo(20), "Q10 should return at most 20 rows (LIMIT 20)");
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

    private static string GetStringValue(Apache.Arrow.IArrowArray column, int index)
    {
        return column switch
        {
            Apache.Arrow.StringArray stringArray => stringArray.GetString(index) ?? string.Empty,
            Apache.Arrow.LargeStringArray largeStringArray => largeStringArray.GetString(index) ?? string.Empty,
            _ => throw new InvalidOperationException($"Unsupported string column type: {column.GetType().Name}")
        };
    }
}