using System.Collections.Generic;
using cAlgo.API;

namespace TradeAlert.Indicator.Host;

/// <summary>Draggable on-chart panel for Real Zone debug (band, probe, overlap/buffer).</summary>
public sealed class RealZoneDebugPanelRenderer
{
    readonly List<TextBlock> _lineBlocks = new(32);
    ChartDraggable? _draggable;
    StackPanel? _panel;
    bool _positioned;

    const double FontSize = 10;
    const double InitialX = 20;
    const double DefaultInitialY = 240;
    readonly double _initialY;

    public RealZoneDebugPanelRenderer(double initialY = DefaultInitialY) =>
        _initialY = initialY;

    public void Sync(Chart chart, IReadOnlyList<(string Text, Color Color)> lines)
    {
        EnsureDraggable(chart);
        if (_panel == null)
            return;

        while (_lineBlocks.Count > lines.Count)
        {
            var last = _lineBlocks.Count - 1;
            _panel.RemoveChild(_lineBlocks[last]);
            _lineBlocks.RemoveAt(last);
        }

        for (var i = 0; i < lines.Count; i++)
        {
            var (text, color) = lines[i];
            if (i >= _lineBlocks.Count)
            {
                var block = new TextBlock
                {
                    FontSize = FontSize,
                    Margin = 1,
                    HorizontalAlignment = HorizontalAlignment.Left,
                };
                _lineBlocks.Add(block);
                _panel.AddChild(block);
            }

            var line = _lineBlocks[i];
            line.Text = text;
            line.ForegroundColor = color;
            line.FontWeight = i == 0
                || text.StartsWith("─", System.StringComparison.Ordinal)
                || text.Contains("===", System.StringComparison.Ordinal)
                || text.StartsWith("━", System.StringComparison.Ordinal)
                ? FontWeight.Bold
                : FontWeight.Normal;
        }
    }

    public void Clear(Chart chart)
    {
        if (_draggable == null)
            return;

        chart.Draggables.Remove(_draggable);
        _draggable = null;
        _panel = null;
        _lineBlocks.Clear();
        _positioned = false;
    }

    void EnsureDraggable(Chart chart)
    {
        if (_draggable != null)
            return;

        _panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            BackgroundColor = Color.FromArgb(225, 12, 18, 28),
            Margin = 6,
        };

        _draggable = chart.Draggables.Add();
        _draggable.ShowGrip = true;
        _draggable.Child = _panel;

        if (!_positioned)
        {
            _draggable.X = InitialX;
            _draggable.Y = _initialY;
            _positioned = true;
        }
    }
}
