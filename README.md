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

#### Refresh Dataset

Trigger a refresh of an accelerated dataset:

```csharp
using Spice;

using var client = new SpiceClientBuilder().Build();

await client.RefreshDatasetAsync("my_dataset");
```

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

using var client = new SpiceClientBuilder()
    .WithSpiceCloud("API_KEY")
    .Build();

await client.RefreshDatasetAsync("my_dataset");
```

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
