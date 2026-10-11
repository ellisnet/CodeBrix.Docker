using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Docker.Tests;

/// <summary>
/// Daemon-free tests for how a streaming exec session decodes what the daemon sends: the framing
/// follows the requested TTY flag, never the hijack response's <c>Content-Type</c> (Podman and Docker
/// before API 1.42 label framed streams <c>application/vnd.docker.raw-stream</c>), and frames survive
/// any split between the bytes read with the response headers and the rest of the socket.
/// </summary>
public sealed class ContainerExecStreamTests
{
    private const byte StdOut = 1;

    private const byte StdErr = 2;

    [Theory]
    [InlineData(HijackedStream.RawStreamContentType)]
    [InlineData(HijackedStream.MultiplexedStreamContentType)]
    [InlineData("")]
    public async Task ReadToEndAsync_WithoutTty_DemultiplexesWhateverContentTypeTheDaemonAnswered(string contentType)
    {
        //Arrange
        var wire = Frames((StdOut, "to-stdout\n"), (StdErr, "to-stderr\n"), (StdOut, "more\n"));
        await using var session = CreateSession(wire, contentType, tty: false);

        //Act
        var logs = await session.ReadToEndAsync(TestContext.Current.CancellationToken);

        //Assert
        session.UsesRawFraming.Should().BeFalse();
        logs.Stdout.Should().Be("to-stdout\nmore\n");
        logs.Stderr.Should().Be("to-stderr\n");
    }

    [Theory]
    [InlineData(HijackedStream.RawStreamContentType)]
    [InlineData("")]
    public async Task ReadToEndAsync_WithTty_PassesTheBytesThroughVerbatim(string contentType)
    {
        //Arrange
        var wire = Encoding.UTF8.GetBytes("$ echo hi\r\nhi\r\n");
        await using var session = CreateSession(wire, contentType, tty: true);

        //Act
        var logs = await session.ReadToEndAsync(TestContext.Current.CancellationToken);

        //Assert
        session.UsesRawFraming.Should().BeTrue();
        logs.Stdout.Should().Be("$ echo hi\r\nhi\r\n");
        logs.Stderr.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(8)]
    [InlineData(13)]
    [InlineData(100)]
    public async Task ReadAsync_WithoutTty_KeepsEveryLineInOrderWhateverPartArrivedWithTheHeaders(int prefixLength)
    {
        //Arrange
        var expectedOut = new List<string>();
        var expectedErr = new List<string>();
        var frames = new List<(byte, string)>();
        for (var i = 0; i < 2000; i++)
        {
            var line = $"{{\"line\":{i},\"pad\":\"{new string('x', 200)}\"}}\n";
            expectedOut.Add(line);
            frames.Add((StdOut, line));
            if (i % 3 == 0)
            {
                expectedErr.Add($"err {i}\n");
                frames.Add((StdErr, $"err {i}\n"));
            }
        }

        var wire = Frames(frames.ToArray());
        await using var session = CreateSession(wire, HijackedStream.RawStreamContentType, tty: false, prefixLength);

        //Act
        var (stdout, stderr) = await ReadAllAsync(session, bufferSize: 1000);

        //Assert
        stdout.Should().Be(string.Concat(expectedOut));
        stderr.Should().Be(string.Concat(expectedErr));
    }

    [Fact]
    public async Task ReadAsync_WithoutTty_ReassemblesLinesLongerThanTheReadBuffer()
    {
        //Arrange
        var longLine = new string('y', 64 * 1024) + "\n";
        var wire = Frames((StdOut, longLine), (StdErr, "between\n"), (StdOut, longLine));
        await using var session = CreateSession(wire, HijackedStream.RawStreamContentType, tty: false, prefixLength: 20);

        //Act
        var (stdout, stderr) = await ReadAllAsync(session, bufferSize: 16 * 1024);

        //Assert
        stdout.Should().Be(longLine + longLine);
        stderr.Should().Be("between\n");
    }

    private static ContainerExecStream CreateSession(byte[] wire, string contentType, bool tty, int prefixLength = 0)
    {
        // The first prefixLength bytes play the part of the body bytes that arrived in the same read
        // as the response headers; the rest is still "on the socket".
        prefixLength = Math.Min(prefixLength, wire.Length);
        var headers = Encoding.ASCII.GetBytes("HTTP/1.1 101 UPGRADED\r\n\r\n");
        var prefix = headers.Concat(wire.Take(prefixLength)).ToArray();
        var socket = new MemoryStream(wire, prefixLength, wire.Length - prefixLength, writable: false);
        var transport = new HijackedStream(socket, contentType, prefix, headers.Length, prefix.Length);
        return new ContainerExecStream(null, "exec-id", transport, tty);
    }

    private static async Task<(string Stdout, string Stderr)> ReadAllAsync(ContainerExecStream session, int bufferSize)
    {
        var stdout = new MemoryStream();
        var stderr = new MemoryStream();
        var buffer = new byte[bufferSize];
        while (true)
        {
            var read = await session.ReadAsync(buffer, TestContext.Current.CancellationToken);
            if (read.EndOfStream)
            {
                break;
            }

            (read.Target == ExecStreamTarget.StandardError ? stderr : stdout).Write(buffer, 0, read.Count);
        }

        return (Encoding.UTF8.GetString(stdout.ToArray()), Encoding.UTF8.GetString(stderr.ToArray()));
    }

    private static byte[] Frames(params (byte Stream, string Text)[] frames)
    {
        var wire = new MemoryStream();
        foreach (var (stream, text) in frames)
        {
            var payload = Encoding.UTF8.GetBytes(text);
            wire.WriteByte(stream);
            wire.Write([0, 0, 0]);
            wire.Write([(byte)(payload.Length >> 24), (byte)(payload.Length >> 16), (byte)(payload.Length >> 8), (byte)payload.Length]);
            wire.Write(payload);
        }

        return wire.ToArray();
    }
}
