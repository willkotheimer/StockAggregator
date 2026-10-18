using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace StockAggregatorApp.Services.Digest;

/// <summary>
/// Lays the digest out as an email: a single 600px column built from tables with
/// inline styles, because Gmail and Outlook drop most &lt;style&gt; rules and all CSS
/// variables. Light only — email clients apply their own dark mode, and a light
/// base survives that better than a hand-made dark one. Every send also carries a
/// plain-text copy for clients that block HTML.
/// </summary>
public static partial class DigestRenderer
{
    private const string Font = "'Segoe UI', Helvetica, Arial, sans-serif";
    private const string Ink = "#18202c";
    private const string Muted = "#5a6474";
    private const string Accent = "#2456c4";
    private const string Line = "#dde1e6";
    private const string Up = "#127a3e";
    private const string Down = "#b42318";

    // "up 1.2%" / "down as much as 2.5%" — the only text that gets color.
    [GeneratedRegex(@"\b(up|down)( as much as)? (\d+(?:\.\d+)?%)")]
    private static partial Regex MovePattern();

    public static string Html(DigestContent c, string dashboardUrl, bool includeDataCheck)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">")
          .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
          .Append("<title>").Append(Enc(c.Subject)).Append("</title></head>")
          .Append($"<body style=\"margin:0;padding:0;background:#f4f5f7;\">")
          .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#f4f5f7;\"><tr><td align=\"center\" style=\"padding:24px 12px;\">")
          .Append($"<table role=\"presentation\" width=\"600\" cellpadding=\"0\" cellspacing=\"0\" style=\"width:100%;max-width:600px;background:#ffffff;border:1px solid {Line};border-radius:6px;\">");

        // Header
        sb.Append($"<tr><td style=\"padding:22px 24px 14px;font-family:{Font};border-bottom:1px solid {Line};\">")
          .Append($"<div style=\"font-size:20px;font-weight:700;color:{Ink};\">{Enc(c.Title)}</div>")
          .Append($"<div style=\"font-size:13px;color:{Muted};margin-top:4px;\">{Enc(c.Dateline)}</div>")
          .Append("</td></tr>");

        foreach (var section in c.Sections)
        {
            sb.Append($"<tr><td style=\"padding:18px 24px 2px;font-family:{Font};\">")
              .Append($"<div style=\"font-size:12px;font-weight:700;letter-spacing:0.06em;text-transform:uppercase;color:{Accent};\">{Enc(section.Heading)}</div>");
            foreach (var item in section.Items)
            {
                sb.Append($"<p style=\"margin:8px 0 0;font-size:15px;line-height:1.5;color:{Ink};\">")
                  .Append(Colorize(string.Join(" ", item.Sentences)))
                  .Append("</p>");
            }
            sb.Append("</td></tr>");
        }

        // Link to the dashboard
        sb.Append($"<tr><td style=\"padding:22px 24px 6px;font-family:{Font};\">")
          .Append($"<a href=\"{Enc(dashboardUrl)}\" style=\"display:inline-block;background:{Accent};color:#ffffff;text-decoration:none;font-size:14px;font-weight:600;padding:10px 16px;border-radius:4px;\">See the full charts</a>")
          .Append("</td></tr>");

        // Footer
        sb.Append($"<tr><td style=\"padding:14px 24px 20px;font-family:{Font};font-size:12px;line-height:1.5;color:{Muted};\">");
        foreach (var line in c.Disclaimer)
        {
            sb.Append($"<div>{Enc(line)}</div>");
        }
        if (includeDataCheck)
        {
            sb.Append($"<div style=\"margin-top:10px;padding-top:10px;border-top:1px solid {Line};\">{Enc(c.DataCheck)}</div>");
        }
        sb.Append("</td></tr>");

        sb.Append("</table></td></tr></table></body></html>");
        return sb.ToString();
    }

    public static string Text(DigestContent c, string dashboardUrl, bool includeDataCheck)
    {
        var sb = new StringBuilder();
        sb.AppendLine(c.Title).AppendLine(c.Dateline).AppendLine();
        foreach (var section in c.Sections)
        {
            sb.AppendLine(section.Heading.ToUpperInvariant());
            foreach (var item in section.Items)
            {
                sb.Append("- ").AppendLine(string.Join(" ", item.Sentences));
            }
            sb.AppendLine();
        }

        sb.Append("See the full charts: ").AppendLine(dashboardUrl).AppendLine();
        foreach (var line in c.Disclaimer)
        {
            sb.AppendLine(line);
        }
        if (includeDataCheck)
        {
            sb.AppendLine().AppendLine(c.DataCheck);
        }

        return sb.ToString();
    }

    private static string Colorize(string sentence)
    {
        var encoded = Enc(sentence);
        return MovePattern().Replace(encoded, m =>
        {
            var color = m.Groups[1].Value == "up" ? Up : Down;
            return $"<span style=\"color:{color};font-weight:600;\">{m.Value}</span>";
        });
    }

    private static string Enc(string text) => WebUtility.HtmlEncode(text);
}
