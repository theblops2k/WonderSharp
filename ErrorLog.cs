using System.Text;
using Raylib_cs;
using WonderBoy.Game;

namespace WonderBoy;

// Registro de errores de las ventanas (--play / --attract): Wonderboy_errors.log junto al ejecutable
// (AppContext.BaseDirectory). Se anota si salta una excepcion no controlada o si el juego se bloquea:
//   - Unported: el port llega a una rutina sin portar y deja de avanzar.
//   - Durante un nivel (LevelTilesDrawn y sin LevelLoading), el sprite del jugador no cambia durante
//     FreezeFrames fotogramas seguidos. Sprite = puntero de graficos del fotograma de animacion del
//     jugador, (ix+$1C/$1D) del objeto $C840 = $C85C/$C85D (lo escribe $2009, ObjectRoutines.Frame).
//     El jugador siempre se esta moviendo, asi que 5 s con el mismo sprite = bloqueo.
// Cada entrada se anade al final del archivo con fecha, motivo, excepcion, estado y ultimos fotogramas.
sealed class ErrorLog
{
    public const string FileName = "Wonderboy_errors.log";
    public const int FreezeFrames = 300;    // 5 s a 60 fps con el mismo sprite del jugador = bloqueo
    const int HistoryLen = 30;              // fotogramas previos que se copian al log
    const int SpriteLo = 0xC85C, SpriteHi = 0xC85D;   // jugador $C840 + $1C/$1D

    static readonly int[] Addrs = { 0xC07E, 0xC07F, 0xC081, 0xC442, 0xC850, 0xC852, 0xC302 };

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, FileName);

    readonly string mode;
    readonly TitleRom game;
    readonly Queue<string> history = new();
    int lastFrame;
    int lastSprite = -1;
    int sameFrames;

    public ErrorLog(string mode, TitleRom game)
    {
        this.mode = mode;
        this.game = game;
        // Respaldo: excepciones fuera del bucle (p. ej. en el hilo de audio).
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write("excepcion no controlada (fuera del bucle de la ventana)", e.ExceptionObject as Exception, lastFrame);
    }

    // Tras cada Step(): guarda el estado del fotograma y devuelve el motivo si el juego esta bloqueado.
    public string? Check(int frame)
    {
        lastFrame = frame;
        history.Enqueue($"f{frame}: {StateLine()}");
        if (history.Count > HistoryLen) history.Dequeue();

        if (game.Unported != null)
            return $"bloqueo: rutina sin portar {game.Unported} (el juego no puede avanzar)";

        // Fuera de un nivel (titulo, attract, carga de ronda) no se cuenta: se reinicia.
        if (!game.LevelTilesDrawn || game.LevelLoading)
        {
            lastSprite = -1;
            sameFrames = 0;
            return null;
        }

        int s = PlayerSprite();
        sameFrames = s == lastSprite ? sameFrames + 1 : 0;
        lastSprite = s;
        return sameFrames >= FreezeFrames
            ? $"bloqueo: el sprite del jugador (${SpriteLo:X4}/${SpriteHi:X4} = ${s:X4}) no cambia desde hace " +
              $"{sameFrames} fotogramas ({sameFrames / 60} s) durante el nivel"
            : null;
    }

    int PlayerSprite() => game[SpriteLo] | game[SpriteHi] << 8;

    // Anade una entrada a Wonderboy_errors.log. No lanza excepciones (el registro nunca rompe el juego).
    public void Write(string reason, Exception? ex, int frame)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine(new string('=', 78));
            sb.AppendLine($"Fecha:      {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            sb.AppendLine($"Modo:       {mode}   (args: {string.Join(' ', Environment.GetCommandLineArgs().Skip(1))})");
            sb.AppendLine($"Motivo:     {reason}");
            sb.AppendLine($"Fotograma:  {frame}");
            sb.AppendLine($"Sistema:    {Environment.OSVersion}, .NET {Environment.Version}, {(Environment.Is64BitProcess ? "64" : "32")} bits");
            if (ex != null)
            {
                sb.AppendLine($"Excepcion:  {ex.GetType().FullName}");
                sb.AppendLine($"Mensaje:    {ex.Message}");
                sb.AppendLine("Pila completa:");
                sb.AppendLine(ex.ToString());   // incluye excepciones internas y sus pilas
            }
            sb.AppendLine("Estado del juego:");
            sb.AppendLine("  " + SafeState());
            sb.AppendLine($"Ultimos {history.Count} fotogramas:");
            foreach (var line in history) sb.AppendLine("  " + line);
            sb.AppendLine();
            File.AppendAllText(FilePath, sb.ToString());
        }
        catch { /* sin disco o sin permisos: no hay nada mas que hacer */ }
    }

    // Pantalla de aviso tras detener el juego: hasta 10 s o una tecla. Si raylib falla, se omite.
    public static void ShowStopScreen(string reason)
    {
        try
        {
            if (!Raylib.IsWindowReady()) return;
            double end = Raylib.GetTime() + 10;
            while (!Raylib.WindowShouldClose() && Raylib.GetTime() < end && Raylib.GetKeyPressed() == 0)
            {
                Raylib.BeginDrawing();
                Raylib.ClearBackground(Color.Black);
                Raylib.DrawText("El juego se ha detenido.", 20, 20, 20, Color.White);
                Raylib.DrawText(reason, 20, 50, 10, Color.LightGray);
                Raylib.DrawText("Detalles en " + FilePath, 20, 70, 10, Color.LightGray);
                Raylib.DrawText("Pulsa una tecla para salir.", 20, 100, 20, Color.White);
                Raylib.EndDrawing();
            }
        }
        catch { }
    }

    string SafeState()
    {
        try
        {
            return StateLine() + $" estado={game.State} modo={(game.GameMode ? "juego" : "attract")}" +
                   $" sin_portar={game.Unported ?? "ninguno"} fotogramas_iguales={sameFrames}";
        }
        catch (Exception e) { return "no disponible: " + e.Message; }
    }

    // $C07E $C07F $C081 $C442 $C850 $C852 $C302 en hex; X ($C850/1) e Y ($C852/3) tambien en 16 bits.
    string StateLine()
    {
        var sb = new StringBuilder();
        foreach (int a in Addrs) sb.Append($"${a:X4}={game[a]:X2} ");
        sb.Append($"X={game[0xC850] | game[0xC851] << 8:X4} Y={game[0xC852] | game[0xC853] << 8:X4}");
        sb.Append($" sprite={PlayerSprite():X4} nivel={(game.LevelTilesDrawn && !game.LevelLoading ? "si" : "no")}");
        return sb.ToString();
    }
}
