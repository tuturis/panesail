using System;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace WindowPalette.CmdPal.Commands;

internal sealed partial class RefreshStatusCommand : InvokableCommand
{
    private readonly Action _refresh;

    public RefreshStatusCommand(Action refresh)
    {
        _refresh = refresh;
        Id = "window-palette.refresh";
        Name = "Refresh";
    }

    public override ICommandResult Invoke()
    {
        _refresh();
        return CommandResult.KeepOpen();
    }
}
