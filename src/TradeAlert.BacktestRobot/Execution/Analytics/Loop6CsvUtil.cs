using System.Globalization;
using System.Text;

namespace TradeAlert.BacktestRobot.Execution.Analytics;

public static class Loop6CsvUtil
{
    public static string SafeCsv(object? value)
    {
        if (value is null)
            return "";

        var s = value switch
        {
            double d => d.ToString("0.########", CultureInfo.InvariantCulture),
            float f => f.ToString("0.########", CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "",
        };

        if (s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0)
            return "\"" + s.Replace("\"", "\"\"") + "\"";

        return s;
    }

    public static string JoinRow(IReadOnlyList<object?> values)
    {
        var sb = new StringBuilder(values.Count * 8);
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0)
                sb.Append(',');
            sb.Append(SafeCsv(values[i]));
        }
        return sb.ToString();
    }

    public static void AppendCsvLine(string path, IReadOnlyList<string> headers, IReadOnlyList<object?> values, ref bool headerWritten)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        if (!headerWritten && !File.Exists(path))
        {
            File.WriteAllText(path, JoinRow(headers.Cast<object?>().ToArray()) + Environment.NewLine, Encoding.UTF8);
            headerWritten = true;
        }
        else if (!headerWritten && File.Exists(path))
        {
            headerWritten = true;
        }

        File.AppendAllText(path, JoinRow(values) + Environment.NewLine, Encoding.UTF8);
    }
}
