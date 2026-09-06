using TradeAlert.Core.Drawing;

namespace TradeAlert.Indicator;

/// <summary>Render tối thiểu — gom lệnh vẽ; không bind order/trade.</summary>
public sealed class MinimalRenderSink : IDrawingCommandSink
{
    readonly List<DrawingCommand> _commands = new();

    public IReadOnlyList<DrawingCommand> Commands => _commands;

    public void Enqueue(in DrawingCommand command) => _commands.Add(command);

    public string LastLabelText
    {
        get
        {
            for (var i = _commands.Count - 1; i >= 0; i--)
            {
                var c = _commands[i];
                if (c.Kind == DrawingCommandKind.AddLabel && !string.IsNullOrEmpty(c.Text))
                    return c.Text!;
            }

            return "";
        }
    }

    public void ClearCommands() => _commands.Clear();
}
