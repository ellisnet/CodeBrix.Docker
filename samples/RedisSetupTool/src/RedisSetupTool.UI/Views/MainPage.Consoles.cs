using CodeBrix.Platform.UI.TerminalView;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RedisSetupTool.DockerManagement.Exec;
using RedisSetupTool.TerminalView;
using RedisSetupTool.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace RedisSetupTool.Views;

/// <summary>
/// The Consoles section's code-behind. <c>TerminalControl</c> declares no dependency properties,
/// so it cannot be bound, styled or placed inside a <c>DataTemplate</c>; every console tab is
/// therefore built here, mirroring the view model's <c>Tabs</c> collection into the
/// <c>TabView</c>.
/// </summary>
public sealed partial class MainPage
{
    private sealed class ConsoleHost
    {
        public TabViewItem Tab { get; init; }

        public TerminalControl Terminal { get; init; }

        public ConsoleTabViewModel Model { get; init; }

        public Grid Body { get; init; }

        public ExecTerminalSession Pump { get; set; }

        public IExecSession Session { get; set; }
    }

    //Exactly one console body is visible at a time; the others stay in the tree, collapsed, so
    //  their terminals remain loaded and keep receiving their sessions' output.
    private void ShowConsoleBody(ConsoleHost selected)
    {
        foreach (var pair in _consoleHosts)
        {
            pair.Value.Body.Visibility = ReferenceEquals(pair.Value, selected)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private readonly Dictionary<ConsoleTabViewModel, ConsoleHost> _consoleHosts = [];
    private ConsolesViewModel _consoles;

    private void AttachConsoles(MainViewModel viewModel)
    {
        if (viewModel is null || _consoles is not null) { return; }

        _consoles = viewModel.Consoles;
        _consoles.Tabs.CollectionChanged += ConsoleTabs_ModelCollectionChanged;
        _consoles.ReopenRequested += ReopenConsole;
        _consoles.SendInput = (model, text) =>
        {
            if (model is not null && _consoleHosts.TryGetValue(model, out var host))
            {
                host.Pump?.OnInput(text);
            }
        };
    }

    private void ConsoleTabs_ModelCollectionChanged(object sender,
        NotifyCollectionChangedEventArgs args)
    {
        if (args.NewItems is not null)
        {
            foreach (var added in args.NewItems)
            {
                if (added is ConsoleTabViewModel model) { AddConsoleTab(model); }
            }
        }

        if (args.OldItems is not null)
        {
            foreach (var removed in args.OldItems)
            {
                if (removed is ConsoleTabViewModel model) { RemoveConsoleTab(model); }
            }
        }
    }

    private void AddConsoleTab(ConsoleTabViewModel model)
    {
        var options = new ExecTerminalSessionOptions
        {
            Palette = TerminalPalette.Dark,
            Scrollback = 5000,
            FontSize = 13f,
        };

        //Scrollback has to be set before the control loads, which is what the factory is for.
        var terminal = TerminalSessionFactory.CreateControl(options);

        var status = new ContentControl
        {
            Content = model,
            ContentTemplate = (DataTemplate)Resources["ConsoleStatusTemplate"],
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        //TerminalControl's grid follows the control's pixel size, so it must land in a bounded
        //  cell; the body stretches into ConsoleBodyHost's star row and gives it one.
        var body = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        terminal.HorizontalAlignment = HorizontalAlignment.Stretch;
        terminal.VerticalAlignment = VerticalAlignment.Stretch;
        Grid.SetRow(status, 0);
        Grid.SetRow(terminal, 1);
        body.Children.Add(status);
        body.Children.Add(terminal);

        //The tab carries no content: a tab's content presenter measures its child against an
        //  unbounded height, which leaves a star row inside it collapsed and the terminal a few
        //  rows tall. The body goes into ConsoleBodyHost instead, a real star cell in the page's
        //  own grid, and the tab strip only selects which body is showing.
        var tab = new TabViewItem { Header = model.ContainerName };

        var host = new ConsoleHost { Tab = tab, Terminal = terminal, Model = model, Body = body };
        _consoleHosts[model] = host;

        ConsoleBodyHost.Children.Add(body);

        //The control drops anything fed to it before Loaded, so the exec is opened and the pump
        //  started there - which is also what makes a tab created while this section is hidden
        //  work: Loaded arrives when the section becomes visible.
        terminal.Loaded += (_, _) => _ = StartConsoleAsync(host, options);

        ConsoleTabs.TabItems.Add(tab);
        ConsoleTabs.SelectedItem = tab;
        ShowConsoleBody(host);
    }

    private async System.Threading.Tasks.Task StartConsoleAsync(ConsoleHost host,
        ExecTerminalSessionOptions options)
    {
        if (host.Pump is not null) { return; }

        try
        {
            var probe = await _consoles.ProbeAsync(host.Model.ContainerId);
            if (!probe.Found)
            {
                host.Model.ApplyFailure(probe.Message ?? "No usable shell in this image.");
                host.Terminal.Feed("\r\n\x1b[31m" + (probe.Message ?? "No usable shell.")
                    + "\x1b[0m\r\n");
                return;
            }

            host.Model.ApplyShell(probe.ShellPath);

            var session = await _consoles.Docker.OpenShellAsync(host.Model.ContainerId,
                new ExecSessionOptions
                {
                    Rows = host.Terminal.Rows,
                    Columns = host.Terminal.Columns,
                });
            host.Session = session;

            var pump = TerminalSessionFactory.Attach(session, host.Terminal, options);
            host.Pump = pump;
            host.Model.ApplyGrid(host.Terminal.Columns, host.Terminal.Rows);

            //StateChanged and GridChanged arrive on a worker thread, so marshal them.
            pump.StateChanged += state => DispatcherQueue?.TryEnqueue(() =>
            {
                host.Model.ApplyState(state, pump.ExitCode);
                host.Tab.Header = state == TerminalSessionState.Running
                    ? host.Model.ContainerName
                    : host.Model.ContainerName + " · " + host.Model.StateText;
            });
            pump.GridChanged += (columns, rows) => DispatcherQueue?.TryEnqueue(
                () => host.Model.ApplyGrid(columns, rows));

            pump.Start();
            host.Terminal.GrabFocus();
        }
        catch (NoShellAvailableException exception)
        {
            host.Model.ApplyFailure(exception.Message);
            host.Terminal.Feed("\r\n\x1b[31m" + exception.Message + "\x1b[0m\r\n");
        }
        catch (Exception exception)
        {
            host.Model.ApplyFailure(exception.Message);
            host.Terminal.Feed("\r\n\x1b[31m" + exception.Message + "\x1b[0m\r\n");
        }
    }

    private void RemoveConsoleTab(ConsoleTabViewModel model)
    {
        if (!_consoleHosts.TryGetValue(model, out var host)) { return; }

        _consoleHosts.Remove(model);
        ConsoleTabs.TabItems.Remove(host.Tab);
        ConsoleBodyHost.Children.Remove(host.Body);

        if (host.Pump is not null)
        {
            TerminalSessionFactory.Detach(host.Pump, host.Terminal);
            _ = host.Pump.DisposeAsync();
        }
        else
        {
            host.Session?.Dispose();
        }
    }

    private void ReopenConsole(ConsoleTabViewModel model)
    {
        if (model is null) { return; }

        var containerId = model.ContainerId;
        var containerName = model.ContainerName;
        _consoles.CloseTab(model);
        _consoles.OpenConsole(containerId, containerName);
    }

    private void ConsoleTabs_TabCloseRequested(TabView sender,
        TabViewTabCloseRequestedEventArgs args)
    {
        foreach (var pair in _consoleHosts)
        {
            if (ReferenceEquals(pair.Value.Tab, args.Tab))
            {
                _consoles.CloseTab(pair.Key);
                return;
            }
        }
    }

    private void ConsoleTabs_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (ConsoleTabs.SelectedItem is not TabViewItem tab) { return; }

        //Show the selected tab's body and give its terminal the keyboard, so typing goes where
        //  the user is looking.
        foreach (var pair in _consoleHosts)
        {
            if (ReferenceEquals(pair.Value.Tab, tab))
            {
                ShowConsoleBody(pair.Value);
                pair.Value.Terminal.GrabFocus();
                return;
            }
        }
    }
}
