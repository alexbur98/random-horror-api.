using System.IO;

namespace DS1Companion.UI.Services;

public sealed class FileLogger
{
    private readonly string _path;
    private readonly object _gate = new();

    public FileLogger(string directory, string name)
    {
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, name);
        try { if (File.Exists(_path) && new FileInfo(_path).Length > 1_000_000) File.Delete(_path); } catch { /* ignore */ }
    }

    public void Write(string message)
    {
        lock (_gate)
        {
            try { File.AppendAllText(_path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}"); }
            catch { /* лог не должен ронять приложение */ }
        }
    }
}
