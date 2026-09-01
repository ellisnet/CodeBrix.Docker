using RedisSetupTool.ViewModels;
using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace RedisSetupTool.Services;

/// <summary>
/// A test hook, off unless it is asked for. Setting <c>REDISSETUP_AUTOMATION</c> makes the shell
/// drive one scripted round trip through its own commands after the first refresh, writing every
/// step to <c>REDISSETUP_AUTOMATION_LOG</c> (or standard output). It exists because the
/// verification pass on the Linux X11 head has to prove a real instance was created, verified and
/// torn down through the application's code path, and synthetic clicks on this family's toolbar
/// widgets are not dependable.
/// </summary>
/// <remarks>
/// Recognised scripts: <c>a1-roundtrip</c> (create an A1 instance, verify it, destroy it),
/// <c>tour</c> (visit every section), and <c>a1-roundtrip+tour</c> for both.
/// </remarks>
public sealed class StartupAutomation
{
    /// <summary>The environment variable that names the script to run.</summary>
    public const string ScriptVariable = "REDISSETUP_AUTOMATION";

    /// <summary>The environment variable that names the log file, when one is wanted.</summary>
    public const string LogVariable = "REDISSETUP_AUTOMATION_LOG";

    private readonly MainViewModel _shell;
    private readonly string _script;
    private readonly string _logPath;
    private readonly object _gate = new();

    /// <summary>Creates the hook over the shell it drives.</summary>
    /// <param name="shell">The shell whose commands the script uses.</param>
    public StartupAutomation(MainViewModel shell)
    {
        _shell = shell;
        _script = (Environment.GetEnvironmentVariable(ScriptVariable) ?? string.Empty).Trim();
        _logPath = Environment.GetEnvironmentVariable(LogVariable);
    }

    /// <summary>Whether a script was asked for.</summary>
    public bool IsEnabled => !string.IsNullOrEmpty(_script);

    /// <summary>Writes one line to the automation log, when a script is running.</summary>
    /// <param name="line">The line to write.</param>
    public void Log(string line)
    {
        if (!IsEnabled || string.IsNullOrEmpty(line)) { return; }

        var stamped = "[AUTOMATION " + DateTimeOffset.Now.ToString("HH:mm:ss",
            CultureInfo.InvariantCulture) + "] " + line;
        Console.WriteLine(stamped);
        if (string.IsNullOrEmpty(_logPath)) { return; }

        try
        {
            lock (_gate)
            {
                File.AppendAllText(_logPath, stamped + Environment.NewLine);
            }
        }
        catch (Exception)
        {
            //A log that cannot be written must never take the application down.
        }
    }

    /// <summary>Runs the script, if one was asked for.</summary>
    /// <returns>A task that completes when the script has finished.</returns>
    public async Task RunAsync()
    {
        if (!IsEnabled) { return; }

        Log("script=" + _script);
        try
        {
            if (_script.Contains("tour", StringComparison.OrdinalIgnoreCase))
            {
                await TourAsync().ConfigureAwait(true);
            }
            if (_script.Contains("a1-roundtrip", StringComparison.OrdinalIgnoreCase))
            {
                await RoundTripAsync().ConfigureAwait(true);
            }
            if (_script.StartsWith("demo", StringComparison.OrdinalIgnoreCase))
            {
                await DemoAsync().ConfigureAwait(true);
            }
            Log("script complete");
        }
        catch (Exception exception)
        {
            Log("script FAILED: " + exception);
        }
    }

    private async Task TourAsync()
    {
        Log("tour: begin");
        foreach (var section in Enum.GetValues<SectionKey>())
        {
            _shell.Navigate(section);
            await Task.Delay(700).ConfigureAwait(true);
            Log("tour: " + section.ToString() + " visible="
                + (SectionVisible(section) ? "yes" : "no"));
        }
        _shell.Navigate(SectionKey.Dashboard);
        Log("tour: end");
    }

