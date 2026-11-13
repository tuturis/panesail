using System;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using WindowPalette.Windows;

namespace WindowPalette.CmdPal.Commands;

internal sealed partial class SetEnabledCommand : InvokableCommand
{
    private readonly FeatureController _controller;
    private readonly bool _enabled;
    private readonly Action _refresh;

    public SetEnabledCommand(FeatureController controller, bool enabled, Action refresh)
    {
        _controller = controller;
        _enabled = enabled;
        _refresh = refresh;
        Id = enabled ? "window-palette.enable" : "window-palette.disable";
        Name = enabled ? "Enable" : "Disable";
    }

    public override ICommandResult Invoke()
    {
        try
        {
            var current = _controller.GetStatusAsync().GetAwaiter().GetResult();
            var result = _controller.ApplySettingsAsync(current.Settings with { Enabled = _enabled }).GetAwaiter().GetResult();
            _refresh();
            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                return CommandResult.ShowToast(result.Error);
            }

            if (result.WorkerRunning != _enabled)
            {
                return CommandResult.ShowToast(_enabled
                    ? "Automatic resizing did not start. Open Settings to check its status."
                    : "Automatic resizing has not stopped. Open Settings to check its status.");
            }

            return CommandResult.ShowToast(_enabled ? "Automatic resizing is running." : "Automatic resizing is off.");
        }
        catch (Exception)
        {
            _refresh();
            return CommandResult.ShowToast("The change could not be completed. Refresh status to check automatic resizing.");
        }
    }
}
