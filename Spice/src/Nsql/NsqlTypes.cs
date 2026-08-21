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

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spice.Nsql;

/// <summary>
/// A natural-language query against the runtime's <c>/v1/nsql</c> endpoint.
/// </summary>
/// <remarks>
/// Only <see cref="Query"/> is required. The runtime needs an LLM model configured in the
/// Spicepod to translate it; when exactly one is configured, <see cref="Model"/> may be left
/// unset and the runtime selects it.
/// </remarks>
public class NsqlRequest
{
    /// <summary>
    /// Creates an NSQL request.
    /// </summary>
    /// <param name="query">The question to answer, in natural language</param>
    public NsqlRequest(string query)
    {
        Query = query;
    }

    /// <summary>
    /// The question to answer, in natural language. Required.
    /// </summary>
    [JsonPropertyName("query")]
    public string Query { get; set; }

    /// <summary>
    /// Names the LLM used to generate SQL. When unset, the runtime uses the only compatible
    /// model configured in the Spicepod, and reports an error if there is not exactly one.
    /// </summary>
    [JsonPropertyName("model")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Model { get; set; }

    /// <summary>
    /// Hints which datasets to sample when building model context. This is a sampling hint
    /// only - it does not restrict which tables the generated query may reference. When
    /// unset, all datasets are used.
    /// </summary>
    [JsonPropertyName("datasets")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Datasets { get; set; }

    /// <summary>
    /// Includes sample rows in the context given to the model. It improves generation on
    /// ambiguous schemas at the cost of sending data values to the model.
    /// </summary>
    [JsonPropertyName("sample_data_enabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool SampleDataEnabled { get; set; }

    /// <summary>
    /// A stable key forwarded to the model provider for prompt caching. Reuse it across
    /// related requests to benefit from it.
    /// </summary>
    [JsonPropertyName("prompt_cache_key")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PromptCacheKey { get; set; }
}

/// <summary>
/// Describes one column of an <see cref="NsqlResponse"/>.
/// </summary>
public class NsqlField
{
    /// <summary>
    /// The column name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The column's Arrow type in its JSON encoding. Simple types encode as a quoted string
    /// (<c>"Utf8"</c>, <c>"Int64"</c>); parameterized ones as an object (for example
    /// <c>{"Timestamp":["Nanosecond",null]}</c>), which is why this is captured as raw JSON
    /// rather than a fixed shape.
    /// </summary>
    [JsonPropertyName("data_type")]
    public JsonElement DataType { get; set; }

    /// <summary>
    /// Whether the column admits nulls.
    /// </summary>
    [JsonPropertyName("nullable")]
    public bool Nullable { get; set; }
}

/// <summary>
/// The schema of the rows an NSQL call returned.
/// </summary>
/// <remarks>
/// <see cref="Fields"/> is empty when the generated query returned no rows - the runtime
/// omits the schema body in that case.
/// </remarks>
public class NsqlSchema
{
    /// <summary>
    /// The columns of the result, in order.
    /// </summary>
    [JsonPropertyName("fields")]
    public IReadOnlyList<NsqlField> Fields { get; set; } = new List<NsqlField>();
}

/// <summary>
/// The result of running a natural-language query via <see cref="SpiceClient.NsqlAsync"/>.
/// </summary>
public class NsqlResponse
{
    /// <summary>
    /// The query the model generated. It is worth logging: a surprising result is usually a
    /// surprising query.
    /// </summary>
    [JsonPropertyName("sql")]
    public string SQL { get; set; } = string.Empty;

    /// <summary>
    /// The number of rows returned.
    /// </summary>
    [JsonPropertyName("row_count")]
    public int RowCount { get; set; }

    /// <summary>
    /// Describes the columns in <see cref="Data"/>.
    /// </summary>
    [JsonPropertyName("schema")]
    public NsqlSchema Schema { get; set; } = new();

    /// <summary>
    /// The rows, each keyed by column name. Values are decoded from JSON, so they carry
    /// JSON's types rather than the Arrow types named in <see cref="Schema"/> - numbers
    /// arrive as <see cref="JsonElement"/> holding a number. Use
    /// <see cref="SpiceClient.NsqlGenerateSqlAsync"/> with <see cref="SpiceClient.Query"/>
    /// when Arrow-typed results matter.
    /// </summary>
    [JsonPropertyName("data")]
    public IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> Data { get; set; }
        = new List<IReadOnlyDictionary<string, JsonElement>>();
}
