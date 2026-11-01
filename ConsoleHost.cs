using System.Runtime.InteropServices;

namespace WonderBoy;

// El ejecutable es WinExe: con doble clic no abre ventana de consola.
// Los modos de linea de comandos (--check-*, --round-load, ...) se enganchan a la
// consola del terminal que los lanzo; si no hay ninguna, crean una propia.
static partial class ConsoleHost
{
    const int AttachParentProcess = -1;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(int processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllocConsole();

    public static void Attach(bool allocIfMissing)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (AttachConsole(AttachParentProcess)) return;
        if (allocIfMissing) AllocConsole();
    }
}
