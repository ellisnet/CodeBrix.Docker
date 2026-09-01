using CodeBrix.Platform.Simple;
using Microsoft.UI.Xaml.Media;
using RedisSetupTool.DockerManagement.Models;
using RedisSetupTool.RedisManagement.Results;
using RedisSetupTool.Services;
using System;

// ReSharper disable once CheckNamespace
namespace RedisSetupTool.ViewModels;

/// <summary>One line of a Verify result: a tick or a cross, the check's name and its detail.</summary>
[Microsoft.UI.Xaml.Data.Bindable]
public sealed class VerificationCheckViewModel
{
    /// <summary>Wraps one check from the Redis probe.</summary>
    /// <param name="check">The check to show.</param>
    public VerificationCheckViewModel(RedisVerificationCheck check)
    {
        Name = check.Name;
        Detail = Formatting.OrDash(check.Detail);
        Passed = check.Passed;
    }

    /// <summary>The check's name.</summary>
    public string Name { get; }

    /// <summary>What the check saw.</summary>
    public string Detail { get; }

    /// <summary>Whether the check passed.</summary>
    public bool Passed { get; }

    /// <summary>A tick for a pass, a cross for a failure.</summary>
    public string Glyph => Passed ? "" : "";

    /// <summary>Green for a pass, red for a failure.</summary>
    public Brush Mark => Passed ? Palette.Good : Palette.Bad;
}

/// <summary>One advisor finding: a severity chip, the rule id, and what to do about it.</summary>
[Microsoft.UI.Xaml.Data.Bindable]
public class AdvisorFindingRowViewModel : SimpleViewModel
{
    private readonly Action _open;

    /// <summary>Wraps one advisor finding.</summary>
    /// <param name="finding">The finding to show.</param>
    /// <param name="open">What clicking the row does, or null when it does nothing.</param>
    public AdvisorFindingRowViewModel(AdvisorFindingInfo finding, Action open = null)
    {
        RuleId = finding.RuleId;
        Severity = finding.Severity;
        ContainerName = Formatting.OrDash(finding.ContainerName);
        Title = finding.Title;
        Detail = Formatting.OrDash(finding.Detail);
        Recommendation = Formatting.OrDash(finding.Recommendation);
        _open = open;
    }

    /// <summary>The advisor rule's identifier, for example <c>CB007</c>.</summary>
    public string RuleId { get; }

    /// <summary>How serious the finding is.</summary>
    public AdvisorLevel Severity { get; }

    /// <summary>The severity chip's caption.</summary>
    public string SeverityText => Severity.ToString().ToUpperInvariant();

    /// <summary>The severity chip's colour.</summary>
    public Brush SeverityBrush => Severity switch
    {
        AdvisorLevel.Critical => Palette.Bad,
        AdvisorLevel.Warning => Palette.Warn,
        _ => Palette.TextTertiary,
    };

    /// <summary>The container the finding is about.</summary>
    public string ContainerName { get; }

    /// <summary>The finding's one-line title.</summary>
    public string Title { get; }

    /// <summary>What the advisor saw.</summary>
    public string Detail { get; }

    /// <summary>What the advisor suggests doing.</summary>
    public string Recommendation { get; }

    /// <summary>Shows the container this finding is about.</summary>
    public SimpleCommand OpenCommand => field ??= new SimpleCommand(() => _open?.Invoke());
}

/// <summary>One line of the daemon's live event stream.</summary>
[Microsoft.UI.Xaml.Data.Bindable]
public sealed class EventRowViewModel
{
    /// <summary>Wraps one daemon event.</summary>
    /// <param name="daemonEvent">The event to show.</param>
    public EventRowViewModel(DaemonEvent daemonEvent)
    {
        Time = Formatting.Clock(daemonEvent.Timestamp);
        Type = Formatting.OrDash(daemonEvent.Type);
        Action = Formatting.OrDash(daemonEvent.Action);
        Subject = string.IsNullOrWhiteSpace(daemonEvent.ActorName)
            ? Formatting.Trim(Formatting.OrDash(daemonEvent.ActorId), 12)
            : daemonEvent.ActorName;
    }

    /// <summary>When the event happened, as a local wall-clock time.</summary>
    public string Time { get; }

    /// <summary>The object kind the event is about: container, image, network, volume.</summary>
    public string Type { get; }

    /// <summary>What happened: create, start, die, destroy.</summary>
    public string Action { get; }

    /// <summary>The object's name, or a short id when it has none.</summary>
    public string Subject { get; }
}

/// <summary>One row of the disk-usage card: a label, a share of the total, and a size.</summary>
[Microsoft.UI.Xaml.Data.Bindable]
public sealed class DiskUsageRowViewModel
{
    /// <summary>Creates a disk-usage row.</summary>
    /// <param name="label">What the row measures.</param>
    /// <param name="bytes">How many bytes it holds.</param>
    /// <param name="totalBytes">The total the share is measured against.</param>
    /// <param name="reclaimableText">What could be reclaimed, or null.</param>
    public DiskUsageRowViewModel(string label, long bytes, long totalBytes,
        string reclaimableText = null)
    {
        Label = label;
        SizeText = Formatting.Bytes(bytes);
        Share = totalBytes > 0 ? Math.Min(100d, bytes * 100d / totalBytes) : 0d;
        ReclaimableText = reclaimableText ?? string.Empty;
    }

    /// <summary>What the row measures.</summary>
    public string Label { get; }

    /// <summary>The formatted size.</summary>
    public string SizeText { get; }

    /// <summary>The share of the total, from 0 to 100, for the row's progress bar.</summary>
    public double Share { get; }

    /// <summary>What could be reclaimed, or an empty string.</summary>
    public string ReclaimableText { get; }
}
