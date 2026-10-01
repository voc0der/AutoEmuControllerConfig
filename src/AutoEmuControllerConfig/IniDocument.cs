namespace AutoEmuControllerConfig;

// Keep unrelated settings, comments, spelling, whitespace and line endings intact.
internal sealed class IniDocument
{
    private readonly List<string> lines;
    private readonly string newline;
    private readonly bool trailingNewline;

    public IniDocument(string text)
    {
        newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        trailingNewline = text.EndsWith('\n');
        lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        if (trailingNewline) lines.RemoveAt(lines.Count - 1);
    }

    public string? Get(string section, string key)
    {
        var index = Find(section, key);
        return index < 0 ? null : lines[index][(lines[index].IndexOf('=') + 1)..].Trim();
    }

    public bool GetBoolean(string section, string key, bool fallback)
    {
        if (Get(section, key + "\\default") == "true") return fallback;
        return Get(section, key)?.ToLowerInvariant() switch
        {
            "true" or "yes" or "1" => true,
            "false" or "no" or "0" => false,
            _ => fallback
        };
    }

    public IEnumerable<string> Keys(string section)
    {
        var active = "";
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']')) active = trimmed[1..^1];
            else if (active == section && !trimmed.StartsWith(';') && !trimmed.StartsWith('#') && trimmed.Contains('='))
                yield return trimmed[..trimmed.IndexOf('=')].Trim();
        }
    }

    public void Set(string section, string key, string value)
    {
        if (value.Contains('\n') || value.Contains('\r')) throw new ArgumentException("Multiline INI value.");
        var index = Find(section, key);
        if (index >= 0)
        {
            if (Get(section, key) == value) return;
            var eq = lines[index].IndexOf('=');
            var start = eq + 1;
            while (start < lines[index].Length && char.IsWhiteSpace(lines[index][start])) start++;
            lines[index] = lines[index][..start] + value;
            return;
        }
        var header = lines.FindIndex(l => l.Trim() == $"[{section}]");
        if (header < 0)
        {
            if (lines.Count > 0 && lines[^1].Length != 0) lines.Add("");
            lines.Add($"[{section}]");
            lines.Add(key + "=" + value);
            return;
        }
        var end = header + 1;
        while (end < lines.Count && !lines[end].TrimStart().StartsWith('[')) end++;
        lines.Insert(end, key + "=" + value);
    }

    public void SetExplicit(string section, string key, string value)
    {
        Set(section, key + "\\default", "false");
        Set(section, key, value);
    }

    private int Find(string section, string key)
    {
        var active = "";
        var found = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith('[') && line.EndsWith(']')) { active = line[1..^1]; continue; }
            if (active != section || line.StartsWith(';') || line.StartsWith('#')) continue;
            var eq = line.IndexOf('=');
            if (eq < 0 || line[..eq].Trim() != key) continue;
            if (found >= 0) throw new InvalidDataException($"Duplicate setting [{section}] {key}.");
            found = i;
        }
        return found;
    }

    public override string ToString() => string.Join(newline, lines) + (trailingNewline ? newline : "");
}
