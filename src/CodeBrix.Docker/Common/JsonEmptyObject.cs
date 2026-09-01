namespace CodeBrix.Docker;

/// <summary>
/// The empty JSON object (<c>{}</c>) the Docker Engine API uses as a set-membership marker, for
/// example as the value of each entry in <c>ExposedPorts</c>.
/// </summary>
public sealed class JsonEmptyObject
{
    /// <summary>A shared instance, since the type carries no state.</summary>
    public static readonly JsonEmptyObject Instance = new();
}