    private async Task RoundTripAsync()
    {
        Log("roundtrip: begin");

        _shell.Navigate(SectionKey.CreateInstance);
        await Task.Delay(400).ConfigureAwait(true);

        var form = _shell.CreateInstance;
        if (!form.SelectByCode("A1"))
        {
            Log("roundtrip: FAILED - the catalog has no A1");
            return;
        }
        form.InstanceName = "automation-" + DateTimeOffset.Now
            .ToString("HHmmss", CultureInfo.InvariantCulture);
        Log("roundtrip: topology=" + form.SelectedCode + " name=" + form.InstanceName
            + " ports=" + form.PortPlanText + " canCreate=" + form.CanCreate);

        if (!form.CanCreate)
        {
            foreach (var problem in form.ValidationMessages)
            {
                Log("roundtrip: validation - " + problem);
            }
            Log("roundtrip: FAILED - the request did not validate");
            return;
        }

        var name = form.InstanceName;
        form.CreateCommand.Execute(null);
        await WaitAsync(() => !form.IsCreating, TimeSpan.FromMinutes(3)).ConfigureAwait(true);

        var card = FindCard(name);
        if (card is null)
        {
            Log("roundtrip: FAILED - no card appeared for " + name);
            return;
        }

        Log("roundtrip: card state=" + card.StateText + " nodes=" + card.Nodes.Count);
        foreach (var row in card.ConnectionRows)
        {
            Log("roundtrip: connect " + row.Label + " = " + row.Value);
        }

        card.VerifyCommand.Execute(null);
        await WaitAsync(() => !card.IsBusy, TimeSpan.FromMinutes(1)).ConfigureAwait(true);
        Log("roundtrip: verify summary = " + card.VerifySummary);
        foreach (var check in card.VerifyChecks)
        {
            Log("roundtrip: check " + (check.Passed ? "PASS " : "FAIL ") + check.Name + " - "
                + check.Detail);
        }

        await InspectContainersAsync(card).ConfigureAwait(true);
        await OpenConsoleAsync(card).ConfigureAwait(true);

        _shell.Navigate(SectionKey.Instances);
        await Task.Delay(400).ConfigureAwait(true);

        //Destroy through the topology service directly rather than the card's command: the
        //  command asks for confirmation, and an unattended run has nobody to answer it.
        var instanceId = card.InstanceId;
        Log("roundtrip: destroying " + instanceId);
        await _shell.DestroyForAutomationAsync(instanceId).ConfigureAwait(true);

        var stillThere = FindCard(name) is not null;
        Log("roundtrip: after destroy, card present=" + stillThere);
        Log("roundtrip: instances remaining=" + _shell.State.Instances.Count);
        Log("roundtrip: end");
    }

    private async Task DemoAsync()
    {
        //Fills the window with something to look at: one instance of each of three shapes, left
        //  running. Nothing destroys them - use the System section's sweep when you are done.
        Log("demo: begin");
        foreach (var code in new[] { "A1", "A2", "B1" })
        {
            _shell.Navigate(SectionKey.CreateInstance);
            await Task.Delay(300).ConfigureAwait(true);

            var form = _shell.CreateInstance;
            if (!form.SelectByCode(code)) { continue; }

            form.InstanceName = "demo-" + code.ToLowerInvariant();
            if (!form.CanCreate)
            {
                Log("demo: " + code + " did not validate");
                continue;
            }

            form.CreateCommand.Execute(null);
            await WaitAsync(() => !form.IsCreating, TimeSpan.FromMinutes(3)).ConfigureAwait(true);
            Log("demo: " + code + " -> " + form.ProgressHeadline);
        }

        _shell.Navigate(SectionKey.Instances);
        Log("demo: end; instances=" + _shell.State.Instances.Count);
    }

