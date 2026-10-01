using System.Security.Cryptography;
using System.Text;

namespace AutoEmuControllerConfig;

internal sealed record FileChange(string Path, byte[] Before, byte[] After)
{
    public static FileChange Prepare(string path, Func<string, string> transform)
    {
        var before = File.ReadAllBytes(path);
        using var reader = new StreamReader(new MemoryStream(before), new UTF8Encoding(false, true), true);
        var text = reader.ReadToEnd();
        var encoding = reader.CurrentEncoding;
        var changed = transform(text);
        if (text == changed) return new(path, before, before);
        var preamble = encoding.GetPreamble();
        var bom = preamble.Length > 0 && before.AsSpan().StartsWith(preamble);
        var content = encoding.GetBytes(changed);
        return new(path, before, bom ? [.. preamble, .. content] : content);
    }

    public bool HasChanges => !Before.AsSpan().SequenceEqual(After);
}

internal static class FileTransaction
{
    public static int Apply(IEnumerable<FileChange> changes, string backupRoot, Action? beforeCommit = null)
    {
        var pending = changes.Where(c => c.HasChanges).ToArray();
        if (pending.Select(c => System.IO.Path.GetFullPath(c.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != pending.Length)
            throw new InvalidOperationException("Duplicate configuration file in write plan.");
        var staged = new Dictionary<FileChange, string>();
        var committed = new List<FileChange>();
        try
        {
            // Prove that every destination is writable before replacing any config.
            foreach (var change in pending)
            {
                VerifyUnchanged(change, change.Before);
                var temp = change.Path + ".aec-" + Guid.NewGuid().ToString("N") + ".tmp";
                staged.Add(change, temp);
                WriteDurably(temp, change.After);
            }
            if (pending.Length > 0)
            {
                var backupDir = System.IO.Path.Combine(backupRoot, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff"));
                Directory.CreateDirectory(backupDir);
                foreach (var change in pending)
                {
                    var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(System.IO.Path.GetFullPath(change.Path))))[..16];
                    WriteDurably(System.IO.Path.Combine(backupDir, id + "-" + System.IO.Path.GetFileName(change.Path)), change.Before);
                    File.WriteAllText(System.IO.Path.Combine(backupDir, id + ".path.txt"), change.Path);
                }
            }
            beforeCommit?.Invoke();
            foreach (var change in pending)
            {
                VerifyUnchanged(change, change.Before);
                File.Move(staged[change], change.Path, overwrite: true);
                committed.Add(change);
            }
            return committed.Count;
        }
        catch (Exception original)
        {
            var errors = new List<Exception> { original };
            foreach (var change in committed.AsEnumerable().Reverse())
            {
                try
                {
                    VerifyUnchanged(change, change.After);
                    var temp = staged[change];
                    WriteDurably(temp, change.Before);
                    File.Move(temp, change.Path, overwrite: true);
                }
                catch (Exception rollback) { errors.Add(rollback); }
            }
            if (errors.Count > 1) throw new AggregateException("Write failed; some files need restoration from backups.", errors);
            throw;
        }
        finally
        {
            foreach (var temp in staged.Values)
                try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static void VerifyUnchanged(FileChange change, byte[] expected)
    {
        if (!File.ReadAllBytes(change.Path).AsSpan().SequenceEqual(expected))
            throw new IOException($"Configuration changed during preparation: {change.Path}. Retry with the emulator closed.");
    }

    private static void WriteDurably(string path, byte[] bytes)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.Write(bytes);
        file.Flush(flushToDisk: true);
    }
}
