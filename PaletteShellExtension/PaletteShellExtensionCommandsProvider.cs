// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using PaletteShellExtension.Classes;
using PaletteShellExtension.Pages;
using System;
using System.Collections.Generic;

namespace PaletteShellExtension;

public partial class PaletteShellExtensionCommandsProvider : CommandProvider
{
    private readonly CommandItem? _scriptsItem;
    private readonly AgentSessionService? _sessions;
    private readonly AgentSessionsPage? _sessionsPage;
    private readonly object _refreshGate = new();
    private ICommandItem[] _commands;

    // What the current top-level list was built from, so routine state changes (a recent
    // folder, a new favorite) don't make the host re-fetch an unchanged list.
    private string _commandsSignature = "";

    public PaletteShellExtensionCommandsProvider()
    {
        // This constructor runs during COM activation; an exception escaping it kills the
        // extension process on every launch. The page/settings constructors guard their own
        // failure modes, but this is the backstop: whatever happens, hand the host a valid
        // provider — worst case a single entry that says why nothing else is here.
        DisplayName = "PaletteShell";

        // The top-level list changes at runtime (agents are added, removed, or hidden from search),
        // so don't let the host treat it as fixed.
        Frozen = false;
        try
        {
            Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
            Settings = PaletteShellSettingsManager.Instance.Settings;

            // Agent sessions are independent of the scripts folder, so a failure in one still
            // leaves the other usable.
            try
            {
                _sessions = AgentSessionService.Instance;
                _sessionsPage = new AgentSessionsPage(_sessions);
            }
            catch (Exception ex)
            {
                Log.Error("Agent sessions failed to initialize", ex);
                _sessions = null;
                _sessionsPage = null;
            }

            _scriptsItem = new CommandItem(new PaletteShellExtensionPage(_sessionsPage)) { Title = DisplayName, Subtitle = "Run your PowerShell scripts" };
            _commands = BuildCommands(out _commandsSignature);

            if (_sessions is not null)
            {
                _sessions.Changed += (_, _) => RefreshCommands();
            }
        }
        catch (Exception ex)
        {
            Log.Error("PaletteShell provider failed to initialize", ex);
            _commands = [
                new CommandItem(new NoOpCommand())
                {
                    Title = DisplayName,
                    Subtitle = "PaletteShell failed to load — check logs in %LOCALAPPDATA%\\PaletteShell\\logs",
                },
            ];
        }
    }

    public override ICommandItem[] TopLevelCommands()
    {
        return _commands;
    }

    // "Agent sessions", then (unless turned off in Settings) one entry per agent so typing an
    // agent's name in Command Palette goes straight to its folder picker, then PaletteShell.
    private ICommandItem[] BuildCommands(out string signature)
    {
        var commands = new List<ICommandItem>();
        var keys = new List<string>();

        if (_sessions is not null && _sessionsPage is not null)
        {
            commands.Add(new CommandItem(_sessionsPage)
            {
                Title = "Agent sessions",
                Subtitle = "Start a coding agent CLI in a folder",
            });

            if (_sessions.ShowAgentsInRoot)
            {
                var agents = _sessions.Agents;
                SessionFolderPickerPage.Prune(agents);
                foreach (var agent in agents)
                {
                    keys.Add(agent.Key);
                    commands.Add(new CommandItem(SessionFolderPickerPage.For(_sessions, agent))
                    {
                        Title = agent.Name,
                        Subtitle = $"Start an agent session · {agent.CommandLine}",
                        Icon = SessionIcons.Agent,
                        MoreCommands = [.. SessionFolderItems.QuickStarts(_sessions, agent)],
                    });
                }

                // Quick starts list the default folder and favorites, so they're part of what
                // the list was built from too.
                keys.Add(_sessions.DefaultFolder);
                keys.AddRange(_sessions.Favorites);
            }
        }

        if (_scriptsItem is not null)
        {
            commands.Add(_scriptsItem);
        }

        signature = string.Join('\n', keys);
        return [.. commands];
    }

    private void RefreshCommands()
    {
        try
        {
            // Changes can be announced from several threads at once (a palette command and a
            // background refresh); serialize so the list and its signature stay paired.
            lock (_refreshGate)
            {
                var commands = BuildCommands(out var signature);
                if (signature == _commandsSignature)
                {
                    return;
                }

                _commands = commands;
                _commandsSignature = signature;
            }

            RaiseItemsChanged();
        }
        catch (Exception ex)
        {
            Log.Error("Failed to refresh top-level commands", ex);
        }
    }
}