    private async Task InspectContainersAsync(InstanceCardViewModel card)
    {
        _shell.Navigate(SectionKey.Containers);
        await Task.Delay(500).ConfigureAwait(true);

        var containers = _shell.Containers;
        Log("containers: list shows " + containers.Rows.Count + " row(s), snapshot has "
            + _shell.State.Containers.Count);
        var shown = 0;
        foreach (var row in containers.Rows)
        {
            if (shown++ >= 6) { break; }
            Log("containers: row " + row.Name + "  image=" + row.Image + "  running="
                + row.IsRunning + "  managed=" + row.IsManaged + "  ports=" + row.PortsText);
        }

        if (card.Nodes.Count == 0) { return; }

        var node = card.Nodes[0];
        containers.SelectById(node.ContainerId);
        await WaitAsync(() => containers.Detail.OverviewFacts.Count > 0,
            TimeSpan.FromSeconds(20)).ConfigureAwait(true);
        Log("containers: selected " + containers.Detail.Title + "; overview facts="
            + containers.Detail.OverviewFacts.Count + " networks="
            + containers.Detail.NetworkFacts.Count + " mounts="
            + containers.Detail.MountFacts.Count + " limits="
            + containers.Detail.LimitFacts.Count);

        containers.Detail.SelectTabCommand.Execute("Logs");
        await WaitAsync(() => containers.Detail.LogText.Length > 0, TimeSpan.FromSeconds(20))
            .ConfigureAwait(true);
        Log("containers: log text is " + containers.Detail.LogText.Length + " characters");

        containers.Detail.SelectTabCommand.Execute("Stats");
        await WaitAsync(() => containers.Detail.StatFacts.Count > 0, TimeSpan.FromSeconds(25))
            .ConfigureAwait(true);
        Log("containers: stats cpu=" + containers.Detail.CpuText + " memory="
            + containers.Detail.MemoryText + " samples=" + containers.Detail.CpuHistory.Count);

        containers.Detail.SelectTabCommand.Execute("Diagnostics");
        await WaitAsync(() => containers.Detail.DiagnosticCards.Count > 0,
            TimeSpan.FromSeconds(25)).ConfigureAwait(true);
        Log("containers: diagnostics cards=" + containers.Detail.DiagnosticCards.Count
            + " summary=" + containers.Detail.DiagnosticsSummary);

        containers.Detail.SelectTabCommand.Execute("Advisor");
        await WaitAsync(() => containers.Detail.Findings.Count > 0, TimeSpan.FromSeconds(20))
            .ConfigureAwait(true);
        Log("containers: advisor findings=" + containers.Detail.Findings.Count);
        foreach (var finding in containers.Detail.Findings)
        {
            Log("containers: advisor " + finding.SeverityText + " " + finding.RuleId + " "
                + finding.Title);
        }

        containers.Detail.SelectTabCommand.Execute("Overview");
        await Task.Delay(300).ConfigureAwait(true);
    }

    private async Task OpenConsoleAsync(InstanceCardViewModel card)
    {
        if (card.Nodes.Count == 0) { return; }

        var node = card.Nodes[0];
        _shell.OpenConsole(node.ContainerId, node.ContainerName);
        await Task.Delay(600).ConfigureAwait(true);

        var consoles = _shell.Consoles;
        if (consoles.Tabs.Count == 0)
        {
            Log("console: FAILED - no tab was created");
            return;
        }

        var tab = consoles.Tabs[consoles.Tabs.Count - 1];
        await WaitAsync(() => tab.IsRunning || tab.StateText == "failed",
            TimeSpan.FromSeconds(25)).ConfigureAwait(true);
        Log("console: shell=" + tab.ShellPath + " grid=" + tab.GridText + " state="
            + tab.StateText);

        if (tab.IsRunning)
        {
            //Type a command the way the terminal control would; the shell echoes and answers
            //  in the terminal, which proves the exec stream is live in both directions.
            consoles.SendInput?.Invoke(tab, "redis-cli PING\r");
            await Task.Delay(1500).ConfigureAwait(true);
            Log("console: after input, state=" + tab.StateText);
            consoles.SendInput?.Invoke(tab, "exit\r");
            await Task.Delay(1500).ConfigureAwait(true);
            Log("console: after exit, state=" + tab.StateText);
        }

        tab.CloseCommand.Execute(null);
        await Task.Delay(500).ConfigureAwait(true);
        Log("console: closed; open tabs=" + consoles.Tabs.Count);
    }

    private InstanceCardViewModel FindCard(string instanceName)
    {
        foreach (var card in _shell.Instances.Instances)
        {
            if (string.Equals(card.InstanceName, instanceName, StringComparison.Ordinal))
            {
                return card;
            }
        }
        return null;
    }

    private bool SectionVisible(SectionKey section) => _shell.CurrentSection == section;

    private static async Task WaitAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition()) { return; }
            await Task.Delay(250).ConfigureAwait(true);
        }
    }
}
