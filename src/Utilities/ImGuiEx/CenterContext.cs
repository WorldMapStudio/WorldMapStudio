#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Collects a vertical stack of labels/buttons/progress bars and, on <see cref="Dispose"/>, lays them
/// out centered both horizontally and vertically within the current window. Built for full-screen menu
/// scenes where a small column of controls should sit in the middle of the viewport.
/// </summary>
public sealed class CenterContext : IDisposable
{
    private record struct LabelItem(string Label, Vector4 Color, float FontScale, bool Disabled);
    private record struct ButtonItem(string Label, Action OnPressed, bool Disabled);
    private record struct ProgressBarItem(float Fraction, string? Overlay, bool Disabled);

    private readonly List<(object Item, float Height)> _items = [];
    private readonly float _buttonWidth;
    private readonly float _buttonHeight;
    private readonly float _spacing;

    public CenterContext(float buttonWidth, float buttonHeight, float spacing)
    {
        _buttonWidth = buttonWidth;
        _buttonHeight = buttonHeight;
        _spacing = spacing;
    }

    public void Label(string label, float fontScale = 1.0f, bool disabled = false)
    {
        LabelColored(label, ImGui.GetStyle().Colors[(int)ImGuiCol.Text], fontScale, disabled);
    }

    public void LabelColored(string label, Vector4 color, float fontScale = 1.0f, bool disabled = false)
    {
        _items.Add((new LabelItem(label, color, fontScale, disabled), _buttonHeight));
    }

    public void Button(string label, Action onPressed, bool disabled = false)
    {
        _items.Add((new ButtonItem(label, onPressed, disabled), _buttonHeight));
    }

    public void ProgressBar(float fraction, string? overlay = null, bool disabled = false)
    {
        _items.Add((new ProgressBarItem(Math.Clamp(fraction, 0f, 1f), overlay, disabled), _buttonHeight));
    }

    public void Dispose()
    {
        var windowSize = ImGui.GetWindowSize();
        int count = _items.Count;

        float totalHeight = count * _buttonHeight + (count - 1) * _spacing;
        float startY = (windowSize.Y - totalHeight) * 0.5f;

        for (int i = 0; i < _items.Count; i++)
        {
            var (item, _) = _items[i];
            float y = startY + i * (_buttonHeight + _spacing);

            switch (item)
            {
                case LabelItem l:
                    RenderLabel(l, y, windowSize.X);
                    break;
                case ButtonItem b:
                    RenderButton(b, y, windowSize.X);
                    break;
                case ProgressBarItem p:
                    RenderProgressBar(p, y, windowSize.X);
                    break;
            }
        }
    }

    private void RenderLabel(LabelItem l, float y, float windowWidth)
    {
        var font = ImGui.GetFont();
        float scaledFontSize = ImGui.GetFontSize() * l.FontScale;

        var textSize = ImGui.CalcTextSize(l.Label) * l.FontScale;
        float x = (windowWidth - _buttonWidth) * 0.5f + (_buttonWidth - textSize.X) * 0.5f;
        float labelY = y + (_buttonHeight - textSize.Y) * 0.5f;

        Vector4 color = l.Disabled
            ? ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]
            : l.Color;

        uint colorU32 = ImGui.ColorConvertFloat4ToU32(color);
        ImGui.GetWindowDrawList().AddText(font, scaledFontSize, new Vector2(x, labelY), colorU32, l.Label);
    }

    private void RenderButton(ButtonItem b, float y, float windowWidth)
    {
        float x = (windowWidth - _buttonWidth) * 0.5f;
        ImGui.SetCursorPosY(y);
        ImGui.SetCursorPosX(x);

        if (b.Disabled)
            ImGui.BeginDisabled();

        if (ImGui.Button(b.Label, new Vector2(_buttonWidth, _buttonHeight)))
            b.OnPressed();

        if (b.Disabled)
            ImGui.EndDisabled();
    }

    private void RenderProgressBar(ProgressBarItem p, float y, float windowWidth)
    {
        float x = (windowWidth - _buttonWidth) * 0.5f;
        var windowPos = ImGui.GetWindowPos();
        var scrollY = ImGui.GetScrollY();

        var min = new Vector2(windowPos.X + x, windowPos.Y + y - scrollY);
        var max = new Vector2(min.X + _buttonWidth, min.Y + _buttonHeight);
        var fillMax = new Vector2(min.X + _buttonWidth * p.Fraction, max.Y);

        var style = ImGui.GetStyle();
        var drawList = ImGui.GetWindowDrawList();

        float disabledAlpha = p.Disabled ? style.DisabledAlpha : 1f;

        uint bgColor = MulAlpha(ImGui.ColorConvertFloat4ToU32(style.Colors[(int)ImGuiCol.FrameBg]), disabledAlpha);
        uint fillColor = MulAlpha(ImGui.ColorConvertFloat4ToU32(style.Colors[(int)ImGuiCol.PlotHistogram]), disabledAlpha);
        uint textColor = MulAlpha(ImGui.ColorConvertFloat4ToU32(style.Colors[(int)ImGuiCol.Text]), disabledAlpha);

        drawList.AddRectFilled(min, max, bgColor, style.FrameRounding);
        drawList.AddRectFilled(min, fillMax, fillColor, style.FrameRounding);

        if (!string.IsNullOrEmpty(p.Overlay))
        {
            var textSize = ImGui.CalcTextSize(p.Overlay);
            var textPos = new Vector2(
                min.X + (_buttonWidth - textSize.X) * 0.5f,
                min.Y + (_buttonHeight - textSize.Y) * 0.5f);

            drawList.AddText(textPos, textColor, p.Overlay);
        }
    }

    private static uint MulAlpha(uint color, float alpha)
    {
        uint a = (uint)((color >> 24) * alpha) & 0xFF;
        return (color & 0x00FFFFFF) | (a << 24);
    }
}
