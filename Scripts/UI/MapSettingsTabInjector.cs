using System.Collections;
using System.Linq;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using Sts2MapMod.Core;
using Sts2MapMod.Localization;
using Sts2MapMod.Patches;
using Sts2MapMod.Utils;

namespace Sts2MapMod.UI;

/// <summary>
/// Injects a "地图配置" tab into <see cref="NSettingsTabManager"/> (same pattern as ModConfig's Mods tab).
/// </summary>
public static class MapSettingsTabInjector
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private const string TabNodeName = "Sts2MapMod_MapTab";
    private const float SaveDebounceSec = 0.35f;
    private static readonly Dictionary<RoomKind, string> PreviewIconPaths = new()
    {
        [RoomKind.Monster] = "res://images/atlases/ui_atlas.sprites/map/icons/map_monster.tres",
        [RoomKind.Elite] = "res://images/atlases/ui_atlas.sprites/map/icons/map_elite.tres",
        [RoomKind.Rest] = "res://images/atlases/ui_atlas.sprites/map/icons/map_rest.tres",
        [RoomKind.Shop] = "res://images/atlases/ui_atlas.sprites/map/icons/map_shop.tres",
        [RoomKind.Treasure] = "res://images/atlases/ui_atlas.sprites/map/icons/map_chest.tres",
        [RoomKind.Event] = "res://images/atlases/ui_atlas.sprites/map/icons/map_unknown.tres",
        [RoomKind.Unknown] = "res://images/atlases/ui_atlas.sprites/map/icons/map_unknown.tres",
        [RoomKind.Custom] = "res://images/atlases/ui_atlas.sprites/map/icons/map_unknown.tres"
    };

    private static volatile bool _loggedFirstTabSwitch;
    private static ulong _saveDebounceToken;

    public static void Initialize()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
        {
            LogUtil.Warn("MapSettingsTab: Initialize skipped — Engine.GetMainLoop() is not SceneTree.");
            return;
        }

        tree.NodeAdded += OnNodeAdded;
        LogUtil.Info("MapSettingsTab: Initialize OK — subscribed to SceneTree.NodeAdded (inject when NSettingsTabManager appears).");
    }

    private static void OnNodeAdded(Node node)
    {
        if (node is not NSettingsTabManager mgr)
            return;
        if (mgr.GetNodeOrNull(TabNodeName) != null)
        {
            LogUtil.Info($"MapSettingsTab: OnNodeAdded skip — tab '{TabNodeName}' already exists on manager {mgr.Name}.");
            return;
        }

        LogUtil.Info($"MapSettingsTab: NSettingsTabManager detected name={mgr.Name} path={mgr.GetPath()} — scheduling Inject on Ready (one-shot).");
        mgr.Connect(Node.SignalName.Ready, Callable.From(() => Inject((NSettingsTabManager)mgr)),
            (uint)GodotObject.ConnectFlags.OneShot);
    }

    private static void Inject(NSettingsTabManager tabManager)
    {
        try
        {
            LogUtil.Info($"MapSettingsTab: Inject() start path={tabManager.GetPath()}");
            var tabsField = typeof(NSettingsTabManager).GetField("_tabs", PrivateInstance);
            if (tabsField?.GetValue(tabManager) is not IDictionary tabs || tabs.Count == 0)
            {
                var reason = tabsField == null ? "_tabs field null" : "tabs empty or not IDictionary";
                LogUtil.Warn($"MapSettingsTab: abort — {reason} (reflection on NSettingsTabManager).");
                Entry.Logger.Warn("MapSettingsTab: _tabs missing or empty.");
                return;
            }

            LogUtil.Info($"MapSettingsTab: _tabs OK count={tabs.Count}");

            NSettingsTab? firstTab = null;
            NSettingsPanel? firstPanel = null;
            foreach (DictionaryEntry entry in tabs)
            {
                firstTab = entry.Key as NSettingsTab;
                firstPanel = entry.Value as NSettingsPanel;
                break;
            }

            if (firstTab == null || firstPanel == null)
            {
                LogUtil.Warn($"MapSettingsTab: abort — first tab/panel null tab={firstTab != null} panel={firstPanel != null}.");
                Entry.Logger.Warn("MapSettingsTab: could not clone first tab/panel.");
                return;
            }

            var mapTab = (NSettingsTab)firstTab.Duplicate();
            mapTab.Name = TabNodeName;
            var tabImage = mapTab.GetNodeOrNull("TabImage");
            if (tabImage is CanvasItem ciImg && ciImg.Material is ShaderMaterial sm)
                ciImg.Material = (ShaderMaterial)sm.Duplicate();

            tabManager.AddChild(mapTab);
            mapTab.SetLabel(MapModL10n.T("TabTitle"));
            mapTab.Deselect();
            LogUtil.Info($"MapSettingsTab: duplicated tab added name={mapTab.Name} label={MapModL10n.T("TabTitle")}");
            PositionNewTab(tabs, mapTab);

            var mapPanel = (NSettingsPanel)firstPanel.Duplicate();
            mapPanel.Name = "Sts2MapMod_MapPanel";
            mapPanel.Visible = false;

            var contentName = firstPanel.Content?.Name;
            VBoxContainer? contentContainer = null;
            Control? focusSentinel = CreatePreReadyFocusSentinel(firstPanel);

            foreach (var child in mapPanel.GetChildren().ToArray())
            {
                var keep = child is VBoxContainer vbox &&
                    ((contentName != null && child.Name == contentName) ||
                     (contentName == null && contentContainer == null));
                if (keep && child is VBoxContainer v)
                {
                    contentContainer = v;
                    foreach (var inner in v.GetChildren().ToArray())
                    {
                        v.RemoveChild(inner);
                        inner.Free();
                    }
                }
                else
                {
                    mapPanel.RemoveChild(child);
                    child.Free();
                }
            }

            if (contentContainer != null && focusSentinel != null)
            {
                focusSentinel.Name = "__Sts2MapModFocusSentinel";
                focusSentinel.Visible = false;
                focusSentinel.MouseFilter = Control.MouseFilterEnum.Ignore;
                contentContainer.AddChild(focusSentinel);
            }

            firstPanel.GetParent()?.AddChild(mapPanel);
            if (contentContainer == null)
                contentContainer = mapPanel.Content;

            tabs.Add(mapTab, mapPanel);
            LogUtil.Info("MapSettingsTab: tabs dictionary updated (mapTab → mapPanel).");

            // Must use NClickableControl.SignalName.Released (StringName), not the string "released" — Control has no such signal (see godot.log).
            var releasedSig = Sts2UiSignals.NClickableReleased;
            var sigSource = releasedSig != null
                ? "NClickableControl.SignalName.Released (sts2 reflection)"
                : null;
            releasedSig ??= Sts2UiSignals.FindReleasedOnType(mapTab);
            if (releasedSig != null && sigSource == null)
                sigSource = $"SignalName.Released on CLR hierarchy from {mapTab.GetType().Name}";

            if (releasedSig != null)
            {
                try
                {
                    var err = mapTab.Connect(releasedSig, Callable.From<NButton>(_ =>
                    {
                        try
                        {
                            // SwitchTabTo is non-public C# on NSettingsTabManager — Node.Call cannot invoke it.
                            InvokeSwitchTabTo(tabManager, mapTab);
                            if (!_loggedFirstTabSwitch)
                            {
                                _loggedFirstTabSwitch = true;
                                LogUtil.Info("MapSettingsTab: SwitchTabTo first success (map config tab selected).");
                            }

                            LogUtil.Diag(ConfigLoader.Config.VerifyGodotPrint,
                                "MapSettingsTab: SwitchTabTo invoked (VerifyGodotPrint).");
                        }
                        catch (Exception ex)
                        {
                            LogUtil.Error("MapSettingsTab: SwitchTabTo threw", ex);
                            Entry.Logger.Warn($"MapSettingsTab: SwitchTabTo failed: {ex.Message}");
                        }
                    }));
                    if (err != Error.Ok)
                        LogUtil.Error($"MapSettingsTab: Connect failed code={err} signal={releasedSig} source={sigSource}");
                    else
                        LogUtil.Info($"MapSettingsTab: Connect OK signal={releasedSig} source={sigSource}");
                }
                catch (Exception ex)
                {
                    LogUtil.Error($"MapSettingsTab: Connect threw signal={releasedSig} source={sigSource}", ex);
                }
            }
            else
            {
                LogUtil.Warn("MapSettingsTab: could not resolve Released signal — tab clicks will not switch panel.");
                Entry.Logger.Warn("MapSettingsTab: could not resolve Released signal; tab click will not work.");
            }

            CapPanelHeight(mapPanel, firstPanel);
            if (contentContainer == null)
                LogUtil.Warn("MapSettingsTab: contentContainer null before PopulateMapSettings — UI may be empty.");

            PopulateMapSettings(contentContainer!);
            RebuildFocusTargets(mapPanel, contentContainer!, focusSentinel);

            Entry.Logger.Info($"Map color settings tab injected ({MapModL10n.T("TabTitle")}).");
            LogUtil.Info($"MapSettingsTab: Inject() completed OK title={MapModL10n.T("TabTitle")}");
            GD.Print("[STS2_MAP_MOD] Settings tab added: ", MapModL10n.T("TabTitle"));
        }
        catch (Exception ex)
        {
            LogUtil.Error("MapSettingsTab: Inject() failed with exception.", ex);
            Entry.Logger.Warn($"MapSettingsTab inject failed: {ex}");
        }
    }

    /// <summary>
    /// <see cref="NSettingsTabManager.SwitchTabTo"/> is not public and is not exposed to <see cref="GodotObject.Call"/>;
    /// vanilla tabs work via internal wiring; our duplicated tab must invoke the method via reflection.
    /// </summary>
    private static void InvokeSwitchTabTo(NSettingsTabManager tabManager, NSettingsTab tab)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var method = typeof(NSettingsTabManager).GetMethod(
            "SwitchTabTo",
            flags,
            binder: null,
            types: new[] { typeof(NSettingsTab) },
            modifiers: null);
        if (method == null)
        {
            LogUtil.Warn("MapSettingsTab: SwitchTabTo not found via reflection — trying Call (may fail if method stays internal).");
            tabManager.Call("SwitchTabTo", tab);
            return;
        }

        method.Invoke(tabManager, new object[] { tab });
    }

    private static void PositionNewTab(IDictionary tabs, NSettingsTab newTab)
    {
        // IDictionary iteration order is undefined — must sort by X or tab clicks hit the wrong panel (e.g. 模组配置 "dead").
        var existing = new List<NSettingsTab>();
        foreach (DictionaryEntry e in tabs)
        {
            if (e.Key is NSettingsTab t)
                existing.Add(t);
        }

        existing.Sort((a, b) => a.Position.X.CompareTo(b.Position.X));
        if (existing.Count == 0)
            return;

        newTab.Size = existing[0].Size;
        var y = existing[0].Position.Y;
        float spacing = existing.Count >= 2
            ? Mathf.Max(4f, existing[1].Position.X - existing[0].Position.X)
            : 120f;

        var rightmost = existing[^1];
        newTab.Position = new Vector2(rightmost.Position.X + spacing, y);

        var parent = newTab.GetParent() as Control;
        if (parent == null || parent.Size.X <= 0)
            return;

        float right = newTab.Position.X + newTab.Size.X;
        if (right <= parent.Size.X)
            return;

        // Overflow: redistribute all vanilla tabs + new tab in sorted visual order (do not rely on dict order).
        var all = new List<NSettingsTab>(existing) { newTab };
        all.Sort((a, b) => a.Position.X.CompareTo(b.Position.X));
        int total = all.Count;
        float tabW = Mathf.Max(1f, all[0].Size.X);
        float newSpacing = parent.Size.X / total;
        float startX = (newSpacing - tabW) * 0.5f;
        for (int i = 0; i < all.Count; i++)
            all[i].Position = new Vector2(startX + newSpacing * i, y);
    }

    private static void CapPanelHeight(NSettingsPanel mapPanel, NSettingsPanel firstPanel)
    {
        try
        {
            var viewport = mapPanel.GetViewport();
            if (viewport != null)
            {
                var refreshCallable = new Callable(mapPanel, NSettingsPanel.MethodName.RefreshSize);
                if (viewport.IsConnected(Viewport.SignalName.SizeChanged, refreshCallable))
                    viewport.Disconnect(Viewport.SignalName.SizeChanged, refreshCallable);
            }

            float maxH = firstPanel.Size.Y;
            if (maxH < 100 && mapPanel.GetParent() is Control p)
                maxH = p.Size.Y * 0.85f;
            mapPanel.Size = new Vector2(mapPanel.Size.X, maxH);
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"MapSettingsTab: cap height failed: {ex.Message}");
        }
    }

    private static Control? CreatePreReadyFocusSentinel(NSettingsPanel firstPanel)
    {
        try
        {
            if (firstPanel.DefaultFocusedControl is not Control def || !GodotObject.IsInstanceValid(def))
                return null;
            if (def.Duplicate() is not Control dup)
                return null;
            dup.FocusMode = Control.FocusModeEnum.All;
            return dup;
        }
        catch
        {
            return null;
        }
    }

    private static void PopulateMapSettings(VBoxContainer root)
    {
        ConfigLoader.EnsureLoaded();
        var cfg = ConfigLoader.Config;
        var validators = new List<Func<string?>>();
        var previewRefreshers = new List<Action>();
        LogUtil.Info($"MapSettingsTab: PopulateMapSettings start Enabled={cfg.Enabled} roomKinds={Enum.GetValues<RoomKind>().Length}");

        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FollowFocus = true
        };
        root.AddChild(scroll);

        var inner = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        scroll.AddChild(inner);

        AddCheckRow(inner, MapModL10n.T("EnableTint"), cfg.Enabled, v =>
        {
            cfg.Enabled = v;
            RefreshAllPreviews(previewRefreshers);
            PersistLiveChange();
            MapScreenPatch.RefreshOpenMapTint();
        });
        AddCheckRow(inner, MapModL10n.T("TintIcons"), cfg.ColorNodeIcon, v =>
        {
            cfg.ColorNodeIcon = v;
            RefreshAllPreviews(previewRefreshers);
            PersistLiveChange();
            MapScreenPatch.RefreshOpenMapTint();
        });

        inner.AddChild(new HSeparator());

        var hint = new Label
        {
            Text = MapModL10n.T("Hint"),
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        hint.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        inner.AddChild(hint);

        var applied = new Label
        {
            Text = MapModL10n.T("AppliedNow"),
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        applied.AddThemeColorOverride("font_color", new Color(0.75f, 0.86f, 0.75f));
        inner.AddChild(applied);

        foreach (RoomKind kind in Enum.GetValues<RoomKind>().Where(k => k is not RoomKind.Boss and not RoomKind.Custom and not RoomKind.Unknown))
            AddKindSection(inner, kind, SettingsFor(cfg, kind), cfg, validators, previewRefreshers);

        var validationStatus = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        SetValidationStatus(validationStatus, MapModL10n.T("ValidationOk"), ok: true);
        inner.AddChild(validationStatus);

        var restoreDefaults = new Button
        {
            Text = MapModL10n.T("RestoreDefaults"),
            FocusMode = Control.FocusModeEnum.All
        };
        restoreDefaults.Pressed += () =>
        {
            try
            {
                cfg.RestoreDefaults();
                RebuildSettingsUi(root);
                ConfigLoader.SaveToDisk();
                MapScreenPatch.RefreshOpenMapTint();
            }
            catch (Exception ex)
            {
                LogUtil.Error("MapSettingsTab: RestoreDefaults failed.", ex);
            }
        };
        inner.AddChild(restoreDefaults);
    }

    private static void RebuildSettingsUi(VBoxContainer root)
    {
        foreach (var child in root.GetChildren().ToArray())
        {
            root.RemoveChild(child);
            child.QueueFree();
        }

        PopulateMapSettings(root);
    }

    private static void AddCheckRow(VBoxContainer parent, string label, bool initial, Action<bool> apply)
    {
        var wrap = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var left = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var l = new Label { Text = label, CustomMinimumSize = new Vector2(280, 0) };
        l.AddThemeFontSizeOverride("font_size", 18);
        var desc = new Label
        {
            Text = label == MapModL10n.T("EnableTint") ? MapModL10n.T("EnableTintDesc") : MapModL10n.T("TintIconsDesc"),
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        desc.AddThemeColorOverride("font_color", new Color(0.77f, 0.74f, 0.62f));
        var toggle = new Button
        {
            ToggleMode = true,
            ButtonPressed = initial,
            FocusMode = Control.FocusModeEnum.All,
            CustomMinimumSize = new Vector2(88, 36)
        };
        void RefreshToggleVisual(bool on)
        {
            toggle.Text = on ? MapModL10n.T("EnabledStateOn") : MapModL10n.T("EnabledStateOff");
            toggle.Modulate = on ? new Color(0.78f, 0.92f, 0.78f, 1f) : new Color(0.98f, 0.82f, 0.62f, 1f);
        }

        RefreshToggleVisual(initial);
        toggle.Connect(BaseButton.SignalName.Toggled, Callable.From((bool on) =>
        {
            apply(on);
            RefreshToggleVisual(on);
        }));
        left.AddChild(l);
        left.AddChild(desc);
        row.AddChild(left);
        row.AddChild(toggle);
        wrap.AddChild(row);
        wrap.AddChild(new HSeparator());
        parent.AddChild(wrap);
    }

    private static void AddKindSection(VBoxContainer parent, RoomKind kind, RoomTypeColorSettings st, MapColorConfig cfg,
        List<Func<string?>> validators, List<Action> previewRefreshers)
    {
        parent.AddChild(new HSeparator());
        var section = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var left = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var right = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(150, 0),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
        };

        var titleRow = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        titleRow.AddChild(CreateKindBadge(kind));
        left.AddChild(titleRow);

        right.AddChild(new Label
        {
            Text = MapModL10n.T("Preview"),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        var preview = CreatePreviewIcon(kind);
        right.AddChild(preview);

        var row1 = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var colorGroup = new VBoxContainer { CustomMinimumSize = new Vector2(220, 0) };
        colorGroup.AddChild(MkSectionLabel(MapModL10n.T("Color")));
        colorGroup.AddChild(MkMutedLabel(MapModL10n.T("ColorDesc")));
        row1.AddChild(colorGroup);
        var hex = new LineEdit
        {
            Text = st.Color,
            PlaceholderText = "#RRGGBB",
            CustomMinimumSize = new Vector2(170, 0),
            FocusMode = Control.FocusModeEnum.All
        };
        var colorError = new Label
        {
            Visible = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        colorError.AddThemeColorOverride("font_color", new Color(1f, 0.45f, 0.45f));

        void RefreshColorValidation(bool normalizeWhenValid)
        {
            if (MapConfigValidator.TryNormalizeHexColor(hex.Text, out var normalized, out _))
            {
                if (normalizeWhenValid && hex.Text != normalized)
                {
                    hex.Text = normalized;
                    return;
                }

                st.Color = normalized;
                colorError.Visible = false;
                colorError.Text = "";
                hex.TooltipText = "";
                RefreshPreview(preview, kind, st, cfg);
                PersistLiveChange();
                MapScreenPatch.RefreshOpenMapTint();
                return;
            }

            colorError.Text = MapModL10n.T("ValidationColorHex");
            colorError.Visible = true;
            hex.TooltipText = colorError.Text;
        }

        hex.TextChanged += t =>
        {
            if (!string.IsNullOrWhiteSpace(t))
                st.Color = t.Trim();
            RefreshColorValidation(normalizeWhenValid: false);
        };
        hex.FocusExited += () => RefreshColorValidation(normalizeWhenValid: true);
        row1.AddChild(hex);

        var opacityGroup = new VBoxContainer { CustomMinimumSize = new Vector2(190, 0) };
        opacityGroup.AddChild(MkSectionLabel(MapModL10n.T("Opacity")));
        opacityGroup.AddChild(MkMutedLabel(MapModL10n.T("OpacityDesc")));
        row1.AddChild(opacityGroup);
        var alpha = new HSlider
        {
            MinValue = 0,
            MaxValue = 1,
            Step = 0.01,
            Value = st.Alpha,
            CustomMinimumSize = new Vector2(160, 24),
            FocusMode = Control.FocusModeEnum.All
        };
        alpha.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        alpha.ValueChanged += v =>
        {
            st.Alpha = (float)v;
            RefreshPreview(preview, kind, st, cfg);
            PersistLiveChange();
            MapScreenPatch.RefreshOpenMapTint();
        };
        row1.AddChild(alpha);
        left.AddChild(row1);
        left.AddChild(colorError);

        var row2 = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var brightnessGroup = new VBoxContainer { CustomMinimumSize = new Vector2(220, 0) };
        brightnessGroup.AddChild(MkSectionLabel(MapModL10n.T("Brightness")));
        brightnessGroup.AddChild(MkMutedLabel(MapModL10n.T("BrightnessDesc")));
        row2.AddChild(brightnessGroup);
        var bright = new HSlider
        {
            MinValue = 0.25,
            MaxValue = 2,
            Step = 0.05,
            Value = st.Brightness ?? 1.0,
            CustomMinimumSize = new Vector2(260, 24),
            FocusMode = Control.FocusModeEnum.All
        };
        bright.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        bright.ValueChanged += v =>
        {
            var f = (float)v;
            st.Brightness = Math.Abs(f - 1.0) < 0.001 ? null : f;
            RefreshPreview(preview, kind, st, cfg);
            PersistLiveChange();
            MapScreenPatch.RefreshOpenMapTint();
        };
        row2.AddChild(bright);
        var brightHint = new Label { Text = MapModL10n.T("BrightnessHint"), AutowrapMode = TextServer.AutowrapMode.Word, CustomMinimumSize = new Vector2(210, 0) };
        brightHint.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        brightHint.AddThemeColorOverride("font_color", new Color(0.77f, 0.74f, 0.62f));
        row2.AddChild(brightHint);
        left.AddChild(row2);

        section.AddChild(left);
        section.AddChild(right);
        parent.AddChild(section);

        validators.Add(() =>
        {
            RefreshColorValidation(normalizeWhenValid: true);
            return colorError.Visible ? $"{MapModL10n.KindTitle(kind)}: {MapModL10n.T("ValidationColorHex")}" : null;
        });

        previewRefreshers.Add(() => RefreshPreview(preview, kind, st, cfg));
        RefreshPreview(preview, kind, st, cfg);
    }

    private static TextureRect CreatePreviewIcon(RoomKind kind)
    {
        var preview = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(88, 88),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            TooltipText = MapModL10n.KindTitle(kind)
        };
        preview.Texture = LoadPreviewTexture(kind);
        return preview;
    }

    private static Texture2D? LoadPreviewTexture(RoomKind kind)
    {
        if (!PreviewIconPaths.TryGetValue(kind, out var path))
            path = PreviewIconPaths[RoomKind.Unknown];
        return ResourceLoader.Load<Texture2D>(path, cacheMode: ResourceLoader.CacheMode.Reuse);
    }

    private static void RefreshPreview(TextureRect preview, RoomKind kind, RoomTypeColorSettings st, MapColorConfig cfg)
    {
        preview.Texture ??= LoadPreviewTexture(kind);
        var vanilla = new Color(1f, 1f, 1f, 1f);
        var tinted = Core.ColorPalette.ToGodotColor(st);
        preview.SelfModulate = (cfg.Enabled && cfg.ColorNodeIcon) ? tinted : vanilla;
    }

    private static void RefreshAllPreviews(IEnumerable<Action> previewRefreshers)
    {
        foreach (var refresh in previewRefreshers)
            refresh();
    }

    private static void PersistLiveChange()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
        {
            ConfigLoader.SaveToDisk();
            return;
        }

        var token = ++_saveDebounceToken;
        var timer = tree.CreateTimer(SaveDebounceSec);
        timer.Timeout += () =>
        {
            if (token != _saveDebounceToken)
                return;
            ConfigLoader.SaveToDisk();
        };
    }

    private static Label MkSectionLabel(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 15);
        return label;
    }

    private static Label MkMutedLabel(string text)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeColorOverride("font_color", new Color(0.77f, 0.74f, 0.62f));
        return label;
    }

    private static Control CreateKindBadge(RoomKind kind)
    {
        var wrap = new MarginContainer();
        wrap.AddThemeConstantOverride("margin_bottom", 6);

        var panel = new PanelContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin
        };

        var box = new StyleBoxFlat
        {
            BgColor = new Color(0.12f, 0.16f, 0.20f, 0.92f),
            BorderColor = new Color(0.40f, 0.50f, 0.58f, 0.95f),
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            ContentMarginLeft = 14,
            ContentMarginTop = 8,
            ContentMarginRight = 14,
            ContentMarginBottom = 8
        };
        panel.AddThemeStyleboxOverride("panel", box);

        var row = new HBoxContainer();
        var dot = new ColorRect
        {
            Color = BadgeAccentColor(kind),
            CustomMinimumSize = new Vector2(8, 26)
        };
        row.AddChild(dot);

        var spacer = new Control { CustomMinimumSize = new Vector2(10, 1) };
        row.AddChild(spacer);

        var title = new Label
        {
            Text = MapModL10n.KindTitle(kind)
        };
        title.AddThemeFontSizeOverride("font_size", 24);
        title.AddThemeColorOverride("font_color", new Color(0.98f, 0.96f, 0.90f));
        row.AddChild(title);

        panel.AddChild(row);
        wrap.AddChild(panel);
        return wrap;
    }

    private static Color BadgeAccentColor(RoomKind kind) => kind switch
    {
        RoomKind.Monster => new Color(0.79f, 0.71f, 0.55f),
        RoomKind.Elite => new Color(0.74f, 0.29f, 0.54f),
        RoomKind.Rest => new Color(0.86f, 0.23f, 0.16f),
        RoomKind.Shop => new Color(0.53f, 0.63f, 0.18f),
        RoomKind.Treasure => new Color(0.84f, 0.67f, 0.18f),
        RoomKind.Event => new Color(0.33f, 0.69f, 0.31f),
        _ => new Color(0.54f, 0.60f, 0.66f)
    };

    private static void SetValidationStatus(Label label, string text, bool ok)
    {
        label.Text = text;
        label.AddThemeColorOverride("font_color", ok ? new Color(0.72f, 0.9f, 0.72f) : new Color(1f, 0.45f, 0.45f));
    }

    private static RoomTypeColorSettings SettingsFor(MapColorConfig c, RoomKind k) => k switch
    {
        RoomKind.Monster => c.Monster,
        RoomKind.Elite => c.Elite,
        RoomKind.Rest => c.Rest,
        RoomKind.Shop => c.Shop,
        RoomKind.Treasure => c.Treasure,
        RoomKind.Event => c.Event,
        RoomKind.Boss => c.Boss,
        RoomKind.Unknown => c.Unknown,
        RoomKind.Custom => c.Custom,
        _ => c.Unknown
    };

    private static void RebuildFocusTargets(NSettingsPanel panel, VBoxContainer content, Control? sentinel)
    {
        try
        {
            var focusables = new List<Control>();
            CollectFocusable(content, focusables);

            if (sentinel != null && GodotObject.IsInstanceValid(sentinel) &&
                sentinel.GetParent() == content && focusables.Count > 0)
            {
                content.RemoveChild(sentinel);
                sentinel.QueueFree();
                sentinel = null;
            }

            if (focusables.Count == 0 && sentinel != null && GodotObject.IsInstanceValid(sentinel))
                focusables.Add(sentinel);

            for (var i = 0; i < focusables.Count; i++)
            {
                var c = focusables[i];
                c.FocusNeighborLeft = c.GetPath();
                c.FocusNeighborRight = c.GetPath();
                c.FocusNeighborTop = (i > 0 ? focusables[i - 1] : c).GetPath();
                c.FocusNeighborBottom = (i < focusables.Count - 1 ? focusables[i + 1] : c).GetPath();
            }

            var firstField = typeof(NSettingsPanel).GetField("_firstControl", PrivateInstance);
            firstField?.SetValue(panel, focusables.FirstOrDefault());
            LogUtil.Info($"MapSettingsTab: RebuildFocusTargets OK focusableCount={focusables.Count} first={(focusables.FirstOrDefault()?.Name.ToString() ?? "null")}");
        }
        catch (Exception ex)
        {
            LogUtil.Error("MapSettingsTab: RebuildFocusTargets failed.", ex);
            Entry.Logger.Warn($"MapSettingsTab: focus rebuild failed: {ex.Message}");
        }
    }

    private static void CollectFocusable(Control parent, List<Control> list)
    {
        foreach (var child in parent.GetChildren())
        {
            if (child is not Control c || !c.Visible)
                continue;
            if (c.FocusMode == Control.FocusModeEnum.All)
                list.Add(c);
            CollectFocusable(c, list);
        }
    }
}
