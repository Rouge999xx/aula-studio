using System.Collections.Concurrent;

namespace AulaStudio.Knob;

/// <summary>
/// A troubleshooting log of what the knob did: %APPDATA%\AulaStudio\knob.log. It only ever records volume/mute keys and
/// knob clicks, never other keys. Lines are written on a background thread so the keyboard hook never waits for the disk.
/// </summary>
public static class KnobLog
{
    const long MaxBytes = 512 * 1024;

    public static readonly string FilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AulaStudio", "knob.log");

    static readonly BlockingCollection<string> pending = new();

    static KnobLog() => new Thread(WriteLoop) { IsBackground = true, Name = "Knob log" }.Start();

    public static void Write(string line) => pending.Add($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {line}");

    static void WriteLoop()
    {
        foreach (string line in pending.GetConsumingEnumerable())
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > MaxBytes)   // keep the newest half
                {
                    var lines = File.ReadAllLines(FilePath);
                    File.WriteAllLines(FilePath, lines.Skip(lines.Length / 2));
                }
                File.AppendAllText(FilePath, line + Environment.NewLine);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
