using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>One JSON-lines message from the <c>POST /images/create</c> progress stream.</summary>
internal sealed class ImagePullProgressMessage
{
    [JsonPropertyName("status")]
    public string Status { get; init; }

    [JsonPropertyName("id")]
    public string Id { get; init; }

    [JsonPropertyName("progress")]
    public string Progress { get; init; }

    [JsonPropertyName("error")]
    public string Error { get; init; }

    [JsonPropertyName("errorDetail")]
    public ImagePullErrorDetail ErrorDetail { get; init; }

    /// <summary>Gets the error text carried by this message, if any.</summary>
    public string ErrorMessage =>
        !string.IsNullOrWhiteSpace(Error) ? Error :
        !string.IsNullOrWhiteSpace(ErrorDetail?.Message) ? ErrorDetail.Message : null;

    /// <summary>Renders the message as one human-readable progress line.</summary>
    public string Describe()
    {
        if (string.IsNullOrWhiteSpace(Status))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(Id))
        {
            builder.Append(Id).Append(": ");
        }

        builder.Append(Status);
        if (!string.IsNullOrWhiteSpace(Progress))
        {
            builder.Append(' ').Append(Progress);
        }

        return builder.ToString();
    }
}

/// <summary>The structured error payload of a pull progress message.</summary>
internal sealed class ImagePullErrorDetail
{
    [JsonPropertyName("message")]
    public string Message { get; init; }
}

/// <summary>Response body of <c>POST /images/prune</c>.</summary>
internal sealed class ImagesPruneResponse
{
    [JsonPropertyName("ImagesDeleted")]
    public IReadOnlyList<ImageDeleteRecord> ImagesDeleted { get; init; }

    [JsonPropertyName("SpaceReclaimed")]
    public long SpaceReclaimed { get; init; }
}

/// <summary>One entry of the array returned by <c>DELETE /images/{name}</c>.</summary>
internal sealed class ImageDeleteRecord
{
    [JsonPropertyName("Untagged")]
    public string Untagged { get; init; }

    [JsonPropertyName("Deleted")]
    public string Deleted { get; init; }
}
