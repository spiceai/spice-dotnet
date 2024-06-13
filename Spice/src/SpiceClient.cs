using Apache.Arrow;
using Spice.Config;
using Spice.Flight;

namespace Spice;

public class SpiceClient
{
    public string? AppId { get; internal set; }
    public string? ApiKey { get; internal set; }
    public string FlightAddress { get; internal set; } = SpiceDefaultConfigLocal.FlightAddress;
    public int MaxRetries { get; internal set; } = 3;

    private SpiceFlightClient? FlightClient { get; set; }

    internal void Init()
    {
        FlightClient = new SpiceFlightClient(FlightAddress, AppId, ApiKey);
    }

    public IAsyncEnumerator<RecordBatch> DoGetAsync(string sql)
    {
        if (FlightClient == null) throw new Exception("FlightClient not initialized");

        return FlightClient.QueryAsync(sql);
    }

    public IEnumerable<RecordBatch> DoGet(string sql)
    {
        if (FlightClient == null) throw new Exception("FlightClient not initialized");

        return FlightClient.Query(sql);
    }
}