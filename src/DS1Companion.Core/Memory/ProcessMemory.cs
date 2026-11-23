using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DS1Companion.Core.Memory;

/// <summary>Абстракция над чтением памяти процесса (подменяется в тестах). Только чтение.</summary>
public interface IProcessMemory : IDisposable
{
    bool IsAlive { get; }
    long ModuleBase { get; }
    int ModuleSize { get; }
    bool TryRead(long address, Span<byte> buffer);
}

public static class ProcessMemoryExtensions
{
    public static long? ReadInt64(this IProcessMemory m, long address)
    {
        Span<byte> b = stackalloc byte[8];
        return m.TryRead(address, b) ? BinaryPrimitives.ReadInt64LittleEndian(b) : null;
    }

    public static int? ReadInt32(this IProcessMemory m, long address)
    {
        Span<byte> b = stackalloc byte[4];
        return m.TryRead(address, b) ? BinaryPrimitives.ReadInt32LittleEndian(b) : null;
    }

    public static byte? ReadByte(this IProcessMemory m, long address)
    {
        Span<byte> b = stackalloc byte[1];
        return m.TryRead(address, b) ? b[0] : null;
    }

    public static string? ReadUtf16(this IProcessMemory m, long address, int maxChars)
    {
        var buf = new byte[maxChars * 2];
        if (!m.TryRead(address, buf)) return null;
        var s = Encoding.Unicode.GetString(buf);
        var end = s.IndexOf('\0');
        return end >= 0 ? s[..end] : s;
    }
}

/// <summary>
/// Read-only доступ к памяти процесса через WinAPI.
/// Открываем ТОЛЬКО с PROCESS_VM_READ | PROCESS_QUERY_INFORMATION. Функции записи в память
/// (WriteProcessMemory/VirtualProtectEx) намеренно не импортируются — это проверяется тестом.
/// </summary>
public sealed class WindowsProcessMemory : IProcessMemory
{
    private const uint PROCESS_VM_READ = 0x0010;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;
    public const uint AccessRights = PROCESS_VM_READ | PROCESS_QUERY_INFORMATION;

    private readonly Process _process;
    private IntPtr _handle;

    public long ModuleBase { get; }
    public int ModuleSize { get; }
    public int ProcessId => _process.Id;
    public bool IsAlive
    {
        get
        {
            try { return _handle != IntPtr.Zero && !_process.HasExited; }
            catch { return false; }
        }
    }

    private WindowsProcessMemory(Process process, IntPtr handle, long moduleBase, int moduleSize)
    {
        _process = process;
        _handle = handle;
        ModuleBase = moduleBase;
        ModuleSize = moduleSize;
    }

    /// <summary>Ищет процесс по имени (без .exe) и открывает его на чтение. null — не найден/нет доступа.</summary>
    public static WindowsProcessMemory? TryOpen(string processName)
    {
        if (!OperatingSystem.IsWindows()) return null;
        foreach (var p in Process.GetProcessesByName(processName))
        {
            try
            {
                if (p.HasExited) continue;
                var module = p.MainModule;
                if (module is null) continue;
                var handle = OpenProcess(AccessRights, false, p.Id);
                if (handle == IntPtr.Zero) continue;
                return new WindowsProcessMemory(p, handle, module.BaseAddress.ToInt64(), module.ModuleMemorySize);
            }
            catch (Exception)
            {
                // Нет прав (игра запущена от администратора) или процесс завершился между вызовами.
            }
        }
        return null;
    }

    public bool TryRead(long address, Span<byte> buffer)
    {
        if (_handle == IntPtr.Zero || address <= 0) return false;
        var managed = new byte[buffer.Length];
        var ok = ReadProcessMemory(_handle, new IntPtr(address), managed, managed.Length, out var read);
        if (!ok || read.ToInt64() != buffer.Length) return false;
        managed.CopyTo(buffer);
        return true;
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            CloseHandle(_handle);
            _handle = IntPtr.Zero;
        }
        _process.Dispose();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, [Out] byte[] lpBuffer, int dwSize, out IntPtr lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}
