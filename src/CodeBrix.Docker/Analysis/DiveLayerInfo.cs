namespace CodeBrix.Docker;

/// <summary>
/// One image layer as reported by Dive.
/// </summary>
/// <param name="Index">The layer's position in the image, starting at zero with the base layer.</param>
/// <param name="SizeBytes">The number of bytes the layer adds to the image.</param>
/// <param name="Command">The build instruction that produced the layer.</param>
public sealed record DiveLayerInfo(int Index, long SizeBytes, string Command)
{
    /// <summary>Gets the layer's content digest, when Dive reported one.</summary>
    public string Digest { get; init; }
}
