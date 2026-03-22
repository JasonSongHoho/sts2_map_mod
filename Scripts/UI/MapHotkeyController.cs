using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using Sts2MapMod.Core;
using Sts2MapMod.Localization;
using Sts2MapMod.Patches;
using Sts2MapMod.Utils;

namespace Sts2MapMod.UI;

public partial class MapHotkeyController : Node
{
    public const string ControllerNodeName = "Sts2MapMod_HotkeyController";
    private const Key ToggleKey = Key.F8;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        LogUtil.Info("MapHotkey: ready (F8 toggles map tint).");
    }

    public override void _Input(InputEvent @event)
    {
        if (TryHandleToggle(@event))
            GetViewport()?.SetInputAsHandled();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (TryHandleToggle(@event))
            GetViewport()?.SetInputAsHandled();
    }

    public static void GameInputPostfix(InputEvent inputEvent)
    {
        TryHandleToggle(inputEvent);
    }

    private static bool TryHandleToggle(InputEvent @event)
    {
        if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo)
            return false;

        if (keyEvent.Keycode != ToggleKey && keyEvent.PhysicalKeycode != ToggleKey)
            return false;

        var screen = NMapScreen.Instance;
        if (screen == null)
            return false;

        var focusOwner = screen.GetViewport()?.GuiGetFocusOwner();
        if (focusOwner is LineEdit or TextEdit)
            return false;

        var cfg = ConfigLoader.Config;
        cfg.Enabled = !cfg.Enabled;
        ConfigLoader.SaveToDisk();
        MapScreenPatch.RefreshOpenMapTint();

        var state = cfg.Enabled ? MapModL10n.T("EnabledStateOn") : MapModL10n.T("EnabledStateOff");
        LogUtil.Info($"MapHotkey: F8 -> {state}");
        GD.Print($"[STS2_MAP_MOD] F8 {MapModL10n.T("EnableTint")} = {state}");
        return true;
    }
}
