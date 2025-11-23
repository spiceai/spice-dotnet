# Dotnet Spice SDK

Dotnet SDK for Spice.ai.
- Supports .NET Standard 2.0+, .NET 8.0, .NET 9.0, and .NET 10.0.
- Asynchronous I/O.

## Install

```bash
dotnet add package spiceai
```

## Usage

<!-- NOTE: If you're changing the code examples below, make sure you update `tests/readme_test.rs`. -->

### Usage with locally running [spice runtime](https://github.com/spiceai/spiceai)

Follow the [quickstart guide](https://github.com/spiceai/spiceai?tab=readme-ov-file#%EF%B8%8F-quickstart-local-machine) to install and run spice locally

```csharp
using Spice;

using var client = new SpiceClientBuilder().Build();
```

### New client with https://spice.ai cloud

```csharp
using Spice;

using var client = new SpiceClientBuilder()
            .WithApiKey("API_KEY")
            .WithSpiceCloud()
            .Build();
```

### Arrow Query

SQL Query

```csharp
using Spice;

using var client = new SpiceClientBuilder()
            .WithApiKey("API_KEY")
            .WithSpiceCloud()
            .Build();

var data = await client.Query("SELECT * FROM eth.recent_blocks LIMIT 10;");
```

### Memory Management

The `SpiceClient` implements `IDisposable` and should be properly disposed to release network resources (gRPC channels, HTTP clients). Use the `using` statement or `using` declaration for automatic disposal:

```csharp
// Using statement (automatically disposes when scope exits)
using (var client = new SpiceClientBuilder().WithSpiceCloud().WithApiKey("API_KEY").Build())
{
    var data = await client.Query("SELECT * FROM tpch.customer LIMIT 10;");
    // Process data...
} // Client is disposed here

// Or using declaration (C# 8.0+)
using var client = new SpiceClientBuilder().WithSpiceCloud().WithApiKey("API_KEY").Build();
var data = await client.Query("SELECT * FROM tpch.customer LIMIT 10;");
// Client is disposed at end of scope
```

**Important**: Always dispose of `SpiceClient` instances to prevent resource leaks, especially in long-running applications or when creating multiple client instances.

## Documentation

Check out our [Documentation](https://docs.spice.ai/sdks/dotnet-sdk) to learn more about how to use the Dotnet SDK.
