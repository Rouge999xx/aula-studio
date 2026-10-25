using System.Text.Json;
using System.Text.Json.Serialization;
using AulaStudio.Protocol;

namespace AulaStudio.Backup;

/// <summary>A full copy of the keyboard's configuration memory as raw hex.</summary>
public sealed class KeyboardBackup
{
    public DateTimeOffset Created { get; set; }
    public string Note { get; set; } = "";
    public string? Device { get; set; }
    public string Layer0 { get; set; } = "";
    public string Layer1 { get; set; } = "";
    public string? Led { get; set; }
    public string? Colors { get; set; }

    [JsonIgnore] public string FilePath { get; set; } = "";
    /// <summary>The very first backup, taken before AULA Studio ever wrote to the keyboard.</summary>
    [JsonIgnore] public bool IsInitial => Path.GetFileNameWithoutExtension(FilePath).EndsWith("-initial", StringComparison.OrdinalIgnoreCase);

    public KeyMatrix BaseLayer => KeyMatrix.FromHex(Layer0);
    public KeyMatrix FnLayer => KeyMatrix.FromHex(Layer1);

    /// <summary>Checks the backup holds two complete, intact key tables before anything is restored from it.</summary>
    public bool IsValid(out string problem)
    {
        try
        {
            var b = BaseLayer;
            var f = FnLayer;
            if (!b.HasTrailer || !f.HasTrailer) { problem = "the key tables are incomplete"; return false; }
            if (b[KeyMatrix.FnSlot] != KeyEntry.FnKey) { problem = "its Fn key is missing"; return false; }
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            problem = "the file is damaged";
            return false;
        }
        problem = "";
        return true;
    }
}

public sealed record KeyboardState(KeyMatrix Base, KeyMatrix Fn, byte[]? Led, byte[]? Colors);

public static class BackupStore
{
    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AulaStudio", "backups");

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static KeyboardBackup Save(KeyboardState state, string note, string tag)
    {
        Directory.CreateDirectory(Folder);
        var backup = new KeyboardBackup
        {
            Created = DateTimeOffset.Now,
            Note = note,
            Device = "AULA F75 wired 258A:010C",
            Layer0 = state.Base.ToHex(),
            Layer1 = state.Fn.ToHex(),
            Led = state.Led is null ? null : Convert.ToHexString(state.Led),
            Colors = state.Colors is null ? null : Convert.ToHexString(state.Colors),
        };
        string path = Path.Combine(Folder, $"{backup.Created:yyyyMMdd-HHmmss-fff}-{tag}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(backup, Json));
        backup.FilePath = path;
        Prune();
        return backup;
    }

    /// <summary>Newest first. Unreadable files are skipped rather than failing the whole list.</summary>
    public static List<KeyboardBackup> List()
    {
        if (!Directory.Exists(Folder)) return new();
        var list = new List<KeyboardBackup>();
        foreach (string file in Directory.EnumerateFiles(Folder, "*.json"))
        {
            try
            {
                var backup = JsonSerializer.Deserialize<KeyboardBackup>(File.ReadAllText(file), Json);
                if (backup is null) continue;
                backup.FilePath = file;
                list.Add(backup);
            }
            catch (JsonException) { }
            catch (IOException) { }
        }
        return list.OrderByDescending(b => b.Created).ToList();
    }

    /// <summary>The keyboard as it was before AULA Studio changed anything: the reference for "Reset to original".</summary>
    public static KeyboardBackup? Initial()
    {
        var all = List();
        return all.LastOrDefault(b => b.IsInitial) ?? all.LastOrDefault();
    }

    public static bool MatchesLatest(KeyboardState state)
    {
        var latest = List().FirstOrDefault();
        return latest is not null && latest.Layer0 == state.Base.ToHex() && latest.Layer1 == state.Fn.ToHex();
    }

    /// <summary>Keeps the newest 60 backups. The initial one (shown as Original) is never deleted.</summary>
    static void Prune()
    {
        foreach (var old in List().Where(b => !b.IsInitial).Skip(60))
        {
            try { File.Delete(old.FilePath); } catch (IOException) { }
        }
    }
}
