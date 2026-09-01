using System.Collections.Generic;
using System.Text;

namespace RedisSetupTool.TerminalView.Tests.Fakes;

/// <summary>Records everything fed to it, byte for byte.</summary>
public sealed class RecordingTerminalSink : ITerminalSink
{
    private readonly List<byte[]> _chunks = [];
    private readonly List<string> _texts = [];
    private readonly object _gate = new();

    /// <summary>Gets the byte chunks, in the order they arrived.</summary>
    public IReadOnlyList<byte[]> Chunks
    {
        get
        {
            lock (_gate)
            {
                return _chunks.ToArray();
            }
        }
    }

    /// <summary>Gets the text fragments, in the order they arrived.</summary>
    public IReadOnlyList<string> Texts
    {
        get
        {
            lock (_gate)
            {
                return _texts.ToArray();
            }
        }
    }

    /// <summary>Gets how many times <see cref="Reset"/> was called.</summary>
    public int ResetCount { get; private set; }

    /// <summary>Gets every byte fed, concatenated.</summary>
    public byte[] AllBytes
    {
        get
        {
            lock (_gate)
            {
                var total = 0;
                foreach (var chunk in _chunks)
                {
                    total += chunk.Length;
                }

                var all = new byte[total];
                var offset = 0;
                foreach (var chunk in _chunks)
                {
                    chunk.CopyTo(all, offset);
                    offset += chunk.Length;
                }

                return all;
            }
        }
    }

    /// <summary>Gets every byte fed, decoded once at the end.</summary>
    public string DecodedText => Encoding.UTF8.GetString(AllBytes);

    /// <inheritdoc />
    public void Feed(byte[] data, int length)
    {
        lock (_gate)
        {
            var copy = new byte[length];
            System.Array.Copy(data, copy, length);
            _chunks.Add(copy);
        }
    }

    /// <inheritdoc />
    public void Feed(string text)
    {
        lock (_gate)
        {
            _texts.Add(text);
        }
    }

    /// <inheritdoc />
    public void Reset() => ResetCount++;
}
