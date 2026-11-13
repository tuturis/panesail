using System;
using System.Text.Json;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using WindowPalette.Core;
using WindowPalette.Windows;

namespace WindowPalette.CmdPal.Settings;

internal sealed partial class FeatureSettings : ICommandSettings
{
    private readonly FeatureController _controller;

    public FeatureSettings(FeatureController controller) => _controller = controller;

    public event Action? Changed;

    public IContentPage SettingsPage => new FeatureSettingsPage(_controller, () => Changed?.Invoke());

    internal static string ModeLabel(ResizeMode mode) => mode switch
    {
        ResizeMode.KeepWindowShape => "Keep window shape",
        _ => "Match screen proportions",
    };

    internal static string StartupDescription(string state) => state switch
    {
        "Enabled" => "Windows startup is enabled. Resizing resumes at sign-in while the feature is enabled.",
        "Disabled" => "Windows startup is off.",
        "DisabledByUser" => "Startup is disabled in Windows. To restore it, enable PaneSail in Windows Settings > Apps > Startup.",
        "EnabledByPolicy" => "Startup is enabled by your organization's policy.",
        "DisabledByPolicy" => "Startup is disabled by your organization's policy.",
        _ => "Windows startup status could not be read.",
    };
}

internal sealed partial class FeatureSettingsPage : ContentPage
{
    private readonly FeatureController _controller;
    private readonly Action _changed;

    public FeatureSettingsPage(FeatureController controller, Action changed)
    {
        _controller = controller;
        _changed = changed;
        Id = "window-palette.settings";
        Name = "Settings";
        Title = "PaneSail settings";
        Icon = new IconInfo("\uE713");
    }

    public override IContent[] GetContent() => [new FeatureSettingsForm(_controller, _changed)];
}

// An explicit form keeps persistence and startup failures visible instead of
// returning home immediately after the toolkit's default settings-save event.
internal sealed partial class FeatureSettingsForm : FormContent
{
    private readonly FeatureController _controller;
    private readonly Action _changed;
    private FeatureStatus? _status;

    public FeatureSettingsForm(FeatureController controller, Action changed)
    {
        _controller = controller;
        _changed = changed;
        RefreshStatus();
    }

    public override ICommandResult SubmitForm(string inputs, string data)
    {
        var applyingSettings = false;
        try
        {
            if (!string.IsNullOrWhiteSpace(data))
            {
                using var action = JsonDocument.Parse(data);
                if (action.RootElement.TryGetProperty("action", out var value) && value.GetString() == "refresh")
                {
                    RefreshStatus();
                    _changed();
                    return CommandResult.KeepOpen();
                }
            }

            using var payload = JsonDocument.Parse(inputs);
            var root = payload.RootElement;
            var mode = root.GetProperty("resizeMode").GetString() switch
            {
                "proportions" => ResizeMode.MatchScreenProportions,
                "shape" => ResizeMode.KeepWindowShape,
                _ => throw new FormatException("Unknown resize mode."),
            };
            var settings = new PaletteSettings
            {
                Enabled = ReadToggle(root, "enabled"),
                Mode = mode,
                StartWithWindows = ReadToggle(root, "startup"),
            };

            applyingSettings = true;
            _status = _controller.ApplySettingsAsync(settings).GetAwaiter().GetResult();
            var message = !string.IsNullOrWhiteSpace(_status.Error)
                ? "Some changes could not be applied. " + _status.Error
                : _status.Settings.Enabled && !_status.WorkerRunning
                    ? "Settings are saved, but automatic resizing is not running. Try enabling it again."
                    : !_status.Settings.Enabled && _status.WorkerRunning
                        ? "Settings are saved, but the background component has not stopped. Refresh its status."
                        : "Settings saved. Changes to the resize mode apply to the next drag.";
            Render(message);
            _changed();
        }
        catch (Exception ex) when (!applyingSettings && ex is (JsonException or FormatException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException))
        {
            Render("Check each setting and try Save again.");
        }
        catch (Exception)
        {
            RefreshStatus("The change could not be completed. Current saved settings are shown below.");
            _changed();
        }

        return CommandResult.KeepOpen();
    }

    private void RefreshStatus(string? message = null)
    {
        try
        {
            _status = _controller.GetStatusAsync().GetAwaiter().GetResult();
            Render(message ?? _status.Error);
        }
        catch (Exception)
        {
            _status = null;
            Render("Settings could not be read. Refresh status to try again.");
        }
    }

    private void Render(string? message = null)
    {
        var notification = JsonString(message ?? "Resize ordinary windows after you release a drag on another monitor. Shift-drag and recognized snapping gestures are left alone.");
        if (_status is null)
        {
            TemplateJson = $$"""
                {
                  "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
                  "type": "AdaptiveCard", "version": "1.5",
                  "body": [{ "type": "TextBlock", "text": {{notification}}, "wrap": true }],
                  "actions": [{ "type": "Action.Submit", "title": "Refresh status", "data": { "action": "refresh" }, "associatedInputs": "none" }]
                }
                """;
            return;
        }

        var statusText = JsonString(_status.DisplayStatus + ".");
        var startupText = JsonString(FeatureSettings.StartupDescription(_status.StartupState));
        var enabled = _status.Settings.Enabled ? "true" : "false";
        var startup = _status.Settings.StartWithWindows ? "true" : "false";
        var mode = _status.Settings.Mode == ResizeMode.KeepWindowShape ? "shape" : "proportions";
        TemplateJson = $$"""
            {
              "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
              "type": "AdaptiveCard", "version": "1.5",
              "body": [
                { "type": "TextBlock", "text": {{notification}}, "wrap": true },
                { "type": "TextBlock", "text": {{statusText}}, "wrap": true, "weight": "Bolder" },
                { "type": "Input.Toggle", "id": "enabled", "title": "Automatic resizing", "value": "{{enabled}}", "valueOn": "true", "valueOff": "false" },
                { "type": "Input.ChoiceSet", "id": "resizeMode", "label": "Resize mode", "value": "{{mode}}", "style": "expanded", "isRequired": true,
                  "choices": [
                    { "title": "Match screen proportions", "value": "proportions" },
                    { "title": "Keep window shape", "value": "shape" }
                  ]
                },
                { "type": "TextBlock", "text": "Match screen proportions keeps the same percentage of usable screen width and height. A wide window can become tall on a portrait screen.", "wrap": true, "isSubtle": true },
                { "type": "TextBlock", "text": "Keep window shape shrinks only when needed to fit. It does not enlarge the window when moving to a larger screen.", "wrap": true, "isSubtle": true },
                { "type": "Input.Toggle", "id": "startup", "title": "Start with Windows", "value": "{{startup}}", "valueOn": "true", "valueOff": "false" },
                { "type": "TextBlock", "text": "Resume at sign-in while automatic resizing is enabled. Turning this off leaves the current session running.", "wrap": true, "isSubtle": true },
                { "type": "TextBlock", "text": {{startupText}}, "wrap": true }
              ],
              "actions": [
                { "type": "Action.Submit", "title": "Save", "data": { "action": "save" } },
                { "type": "Action.Submit", "title": "Refresh status", "data": { "action": "refresh" }, "associatedInputs": "none" }
              ]
            }
            """;
    }

    private static bool ReadToggle(JsonElement root, string name)
    {
        var value = root.GetProperty(name);
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when value.GetString() == "true" => true,
            JsonValueKind.String when value.GetString() == "false" => false,
            _ => throw new FormatException("Invalid toggle value."),
        };
    }

    private static string JsonString(string value) => "\"" + JsonEncodedText.Encode(value).ToString() + "\"";
}
