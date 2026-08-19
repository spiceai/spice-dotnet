# Dotnet Spice SDK

Dotnet SDK for Spice.ai.
- Minimum supported version: .NET 8.0
- Supports .NET Standard 2.0+, .NET 8.0, .NET 9.0, and .NET 10.0.
- Asynchronous I/O.

## Install

```bash
dotnet add package spiceai
```

## Usage

<!-- NOTE: If you're changing the code examples below, make sure you update `tests/readme_test.rs`. -->

### Self-Hosted Spice Runtime

Follow the [quickstart guide](https://github.com/spiceai/spiceai?tab=readme-ov-file#%EF%B8%8F-quickstart-local-machine) to install and run Spice locally.

#### Initialize Client

```csharp
using Spice;

using var client = new SpiceClientBuilder().Build();
```

#### Query

```csharp
using Spice;

using var client = new SpiceClientBuilder().Build();

var data = await client.Query("SELECT * FROM my_table LIMIT 10;");
```

#### Parameterized Queries

Use parameterized queries to prevent SQL injection and improve performance:

```csharp
using Spice;

using var client = new SpiceClientBuilder().Build();

var parameters = new Dictionary<string, object>
{
    { "product_id", 42 },
    { "min_price", 10.0 }
};

var data = await client.Query(
    "SELECT * FROM products WHERE id = :product_id AND price >= :min_price", 
    parameters);
```

##### Parameterized Queries with Positional Placeholders

For more control over parameter types, use `QueryWithParams` with positional placeholders (`$1`, `$2`, etc.). This uses the ADBC protocol and supports explicit type specification:

> **Note**: The `QueryWithParams` method requires the ADBC FlightSQL driver to support prepared statements.
> The pure C# driver (`Apache.Arrow.Adbc.Drivers.FlightSql`) currently does not implement `Prepare()`.
> For full parameterized query support, the Go-based interop driver (`Apache.Arrow.Adbc.Drivers.Interop.FlightSql`)
> is required. See the [Apache ADBC documentation](https://arrow.apache.org/adbc/) for more details.

```csharp
using Spice;
using Spice.Params;

using var client = new SpiceClientBuilder().Build();

// Basic usage with type inference
var data = await client.QueryWithParams(
    "SELECT * FROM products WHERE id = $1 AND price >= $2",
    42,           // Inferred as Int32
    10.50         // Inferred as Double
);

// Read the results as Arrow data
while (await data!.ReadNextRecordBatchAsync() is { } batch)
{
    // Process batch...
}
```

##### Explicit Type Control with the Param Class

Use the `Param` class for explicit control over Arrow data types:

```csharp
using Spice;
using Spice.Params;

using var client = new SpiceClientBuilder().Build();

// Explicitly typed parameters
var data = await client.QueryWithParams(
    "SELECT * FROM orders WHERE customer_id = $1 AND order_date >= $2 AND total > $3",
    Param.Int64(12345),                           // Explicit Int64
    Param.Date32(new DateTime(2024, 1, 1)),       // Date without time
    Param.Decimal128(100.00m, 10, 2)              // Decimal with precision/scale
);

// Supported Param types:
// - Integers: Param.Int8, Int16, Int32, Int64, UInt8, UInt16, UInt32, UInt64
// - Floating point: Param.Float, Double
// - Text/Binary: Param.String, Binary
// - Boolean: Param.Boolean
// - Date/Time: Param.Date32, Date64, Time32, Time64, Timestamp
// - Duration: Param.DurationSeconds, DurationMilliseconds, DurationMicroseconds, DurationNanoseconds
// - Decimal: Param.Decimal128, Decimal256
// - Null: Param.Null
```

##### Mixed Parameters

You can mix inferred and explicit types in the same query:

```csharp
var data = await client.QueryWithParams(
    "SELECT * FROM users WHERE name = $1 AND age > $2 AND verified = $3",
    "John",                    // Inferred as String
    Param.Int16(18),           // Explicit Int16
    true                       // Inferred as Boolean
);
```

#### Refresh Dataset

Trigger a refresh of an accelerated dataset:

```csharp
using Spice;

using var client = new SpiceClientBuilder().Build();

await client.RefreshDatasetAsync("my_dataset");
```

##### Refresh Options

Override the dataset's configured refresh settings for a single refresh by passing
`RefreshOptions`. Any option left unset falls back to the dataset's Spicepod configuration.

```csharp
using Spice;
using Spice.Datasets;

using var client = new SpiceClientBuilder().Build();

await client.RefreshDatasetAsync("taxi_trips", new RefreshOptions()
    .WithRefreshSql("SELECT * FROM taxi_trips WHERE tip_amount > 10.0")
    .WithRefreshMode(RefreshMode.Append)
    .WithMaxJitter(TimeSpan.FromSeconds(10)));
```

Object initializer syntax works too:

```csharp
await client.RefreshDatasetAsync("taxi_trips", new RefreshOptions
{
    RefreshSql = "SELECT * FROM taxi_trips WHERE tip_amount > 10.0",
    RefreshMode = RefreshMode.Append,
    MaxJitter = TimeSpan.FromSeconds(10),
});
```

| Option | Type | Description |
| --- | --- | --- |
| `RefreshSql` | `string` | The SQL statement used for this refresh. Defaults to the dataset's `refresh_sql`. |
| `RefreshMode` | `RefreshMode` | `Full` replaces the accelerated data; `Append` adds newly returned rows. Defaults to the dataset's `refresh_mode`. |
| `MaxJitter` | `TimeSpan` | Maximum jitter added before the refresh starts. Defaults to the dataset's `refresh_jitter_max`. |

> **Note**: On-demand refreshes apply to the `full` and `append` refresh modes. Datasets accelerated with
> `changes` mode are kept up to date by change data capture and are not refreshed through this API.

#### Custom Connection Settings

```csharp
using Spice;

// Connect to Spice on a custom host/port
using var client = new SpiceClientBuilder()
    .WithFlightAddress("grpc://my-server:50051")
    .WithHttpAddress("http://my-server:8090")
    .Build();

// Enable TLS for self-hosted Spice
using var tlsClient = new SpiceClientBuilder()
    .WithFlightAddress("grpc+tls://my-server:50051")
    .WithHttpAddress("https://my-server:8090")
    .WithTls(true)
    .Build();
```

### Spice.ai Cloud

#### Initialize Client

```csharp
using Spice;

using var client = new SpiceClientBuilder()
    .WithSpiceCloud("API_KEY")  // Automatically configures endpoints and enables TLS
    .Build();
```

#### Query

```csharp
using Spice;

using var client = new SpiceClientBuilder()
    .WithSpiceCloud("API_KEY")
    .Build();

var data = await client.Query("SELECT * FROM eth.recent_blocks LIMIT 10;");
```

#### Parameterized Queries

```csharp
using Spice;

using var client = new SpiceClientBuilder()
    .WithSpiceCloud("API_KEY")
    .Build();

var parameters = new Dictionary<string, object>
{
    { "nation_name", "CHINA" },
    { "min_key", 0 }
};

var data = await client.Query(
    "SELECT * FROM tpch.nation WHERE n_name = :nation_name AND n_nationkey >= :min_key", 
    parameters);
```

#### Refresh Dataset

```csharp
using Spice;
using Spice.Datasets;

using var client = new SpiceClientBuilder()
    .WithSpiceCloud("API_KEY")
    .Build();

await client.RefreshDatasetAsync("my_dataset");

// Or with refresh overrides for this refresh only
await client.RefreshDatasetAsync("my_dataset", new RefreshOptions()
    .WithRefreshMode(RefreshMode.Append));
```

#### Search

`SearchAsync` runs vector similarity, keyword, and hybrid search against datasets that have
an embedding column and a loaded embedding model.

```csharp
using Spice;
using Spice.Search;

using var client = new SpiceClientBuilder().Build();

var response = await client.SearchAsync(new SearchRequest("tickets to Tokyo")
{
    Datasets = new[] { "app_messages" },
    Limit = 3,
});

Console.WriteLine($"{response.Results.Count} matches in {response.DurationMs}ms");
foreach (var match in response.Results)
{
    Console.WriteLine($"{match.Dataset} {match.Score}");
}
```

Only `Text` is required. `Datasets` restricts the search — leave it unset to search every
dataset with an embedding column. `Limit` caps matches per dataset, `Where` applies an SQL
predicate before the search, and `AdditionalColumns` names extra columns to return. Setting
`Keywords` pre-filters the embedding column with a lexical search before the vector search
runs, making the search hybrid:

```csharp
var response = await client.SearchAsync(new SearchRequest("tickets to Tokyo")
{
    Where = "city = 'Tokyo'",
    AdditionalColumns = new[] { "timestamp" },
    Keywords = new[] { "plane", "tickets" },
});
```

Each `SearchMatch` carries the `Dataset` it was found in, its similarity `Score`, the
matched column values in `Matches`, the row's `PrimaryKey`, the columns requested via
`AdditionalColumns` in `Data`, and any `Metadata`. The runtime omits the last three when
empty; they default to empty dictionaries, so they can be read without a null check.

### Memory Management

The `SpiceClient` implements `IDisposable` and should be properly disposed to release network resources (gRPC channels, HTTP clients). Use the `using` statement or `using` declaration for automatic disposal:

```csharp
// Using statement (automatically disposes when scope exits)
using (var client = new SpiceClientBuilder().WithSpiceCloud("API_KEY").Build())
{
    var data = await client.Query("SELECT * FROM tpch.customer LIMIT 10;");
    // Process data...
} // Client is disposed here

// Or using declaration (C# 8.0+)
using var client = new SpiceClientBuilder().WithSpiceCloud("API_KEY").Build();
var data = await client.Query("SELECT * FROM tpch.customer LIMIT 10;");
// Client is disposed at end of scope
```

**Important**: Always dispose of `SpiceClient` instances to prevent resource leaks, especially in long-running applications or when creating multiple client instances.

## Documentation

Check out our [Documentation](https://docs.spice.ai/sdks/dotnet-sdk) to learn more about how to use the Dotnet SDK.
