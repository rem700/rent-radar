namespace RentRadar.Scraping;

/// <summary>
/// Gmail access for the ingatlan.com e-mail alerts. Configure through user secrets or environment variables
/// (<c>Mail__User</c>, <c>Mail__AppPassword</c>, <c>Mail__Label</c>) or point <c>Mail:IniPath</c> to an existing
/// mail.ini from the Python version of the project.
/// </summary>
public sealed class MailOptions
{
    public const string Section = "Mail";

    public string User { get; set; } = "";
    /// <summary>A Google app password (16 letters), not the account password.</summary>
    public string AppPassword { get; set; } = "";
    public string Label { get; set; } = "rent-finder";
    public string? IniPath { get; set; }

    public bool IsConfigured => User.Length > 0 && AppPassword.Length > 0;
}

public static class MailSettings
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["user"] = "user", ["gmail_user"] = "user",
        ["app_password"] = "app_password", ["gmail_app_password"] = "app_password", ["password"] = "app_password",
        ["label"] = "label", ["gmail_label"] = "label",
    };

    /// <summary>Values from configuration win; empty ones are filled from the ini file, if any.</summary>
    public static MailOptions Resolve(MailOptions configured)
    {
        var result = new MailOptions
        {
            User = configured.User,
            AppPassword = configured.AppPassword.Replace(" ", ""),
            Label = configured.Label,
            IniPath = configured.IniPath,
        };
        if (!string.IsNullOrWhiteSpace(configured.IniPath) && File.Exists(configured.IniPath))
        {
            var ini = LoadIni(File.ReadAllText(configured.IniPath));
            if (result.User.Length == 0) result.User = ini.User;
            if (result.AppPassword.Length == 0) result.AppPassword = ini.AppPassword;
            if (string.IsNullOrWhiteSpace(configured.Label) || configured.Label == "rent-finder") result.Label = ini.Label;
        }
        return result;
    }

    /// <summary>
    /// Understands both layouts:
    /// <code>
    /// [gmail]                 GMAIL_USER=...
    /// user = ...        or    GMAIL_APP_PASSWORD=...
    /// app_password = ...      GMAIL_LABEL=...
    /// </code>
    /// </summary>
    public static MailOptions LoadIni(string text)
    {
        var s = new MailOptions();
        foreach (var raw in text.TrimStart('﻿').Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is '#' or ';' or '[') continue;
            var sep = line.IndexOf('=') >= 0 ? '=' : line.IndexOf(':') >= 0 ? ':' : '\0';
            if (sep == '\0') continue;
            var i = line.IndexOf(sep);
            if (!Aliases.TryGetValue(line[..i].Trim(), out var field)) continue;
            var value = line[(i + 1)..].Trim().Trim('"', '\'');
            if (field == "app_password") value = value.Replace(" ", "");
            if (value.Length == 0) continue;
            switch (field)
            {
                case "user": s.User = value; break;
                case "app_password": s.AppPassword = value; break;
                case "label": s.Label = value; break;
            }
        }
        return s;
    }
}
