using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace WindowPalette.CmdPal.Commands;

internal sealed partial class RunDemoCommand : InvokableCommand
{
    public RunDemoCommand()
    {
        Id = "window-palette.run-demo";
        Name = "Run demo";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
    }

    public override ICommandResult Invoke() => CommandResult.ShowToast("Extension is working.");
}
