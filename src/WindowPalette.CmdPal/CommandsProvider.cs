// Copyright (c) Microsoft Corporation
// Licensed under the MIT license. See THIRD-PARTY-NOTICES.md in the repository root.

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using WindowPalette.CmdPal.Pages;
using WindowPalette.CmdPal.Settings;
using WindowPalette.Windows;

namespace WindowPalette.CmdPal;

public sealed partial class CommandsProvider : CommandProvider
{
    private readonly ICommandItem[] _commands;

    public CommandsProvider()
    {
#if PANESAIL_STORE
        Id = "PaneSail";
#else
        Id = "WindowPalette";
#endif
        DisplayName = "PaneSail";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        var controller = new FeatureController();
        var settings = new FeatureSettings(controller);
        Settings = settings;
        _commands =
        [
            new CommandItem(new DemoPage(controller, settings))
            {
                Title = DisplayName,
                Subtitle = "Resize windows when you drag them between monitors.",
            },
        ];
    }

    public override ICommandItem[] TopLevelCommands() => _commands;
}
