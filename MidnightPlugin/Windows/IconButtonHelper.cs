using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace MidnightPlugin.Windows;

internal static class IconButtonHelper
{
    /// <summary>
    /// Draws a button pairing a FontAwesome icon with a label. The icon is
    /// rendered with Dalamud's dedicated icon font so it is never subject to
    /// whether the active UI font merged the FontAwesome glyph range. Glyphs are
    /// painted through the draw list rather than as ImGui text so the caller's
    /// <see cref="ImGui.SameLine()"/> continues to align from the button's edge.
    /// </summary>
    public static bool IconTextButton(FontAwesomeIcon icon, string label, string id, bool iconAfter = false)
    {
        var iconString = icon.ToIconString();
        var style = ImGui.GetStyle();
        var iconFont = Plugin.PluginInterface.UiBuilder.FontIcon;

        ImGui.PushFont(iconFont);
        var iconSize = ImGui.CalcTextSize(iconString);
        ImGui.PopFont();
        var textSize = ImGui.CalcTextSize(label);

        var width = iconSize.X + style.ItemInnerSpacing.X + textSize.X + style.FramePadding.X * 2f;
        var height = MathF.Max(iconSize.Y, textSize.Y) + style.FramePadding.Y * 2f;
        var clicked = ImGui.InvisibleButton(id, new Vector2(width, height));

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var draw = ImGui.GetWindowDrawList();

        var color = ImGui.GetColorU32(ImGuiCol.Button);
        if (ImGui.IsItemActive())
        {
            color = ImGui.GetColorU32(ImGuiCol.ButtonActive);
        }
        else if (ImGui.IsItemHovered())
        {
            color = ImGui.GetColorU32(ImGuiCol.ButtonHovered);
        }

        var rounding = style.FrameRounding;
        draw.AddRectFilled(min, max, color, rounding);
        draw.AddRect(min, max, ImGui.GetColorU32(ImGuiCol.Border), rounding, ImDrawFlags.None, 1f);

        var textColor = ImGui.GetColorU32(ImGuiCol.Text);
        var iconX = iconAfter ? width - iconSize.X - style.FramePadding.X : style.FramePadding.X;
        var labelX = iconAfter ? style.FramePadding.X : iconSize.X + style.ItemInnerSpacing.X;
        var iconPos = new Vector2(min.X + iconX, min.Y + (height - iconSize.Y) / 2f);
        var labelPos = new Vector2(min.X + labelX, min.Y + (height - textSize.Y) / 2f);

        ImGui.PushFont(iconFont);
        draw.AddText(iconPos, textColor, iconString);
        ImGui.PopFont();
        draw.AddText(labelPos, textColor, label);

        return clicked;
    }

    /// <summary>
    /// Draws a fixed-size, icon-only toolbar button with a hover tooltip.
    /// </summary>
    public static bool IconButton(FontAwesomeIcon icon, string tooltip, string id)
    {
        var iconString = icon.ToIconString();
        var style = ImGui.GetStyle();
        var iconFont = Plugin.PluginInterface.UiBuilder.FontIcon;

        ImGui.PushFont(iconFont);
        var iconSize = ImGui.CalcTextSize(iconString);
        ImGui.PopFont();

        var side = ImGui.GetFrameHeight();
        var size = new Vector2(side, side);
        var clicked = ImGui.InvisibleButton(id, size);

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var draw = ImGui.GetWindowDrawList();

        var color = ImGui.GetColorU32(ImGuiCol.Button);
        if (ImGui.IsItemActive())
        {
            color = ImGui.GetColorU32(ImGuiCol.ButtonActive);
        }
        else if (ImGui.IsItemHovered())
        {
            color = ImGui.GetColorU32(ImGuiCol.ButtonHovered);
        }

        var rounding = style.FrameRounding;
        draw.AddRectFilled(min, max, color, rounding);
        draw.AddRect(min, max, ImGui.GetColorU32(ImGuiCol.Border), rounding, ImDrawFlags.None, 1f);

        var textColor = ImGui.GetColorU32(ImGuiCol.Text);
        var iconPos = new Vector2(min.X + (size.X - iconSize.X) / 2f, min.Y + (size.Y - iconSize.Y) / 2f);

        ImGui.PushFont(iconFont);
        draw.AddText(iconPos, textColor, iconString);
        ImGui.PopFont();

        if (ImGui.IsItemHovered() && !string.IsNullOrEmpty(tooltip))
        {
            ImGui.BeginTooltip();
            ImGui.Text(tooltip);
            ImGui.EndTooltip();
        }

        return clicked;
    }
}
