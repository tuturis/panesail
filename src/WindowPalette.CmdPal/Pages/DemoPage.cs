// Copyright (c) Microsoft Corporation
// Licensed under the MIT license. See THIRD-PARTY-NOTICES.md in the repository root.

using System;
using System.Collections.Generic;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using WindowPalette.CmdPal.Commands;
using WindowPalette.CmdPal.Settings;
using WindowPalette.Windows;

namespace WindowPalette.CmdPal.Pages;

internal sealed partial class DemoPage : ListPage
{
    private readonly FeatureController _controller;
    private readonly FeatureSettings _settings;

    public DemoPage(FeatureController controller, FeatureSettings settings)
    {
        _controller = controller;
        _settings = settings;
        _settings.Changed += Refresh;
        Id = "window-palette.home";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        Title = "PaneSail";
        Name = "Open";
    }

    public override IListItem[] GetItems()
    {
        var items = new List<IListItem>();
        try
        {
            var status = _controller.GetStatusAsync().GetAwaiter().GetResult();
            items.Add(new ListItem(new NoOpCommand())
            {
                Title = status.DisplayStatus,
                Subtitle = status.Error ?? "Drag a window by its title bar to another monitor, then release it. Shift-drag is left to FancyZones.",
            });

            if (!status.WorkerRunning)
            {
                items.Add(new ListItem(new SetEnabledCommand(_controller, true, Refresh))
                {
                    Title = status.Settings.Enabled ? "Start automatic resizing" : "Enable automatic resizing",
                    Subtitle = "Keep resizing active when Command Palette closes.",
                });
            }

            if (status.Settings.Enabled || status.WorkerRunning)
            {
                items.Add(new ListItem(new SetEnabledCommand(_controller, false, Refresh))
                {
                    Title = "Disable automatic resizing",
                    Subtitle = "Stop watching window drags and leave windows as they are.",
                });
            }

            items.Add(new ListItem(new NoOpCommand())
            {
                Title = FeatureSettings.ModeLabel(status.Settings.Mode),
                Subtitle = FeatureSettings.StartupDescription(status.StartupState),
            });
            if (!string.IsNullOrWhiteSpace(status.LastActivity))
            {
                items.Add(new ListItem(new NoOpCommand())
                {
                    Title = "Last activity",
                    Subtitle = status.LastActivity,
                });
            }
        }
        catch (Exception)
        {
            items.Add(new ListItem(new NoOpCommand())
            {
                Title = "Unable to read automatic resizing status",
                Subtitle = "Try Refresh status. Settings and windows have not been changed.",
            });
        }

        items.Add(new ListItem(_settings.SettingsPage)
        {
            Title = "Settings",
            Subtitle = "Choose how windows resize and whether to start with Windows.",
        });
        items.Add(new ListItem(new RefreshStatusCommand(Refresh))
        {
            Title = "Refresh status",
            Subtitle = "Check the background component and Windows startup state again.",
        });
        items.Add(new ListItem(new RunDemoCommand())
        {
            Title = "Run demo",
            Subtitle = "Check that the Command Palette extension is responding.",
        });
        return items.ToArray();
    }

    private void Refresh() => RaiseItemsChanged();
}
