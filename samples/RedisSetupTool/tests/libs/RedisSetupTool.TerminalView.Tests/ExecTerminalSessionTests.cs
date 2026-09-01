using System;
using System.Text;
using System.Threading.Tasks;
using RedisSetupTool.TerminalView.Tests.Fakes;
using SilverAssertions;
using Xunit;

namespace RedisSetupTool.TerminalView.Tests;

/// <summary>Covers the pump between an exec session and a terminal.</summary>
public class ExecTerminalSessionTests
{
    /// <summary>Bytes reach the sink verbatim, in order, with the right lengths.</summary>
    [Fact]
    public async Task Pump_WhenBytesArrive_FeedsThemVerbatimInOrder()
    {
        //Arrange
        var session = new FakeExecSession();
        var sink = new RecordingTerminalSink();
        await using var pump = new ExecTerminalSession(session, sink);
        session.Emit(1, 2, 3);
        session.Emit(4, 5);
        session.EndOfStream();

        //Act
        pump.Start();
        await WaitForAsync(() => pump.State == TerminalSessionState.Exited);

        //Assert
        sink.Chunks.Count.Should().Be(2);
        sink.Chunks[0].Should().BeEquivalentTo(new byte[] { 1, 2, 3 });
        sink.Chunks[1].Should().BeEquivalentTo(new byte[] { 4, 5 });
    }

    /// <summary>A read that splits a multi-byte sequence still produces the right text.</summary>
    [Fact]
    public async Task Pump_WhenAReadSplitsAUtf8Sequence_NeverDecodesAPartialRead()
    {
        //Arrange
        var text = "café über";
        var bytes = Encoding.UTF8.GetBytes(text);
        var session = new FakeExecSession();
        var sink = new RecordingTerminalSink();
        await using var pump = new ExecTerminalSession(session, sink);

        //The split lands in the middle of the two-byte sequence for the accented letter.
        session.Emit(bytes[..4]);
        session.Emit(bytes[4..]);
        session.EndOfStream();

        //Act
        pump.Start();
        await WaitForAsync(() => pump.State == TerminalSessionState.Exited);

        //Assert
        sink.Chunks.Count.Should().Be(2);
        sink.DecodedText.Should().Be(text);
    }

    /// <summary>Keystrokes go straight through to the session.</summary>
    [Fact]
    public async Task OnInput_WhenCalled_ForwardsToTheSession()
    {
        //Arrange
        var session = new FakeExecSession();
        await using var pump = new ExecTerminalSession(session, new RecordingTerminalSink());

        //Act
        pump.OnInput("ls -al\n");
        await WaitForAsync(() => session.Sent.Count == 1);

        //Assert
        session.Sent[0].Should().Be("ls -al\n");
    }

    /// <summary>The control reports (columns, rows); the daemon takes (rows, columns).</summary>
    [Fact]
    public async Task OnGridResized_WhenCalled_SwapsTheArgumentsForTheDaemon()
    {
        //Arrange
        var session = new FakeExecSession();
        await using var pump = new ExecTerminalSession(session, new RecordingTerminalSink(),
            new ExecTerminalSessionOptions { ResizeDebounceMs = 10 });

        //Act
        pump.OnGridResized(120, 40);
        await WaitForAsync(() => session.Resizes.Count == 1);

        //Assert
        session.Resizes[0].Rows.Should().Be(40);
        session.Resizes[0].Columns.Should().Be(120);
        pump.Columns.Should().Be(120);
        pump.Rows.Should().Be(40);
    }

    /// <summary>A burst of resizes collapses into one round trip.</summary>
    [Fact]
    public async Task OnGridResized_WhenCalledRepeatedly_IsDebounced()
    {
        //Arrange
        var session = new FakeExecSession();
        await using var pump = new ExecTerminalSession(session, new RecordingTerminalSink(),
            new ExecTerminalSessionOptions { ResizeDebounceMs = 80 });

        //Act
        for (var columns = 100; columns < 105; columns++)
        {
            pump.OnGridResized(columns, 30);
        }

        await WaitForAsync(() => session.Resizes.Count >= 1);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        //Assert
        session.Resizes.Count.Should().Be(1);
        session.Resizes[0].Columns.Should().Be(104);
    }

    /// <summary>End of stream writes the exit banner and moves the session to Exited.</summary>
    [Fact]
    public async Task Pump_WhenTheShellExits_FeedsTheBannerAndReportsTheCode()
    {
        //Arrange
        var session = new FakeExecSession { ExitCode = 7 };
        var sink = new RecordingTerminalSink();
        await using var pump = new ExecTerminalSession(session, sink);
        session.EndOfStream();

        //Act
        pump.Start();
        await WaitForAsync(() => pump.State == TerminalSessionState.Exited);

        //Assert
        pump.ExitCode.Should().Be(7);
        sink.Texts.Count.Should().Be(1);
        sink.Texts[0].Should().Contain("process exited with code 7");
    }

    /// <summary>A pump failure is reported rather than thrown.</summary>
    [Fact]
    public async Task Pump_WhenReadingThrows_ReportsFailureAndFeedsTheBanner()
    {
        //Arrange
        var session = new FakeExecSession { ReadFailure = new InvalidOperationException("boom") };
        var sink = new RecordingTerminalSink();
        await using var pump = new ExecTerminalSession(session, sink);

        //Act
        pump.Start();
        await WaitForAsync(() => pump.State == TerminalSessionState.Failed);

        //Assert
        sink.Texts[0].Should().Contain("session failed: boom");
    }

    /// <summary>Disposal cancels the pump, disposes the session, and can be repeated.</summary>
    [Fact]
    public async Task DisposeAsync_WhenCalledTwice_IsIdempotent()
    {
        //Arrange
        var session = new FakeExecSession();
        var pump = new ExecTerminalSession(session, new RecordingTerminalSink());
        pump.Start();

        //Act
        await pump.DisposeAsync();
        await pump.DisposeAsync();

        //Assert
        session.Disposed.Should().Be(true);
    }

    /// <summary>Starting twice starts one pump.</summary>
    [Fact]
    public async Task Start_WhenCalledTwice_StartsOnlyOnePump()
    {
        //Arrange
        var session = new FakeExecSession();
        var sink = new RecordingTerminalSink();
        await using var pump = new ExecTerminalSession(session, sink);
        session.Emit(9);
        session.EndOfStream();

        //Act
        pump.Start();
        pump.Start();
        await WaitForAsync(() => pump.State == TerminalSessionState.Exited);

        //Assert
        sink.Chunks.Count.Should().Be(1);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("The condition did not hold within five seconds.");
    }
}
