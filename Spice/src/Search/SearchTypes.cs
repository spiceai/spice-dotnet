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

namespace Spice.Search;

/// <summary>
/// A search over datasets that have an embedding column and a loaded embedding model.
/// </summary>
/// <remarks>
/// Only <see cref="Text"/> is required. Leave <see cref="Datasets"/> unset to search every
/// dataset with an embedding column, and set <see cref="Keywords"/> to pre-filter the
/// embedding column with a lexical search first, making the search hybrid.
/// </remarks>
public class SearchRequest
{
    /// <summary>
    /// Creates a search request.
    /// </summary>
    /// <param name="text">The query to find similar documents for</param>
    public SearchRequest(string text)
    {
        Text = text;
    }

    /// <summary>
    /// The query to find similar documents for.
    /// </summary>
    [JsonPropertyName("text")]
    public string Text { get; set; }

    /// <summary>
    /// Restricts the search to these datasets. Null searches every dataset with an
    /// embedding column.
    /// </summary>
    [JsonPropertyName("datasets")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Datasets { get; set; }

    /// <summary>
    /// Maximum matches to return per dataset. Null uses the runtime's default.
    /// </summary>
    [JsonPropertyName("limit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Limit { get; set; }

    /// <summary>
    /// An SQL predicate applied before the search, without the WHERE keyword —
    /// for example <c>city = 'Tokyo'</c>.
    /// </summary>
    [JsonPropertyName("where")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Where { get; set; }

    /// <summary>
    /// Extra dataset columns to return. A column that is part of the primary key is
    /// returned in <see cref="SearchMatch.PrimaryKey"/> rather than <see cref="SearchMatch.Data"/>.
    /// </summary>
    [JsonPropertyName("additional_columns")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? AdditionalColumns { get; set; }

    /// <summary>
    /// Pre-filters the embedding column with a lexical search before the vector search
    /// runs, making the search hybrid.
    /// </summary>
    [JsonPropertyName("keywords")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Keywords { get; set; }
}

/// <summary>
/// A single document matched by a search.
/// </summary>
/// <remarks>
/// The runtime omits <see cref="Data"/>, <see cref="PrimaryKey"/> and <see cref="Metadata"/>
/// when they are empty; they default to empty dictionaries so they can be read without a
/// null check.
/// </remarks>
public class SearchMatch
{
    /// <summary>
    /// The dataset the match was found in.
    /// </summary>
    [JsonPropertyName("dataset")]
    public string Dataset { get; set; } = string.Empty;

    /// <summary>
    /// Similarity of the match to the query text. Higher is closer.
    /// </summary>
    /// <remarks>The runtime serializes this as <c>_score</c>.</remarks>
    [JsonPropertyName("_score")]
    public double Score { get; set; }

    /// <summary>
    /// The matched values of each searched column.
    /// </summary>
    [JsonPropertyName("matches")]
    public IReadOnlyDictionary<string, IReadOnlyList<JsonElement>> Matches { get; set; }
        = new Dictionary<string, IReadOnlyList<JsonElement>>();

    /// <summary>
    /// Primary key identifying the matched row, if the dataset declares one.
    /// </summary>
    [JsonPropertyName("primary_key")]
    public IReadOnlyDictionary<string, JsonElement> PrimaryKey { get; set; }
        = new Dictionary<string, JsonElement>();

    /// <summary>
    /// Columns requested via <see cref="SearchRequest.AdditionalColumns"/>.
    /// </summary>
    [JsonPropertyName("data")]
    public IReadOnlyDictionary<string, JsonElement> Data { get; set; }
        = new Dictionary<string, JsonElement>();

    /// <summary>
    /// Any additional metadata the runtime attached to the match.
    /// </summary>
    [JsonPropertyName("metadata")]
    public IReadOnlyDictionary<string, JsonElement> Metadata { get; set; }
        = new Dictionary<string, JsonElement>();
}

/// <summary>
/// The result of a search.
/// </summary>
public class SearchResponse
{
    /// <summary>
    /// Matches, ordered by descending score.
    /// </summary>
    [JsonPropertyName("results")]
    public IReadOnlyList<SearchMatch> Results { get; set; } = new List<SearchMatch>();

    /// <summary>
    /// How long the runtime took to run the search.
    /// </summary>
    [JsonPropertyName("duration_ms")]
    public long DurationMs { get; set; }
}
