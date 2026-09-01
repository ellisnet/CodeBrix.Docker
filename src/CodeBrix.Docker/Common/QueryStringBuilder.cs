using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace CodeBrix.Docker;

/// <summary>
/// Builds the query strings used by Docker Engine API requests, including the daemon's
/// JSON-encoded <c>filters</c> parameter.
/// </summary>
internal sealed class QueryStringBuilder
{
    private readonly List<KeyValuePair<string, string>> _parameters = [];
    private readonly Dictionary<string, List<string>> _filters = new(StringComparer.Ordinal);

    /// <summary>Adds a string parameter, ignoring <see langword="null"/> and empty values.</summary>
    public QueryStringBuilder Add(string name, string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            _parameters.Add(new KeyValuePair<string, string>(name, value));
        }

        return this;
    }

    /// <summary>Adds a boolean parameter as <c>true</c>/<c>false</c>.</summary>
    public QueryStringBuilder Add(string name, bool value) =>
        Add(name, value ? "true" : "false");

    /// <summary>Adds a boolean parameter only when it is <see langword="true"/>.</summary>
    public QueryStringBuilder AddIfTrue(string name, bool value) =>
        value ? Add(name, "true") : this;

    /// <summary>Adds an integer parameter, ignoring <see langword="null"/>.</summary>
    public QueryStringBuilder Add(string name, int? value) =>
        value.HasValue ? Add(name, value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)) : this;

    /// <summary>Adds an integer parameter, ignoring <see langword="null"/>.</summary>
    public QueryStringBuilder Add(string name, long? value) =>
        value.HasValue ? Add(name, value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)) : this;

    /// <summary>Adds one value to the daemon's <c>filters</c> parameter.</summary>
    public QueryStringBuilder AddFilter(string name, string value)
    {
        if (!_filters.TryGetValue(name, out var values))
        {
            values = [];
            _filters[name] = values;
        }

        values.Add(value);
        return this;
    }

    /// <summary>
    /// Adds each entry of <paramref name="labels"/> as a <c>label=key=value</c> filter.
    /// </summary>
    public QueryStringBuilder AddLabelFilters(IDictionary<string, string> labels)
    {
        if (labels is not null)
        {
            foreach (var (key, value) in labels)
            {
                AddFilter("label", string.IsNullOrEmpty(value) ? key : $"{key}={value}");
            }
        }

        return this;
    }

    /// <summary>
    /// Builds the query string, including the leading <c>?</c>. Returns an empty string when nothing was added.
    /// </summary>
    public string Build()
    {
        var all = new List<KeyValuePair<string, string>>(_parameters);
        if (_filters.Count > 0)
        {
            all.Add(new KeyValuePair<string, string>("filters", SerializeFilters(_filters)));
        }

        if (all.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder("?");
        for (var i = 0; i < all.Count; i++)
        {
            if (i > 0)
            {
                builder.Append('&');
            }

            builder.Append(Uri.EscapeDataString(all[i].Key))
                   .Append('=')
                   .Append(Uri.EscapeDataString(all[i].Value));
        }

        return builder.ToString();
    }

    /// <summary>Appends the built query string to <paramref name="path"/>.</summary>
    public string AppendTo(string path) => path + Build();

    private static string SerializeFilters(Dictionary<string, List<string>> filters)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var (name, values) in filters)
            {
                writer.WriteStartArray(name);
                foreach (var value in values)
                {
                    writer.WriteStringValue(value);
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
