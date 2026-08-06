using Godot;

namespace Slingtt.Render;

// Shared UI construction helpers. Building controls in code rather than in .tscn
// files keeps the number of resource paths that an Android export could fail to
// resolve at zero, and it puts every size in one place — which is what makes the
// layout hold up from a 5" 720p phone to a tablet.
public static partial class UiKit
{
    /// <summary>Minimum touch target. Anything interactive is at least this tall,
    /// so nothing here needs a stylus or a mouse to hit.</summary>
    public const int TouchMin = 56;

    /// <summary>Wrapping is opt-in: a narrow right-aligned value label with
    /// autowrap on breaks one character per line, which is how "FULL" turns into a
    /// vertical column. Only prose asks for `wrap`.</summary>
    public static Label MakeLabel(
        string text,
        int size,
        Color color,
        HorizontalAlignment align = HorizontalAlignment.Left,
        bool wrap = false)
    {
        var l = new Label
        {
            Text = text,
            HorizontalAlignment = align,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off,
        };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        return l;
    }

    public static StyleBoxFlat Panel(Color bg, Color border, int radius = 14, int borderWidth = 1)
    {
        var sb = new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border,
            CornerRadiusTopLeft = radius,
            CornerRadiusTopRight = radius,
            CornerRadiusBottomLeft = radius,
            CornerRadiusBottomRight = radius,
            ContentMarginLeft = 14,
            ContentMarginRight = 14,
            ContentMarginTop = 10,
            ContentMarginBottom = 10,
        };
        sb.SetBorderWidthAll(borderWidth);
        return sb;
    }

    public static PanelContainer MakePanel(Color? bg = null, Color? border = null)
    {
        var p = new PanelContainer();
        p.AddThemeStyleboxOverride("panel", Panel(bg ?? Palette.UiSurface, border ?? Palette.UiBorder));
        return p;
    }

    /// <summary>A touch-sized button. `accent` drives the fill so primary and
    /// secondary actions are distinguishable without relying on hover.</summary>
    public static Button MakeButton(string text, Color accent, bool primary = true, int fontSize = 26)
    {
        var b = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(0, TouchMin),
            FocusMode = Control.FocusModeEnum.None,
        };

        Color bg = primary ? accent : Palette.UiSurfaceRaised;
        Color fg = primary ? Palette.UiBg : Palette.UiText;

        b.AddThemeStyleboxOverride("normal", Panel(bg, primary ? accent : Palette.UiBorderStrong, 14, primary ? 0 : 1));
        b.AddThemeStyleboxOverride("hover", Panel(bg.Lightened(0.06f), primary ? accent : Palette.UiBorderStrong, 14, primary ? 0 : 1));
        b.AddThemeStyleboxOverride("pressed", Panel(bg.Darkened(0.18f), primary ? accent : Palette.UiBorderStrong, 14, primary ? 0 : 1));
        b.AddThemeStyleboxOverride("disabled", Panel(Palette.UiSurface, Palette.UiBorder, 14, 1));
        b.AddThemeColorOverride("font_color", fg);
        b.AddThemeColorOverride("font_hover_color", fg);
        b.AddThemeColorOverride("font_pressed_color", fg);
        b.AddThemeColorOverride("font_disabled_color", Palette.UiTextDim);
        b.AddThemeFontSizeOverride("font_size", fontSize);
        return b;
    }

    /// <summary>Insets a full-rect container by the device's safe area (notches,
    /// punch-holes, gesture bars) plus a margin. On desktop the safe area is the
    /// whole window, so this reduces to the plain margin.</summary>
    public static MarginContainer SafeArea(int extra = 16) => new SafeAreaContainer(extra);

    /// <summary>The inset has to be applied once the node is in the tree — the
    /// physical-pixel safe area only converts to UI space when a viewport exists.
    /// Recomputed on resize so a device rotation doesn't strand content under a notch.</summary>
    private sealed partial class SafeAreaContainer : MarginContainer
    {
        private readonly int _extra;

        public SafeAreaContainer(int extra)
        {
            _extra = extra;
            SetAnchorsPreset(LayoutPreset.FullRect);
        }

        public override void _Ready()
        {
            Apply();
            Resized += Apply;
            GetTree().Root.SizeChanged += Apply;
        }

        private void Apply()
        {
            int left = _extra, top = _extra, right = _extra, bottom = _extra;

            Rect2I safe = DisplayServer.GetDisplaySafeArea();
            Vector2I screen = DisplayServer.WindowGetSize();
            if (screen.X > 0 && screen.Y > 0 && safe.Size.X > 0 && safe.Size.Y > 0
                && (safe.Size.X < screen.X || safe.Size.Y < screen.Y))
            {
                Vector2 vp = GetViewportRect().Size;
                float sx = vp.X / screen.X;
                float sy = vp.Y / screen.Y;
                left += Mathf.RoundToInt(safe.Position.X * sx);
                top += Mathf.RoundToInt(safe.Position.Y * sy);
                right += Mathf.RoundToInt((screen.X - safe.Position.X - safe.Size.X) * sx);
                bottom += Mathf.RoundToInt((screen.Y - safe.Position.Y - safe.Size.Y) * sy);
            }

            AddThemeConstantOverride("margin_left", left);
            AddThemeConstantOverride("margin_top", top);
            AddThemeConstantOverride("margin_right", right);
            AddThemeConstantOverride("margin_bottom", bottom);
        }
    }

    public static ColorRect Backdrop(Color color)
    {
        var r = new ColorRect { Color = color };
        r.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        r.MouseFilter = Control.MouseFilterEnum.Ignore;
        return r;
    }

    public static VBoxContainer VBox(int separation = 12)
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", separation);
        return v;
    }

    public static HBoxContainer HBox(int separation = 10)
    {
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", separation);
        return h;
    }

    public static Control Spacer(float height = 0f) => new()
    {
        CustomMinimumSize = new Vector2(0, height),
        SizeFlagsVertical = height > 0 ? Control.SizeFlags.Fill : Control.SizeFlags.ExpandFill,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    public static string FormatNumber(double n) => Mathf.RoundToInt(n).ToString("N0");
}
