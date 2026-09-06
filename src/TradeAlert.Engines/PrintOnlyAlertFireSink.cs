using System;
using TradeAlert.Core.Models.Alerts;

namespace TradeAlert.Indicator;

/// <summary>AccessRights.None — chỉ in/draw tĩnh qua delegate; không gọi Telegram/email/network/file APIs.</summary>
public sealed class PrintOnlyAlertFireSink : IAlertFireSink
{
    readonly Action<string>? _drawStaticDiagOptional;
    readonly Action<string> _printLine;
    readonly Func<AlertEvent, string> _format;

    public PrintOnlyAlertFireSink(
        Action<string> printLine,
        Func<AlertEvent, string> format,
        Action<string>? drawStaticDiagOptional = null)
    {
        _printLine = printLine ?? throw new ArgumentNullException(nameof(printLine));
        _format = format ?? throw new ArgumentNullException(nameof(format));
        _drawStaticDiagOptional = drawStaticDiagOptional;
    }

    public void Enqueue(in AlertEvent alertEvent)
    {
        var line = _format(alertEvent);
        _printLine(line);
        _drawStaticDiagOptional?.Invoke(line);
    }
}
