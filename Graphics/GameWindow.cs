using Raylib_cs;

namespace WonderBoy.Graphics;

// Config.cfg (carpeta del ejecutable). Lineas "Clave=Valor", sin distinguir mayusculas:
//   Fullscreen=NO        ventana de 256x224 x 2 (512x448), relacion de aspecto del original
//   Fullscreen=ORIGINAL  pantalla completa (ventana sin bordes del tamano del monitor) con la
//                        misma relacion de aspecto; barras negras donde sobre espacio
//   Fullscreen=WIDESCREEN  como ORIGINAL, pero en los niveles (--play) muestra mas columnas del mapa
//                        a izquierda y derecha hasta llenar el ancho, sin estirar (WideView); el fondo
//                        de la pantalla de titulo tambien llena el ancho (TitleRenderer.Render con width)
//   ShowFps=T            fps en el titulo ("WonderBoy PC - Fps XX"); con ORIGINAL tambien arriba
//                        a la izquierda con la fuente del juego (tiles $30-$39), amarillo al 80%
//   ShowFps=F            sin fps en el titulo ni en pantalla
//                        (con WIDESCREEN los fps en pantalla van arriba a la derecha)
//   CorrectLicensed=T    corrige el texto de la licencia "LISENCED BY" -> "LICENSED BY" (todos los modos)
//   CorrectLicensed=F    texto original de la ROM (defecto)
//   LevelStart=N         (oculto, opcional) solo --play: cada partida empieza en la ronda N (1..32;
//                        3 = area 1 ronda 3) con $C301=N-1, como --round-load. Fuera de 1..32 se ignora.
//                        Nunca se escribe en el archivo; sin la linea el arranque es el normal.
//   CheckPoint=N         (oculto, opcional) solo --play: la primera ronda de cada partida empieza en el
//                        cartel N (1..4) como al continuar tras perder una vida ($1B76): $C30B=$0100/$1000/
//                        $2000/$3000 y $C30F=$00/$0F/$1F/$2F. GOAL u otro valor se ignora: la ROM no tiene
//                        punto de reinicio en la meta (tras el cartel 4 siempre vuelve al 4). Nunca se escribe.
//   OpacityBars=N        (opcional) opacidad 0..100 del fondo negro de las barras del HUD (puntuacion,
//                        vidas / vitalidad e inventario) en todas las pantallas (TitleRenderer.BarOpacity).
//                        Sin la linea o fuera de 0..100 = 100 (opaco, como el original). Se escribe desde el menu.
//   LivesAtStart=N       vidas al empezar cada partida (1..6, defecto 3): $C027, que $1388 copia a $C302.
//   InfinityCredit=T/F   T = sin monedas: START en attract (o en la cuenta atras de continuar) pone el credito.
//   SoundVolume=N        volumen del juego 0..100 (defecto 80), AudioStream de GameAudio.
//   Filters=NONE / lista (opcional) filtros de pantalla separados por comas: BILINEAR, SHARPBILINEAR, XBR,
//                        SCALE3X, FSR, VIBRANCE, SCANLINES (ScreenFilters.cs). Sin la linea = NONE. Se escribe desde el menu.
//   MaxFPS=N             (opcional) fotogramas por segundo de la ventana, 60..1000 (defecto 60). La logica sigue a 60
//                        pasos por segundo (FrameClock.cs); con N > 60 se repite el dibujo del ultimo paso, sin interpolar.
//                        Sin la linea o fuera de rango = 60. No se crea: el menu la escribe solo si cambia.
//   VSync=T/F            (opcional) sincronia vertical con el monitor (defecto F). No se crea: el menu la escribe solo si cambia.
// Si el archivo no existe se crea con Fullscreen=NO, ShowFps=T, CorrectLicensed=F, LivesAtStart=3,
// InfinityCredit=F y SoundVolume=80; si faltan LivesAtStart / InfinityCredit / SoundVolume
// se anaden con su defecto. Valores no reconocidos -> defecto. Menu de opciones (Tab, OptionsMenu): cambia los valores
// y llama a Save, que reescribe solo sus claves (LevelStart y CheckPoint y otras lineas quedan como estan).
public sealed class WindowConfig
{
    public const string FileName = "Config.cfg";
    public const int MaxLives = 6;                       // $14D0 dibuja como mucho 6 iconos de vida
    public bool Fullscreen { get; private set; }
    public bool Widescreen { get; private set; }
    public bool ShowFps { get; set; } = true;
    public bool CorrectLicensed { get; set; }
    public int LevelStart { get; private set; }          // 0 = sin la linea (arranque normal); 1..32 = ronda inicial
    public int CheckPoint { get; private set; }          // 0 = sin la linea (arranque normal); 1..4 = cartel inicial
    public int OpacityBars { get; set; } = 100;          // 100 = sin la linea (fondo de las barras opaco)
    public int LivesAtStart { get; set; } = 3;           // 1..MaxLives
    public bool InfinityCredit { get; set; }
    public int SoundVolume { get; set; } = 80;           // 0..100
    public ScreenFilter Filters { get; set; }            // filtros de pantalla (GameWindow.Draw / FilterRenderer)
    public const int DefaultMaxFps = 60, MinMaxFps = 60, MaxMaxFps = 1000;
    public int MaxFps { get; set; } = DefaultMaxFps;     // fps de la ventana (FrameClock)
    public bool VSync { get; set; }                      // sincronia vertical (GameWindow.ApplyVSync)
    bool maxFpsLine, vsyncLine;                          // la linea existe en Config.cfg (Save la mantiene)
    // Valores del menu (Graphics > Max FPS).
    public static readonly int[] MaxFpsSteps = { 60, 75, 90, 100, 120, 144, 165, 180, 200, 240, 300, 360 };
    public static int NextMaxFps(int cur, int d)
    {
        if (d > 0) { foreach (int v in MaxFpsSteps) if (v > cur) return v; return MaxFpsSteps[^1] > cur ? MaxFpsSteps[^1] : cur; }
        for (int i = MaxFpsSteps.Length - 1; i >= 0; i--) if (MaxFpsSteps[i] < cur) return MaxFpsSteps[i];
        return MaxFpsSteps[0];
    }
    public string Path { get; private set; } = "";
    public bool Created { get; private set; }

    // Modo de pantalla: 0 = NO (ventana), 1 = ORIGINAL, 2 = WIDESCREEN.
    public int DisplayMode
    {
        get => Widescreen ? 2 : Fullscreen ? 1 : 0;
        set { Fullscreen = value != 0; Widescreen = value == 2; }
    }
    public string DisplayModeText => DisplayMode switch { 2 => "WIDESCREEN", 1 => "ORIGINAL", _ => "NO" };

    static string TF(bool b) => b ? "T" : "F";

    public static WindowConfig Load()
    {
        var cfg = new WindowConfig { Path = System.IO.Path.Combine(AppContext.BaseDirectory, FileName) };
        if (!File.Exists(cfg.Path))
        {
            File.WriteAllText(cfg.Path, "Fullscreen=NO" + Environment.NewLine + "ShowFps=T" + Environment.NewLine
                                     + "CorrectLicensed=F" + Environment.NewLine + "LivesAtStart=3" + Environment.NewLine
                                     + "InfinityCredit=F" + Environment.NewLine + "SoundVolume=80" + Environment.NewLine);
            cfg.Created = true;
        }
        bool hasLives = false, hasCredit = false, hasVolume = false;
        foreach (var raw in File.ReadAllLines(cfg.Path))
        {
            int eq = raw.IndexOf('=');
            if (eq < 0) continue;
            string key = raw[..eq].Trim().ToUpperInvariant(), val = raw[(eq + 1)..].Trim().ToUpperInvariant();
            if (key == "FULLSCREEN" && (val == "NO" || val == "ORIGINAL" || val == "WIDESCREEN"))
            {
                cfg.Fullscreen = val != "NO";
                cfg.Widescreen = val == "WIDESCREEN";   // como ORIGINAL salvo en niveles (WideView)
            }
            else if (key == "SHOWFPS" && (val == "T" || val == "F")) cfg.ShowFps = val == "T";
            else if (key == "CORRECTLICENSED" && (val == "T" || val == "F")) cfg.CorrectLicensed = val == "T";
            else if (key == "LEVELSTART" && int.TryParse(val, out int ls) && ls >= 1 && ls <= 32) cfg.LevelStart = ls;
            else if (key == "CHECKPOINT" && int.TryParse(val, out int cp) && cp >= 1 && cp <= 4) cfg.CheckPoint = cp;
            else if (key == "OPACITYBARS" && int.TryParse(val, out int ob) && ob >= 0 && ob <= 100) cfg.OpacityBars = ob;
            else if (key == "FILTERS") cfg.Filters = ScreenFilters.Parse(val);
            else if (key == "MAXFPS")
            {
                cfg.maxFpsLine = true;
                if (int.TryParse(val, out int mf) && mf >= MinMaxFps && mf <= MaxMaxFps) cfg.MaxFps = mf;
            }
            else if (key == "VSYNC")
            {
                cfg.vsyncLine = true;
                if (val == "T" || val == "F") cfg.VSync = val == "T";
            }
            else if (key == "LIVESATSTART")
            {
                hasLives = true;
                if (int.TryParse(val, out int lv) && lv >= 1 && lv <= MaxLives) cfg.LivesAtStart = lv;
            }
            else if (key == "INFINITYCREDIT")
            {
                hasCredit = true;
                if (val == "T" || val == "F") cfg.InfinityCredit = val == "T";
            }
            else if (key == "SOUNDVOLUME")
            {
                hasVolume = true;
                if (int.TryParse(val, out int sv) && sv >= 0 && sv <= 100) cfg.SoundVolume = sv;
            }
        }
        var missing = new List<(string, string)>();
        if (!hasLives) missing.Add(("LivesAtStart", cfg.LivesAtStart.ToString()));
        if (!hasCredit) missing.Add(("InfinityCredit", TF(cfg.InfinityCredit)));
        if (!hasVolume) missing.Add(("SoundVolume", cfg.SoundVolume.ToString()));
        if (missing.Count > 0) cfg.Write(missing);
        return cfg;
    }

    // Guarda las opciones del menu (las ocultas LevelStart / CheckPoint no se tocan).
    // MaxFPS / VSync: solo si la linea ya existe o el valor no es el defecto (no se crean sin cambiarlas).
    public void Save()
    {
        var v = new List<(string, string)>
        {
            ("Fullscreen", DisplayModeText), ("ShowFps", TF(ShowFps)), ("CorrectLicensed", TF(CorrectLicensed)),
            ("OpacityBars", OpacityBars.ToString()), ("LivesAtStart", LivesAtStart.ToString()),
            ("InfinityCredit", TF(InfinityCredit)), ("SoundVolume", SoundVolume.ToString()),
            ("Filters", ScreenFilters.ToText(Filters)),
        };
        if (maxFpsLine || MaxFps != DefaultMaxFps) { v.Add(("MaxFPS", MaxFps.ToString())); maxFpsLine = true; }
        if (vsyncLine || VSync) { v.Add(("VSync", TF(VSync))); vsyncLine = true; }
        Write(v);
    }

    // Sustituye la linea de cada clave (sin distinguir mayusculas) o la anade al final; el resto queda igual.
    void Write(IEnumerable<(string key, string val)> values)
    {
        try
        {
            var lines = File.Exists(Path) ? File.ReadAllLines(Path).ToList() : new List<string>();
            foreach (var (key, val) in values)
            {
                int i = lines.FindIndex(l =>
                {
                    int eq = l.IndexOf('=');
                    return eq >= 0 && l[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase);
                });
                if (i >= 0) lines[i] = key + "=" + val;
                else lines.Add(key + "=" + val);
            }
            File.WriteAllLines(Path, lines);
        }
        catch (IOException) { }                      // archivo bloqueado o de solo lectura: el valor vale para esta sesion
        catch (UnauthorizedAccessException) { }
    }
}

// Ventana de --attract y --play segun Config.cfg: abre, titulo con fps, dibujo escalado y centrado
// de la imagen 512x224 de TitleRenderer (pixel logico 2x1 -> 256x224 con aspecto 8:7) y fps en pantalla.
public sealed class GameWindow
{
    const int BaseW = TitleRenderer.Width, BaseH = TitleRenderer.Height * 2;   // 512x448 = 256x224 x 2
    static readonly Color FpsColor = new(255, 255, 0, 204);                     // amarillo, 80%
    static readonly Color FpsShadow = new(0, 0, 0, 204);                        // pixel 7 de la fuente
    readonly WindowConfig cfg;
    readonly byte[][] tiles;
    string title = "";
    FilterRenderer? filters;   // se crea al primer fotograma con algun filtro activo

    GameWindow(WindowConfig cfg, byte[][] tiles) { this.cfg = cfg; this.tiles = tiles; }

    public static GameWindow Open(WindowConfig cfg, byte[][] tiles)
    {
        Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);   // oculta hasta el primer fotograma
        Raylib.InitWindow(BaseW, BaseH, "WonderBoy PC");
        SetIcon();
        if (cfg.Fullscreen) Raylib.ToggleBorderlessWindowed();
        var w = new GameWindow(cfg, tiles) { borderless = cfg.Fullscreen };
        w.UpdateTitle();
        w.ApplyFrameRate();
        return w;
    }

    // MaxFPS y VSync de cfg (al abrir y desde el menu, efecto inmediato). VSync: SetWindowState / ClearWindowState
    // (raylib cambia el intervalo de intercambio). La logica sigue a 60 pasos por segundo (FrameClock).
    // Tope real = min(MaxFPS, refresco del monitor de la ventana): mismo numero en ventana y en pantalla completa.
    public void ApplyFrameRate()
    {
        int hz = Raylib.GetMonitorRefreshRate(Raylib.GetCurrentMonitor());
        int fps = hz > 0 ? Math.Min(cfg.MaxFps, hz) : cfg.MaxFps;   // hz=0 si raylib no lo sabe
        Raylib.SetTargetFPS(fps);
        if (cfg.VSync) Raylib.SetWindowState(ConfigFlags.VSyncHint);
        else Raylib.ClearWindowState(ConfigFlags.VSyncHint);
    }

    bool borderless;   // estado actual de la ventana (ToggleBorderlessWindowed)

    // Menu de opciones: aplica al momento el modo de pantalla de cfg (NO = ventana 512x448; ORIGINAL y
    // WIDESCREEN = ventana sin bordes del tamano del monitor; WIDESCREEN solo cambia WideWidth).
    public void ApplyDisplayMode()
    {
        if (cfg.Fullscreen == borderless) return;
        Raylib.ToggleBorderlessWindowed();
        borderless = cfg.Fullscreen;
    }

    // Icono de la ventana (barra de titulo y de tareas): Icon/wonderboy.png incrustado en el exe.
    public static void SetIcon()
    {
        using var s = typeof(GameWindow).Assembly.GetManifestResourceStream("WonderBoy.Icon.png");
        if (s == null) return;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        var img = Raylib.LoadImageFromMemory(".png", ms.ToArray());
        Raylib.SetWindowIcon(img);
        Raylib.UnloadImage(img);
    }

    bool shown;

    // Llamar tras EndDrawing: la primera vez muestra la ventana (solo una vez).
    public void ShowAfterFirstFrame()
    {
        if (shown) return;
        shown = true;
        Raylib.ClearWindowState(ConfigFlags.HiddenWindow);
    }

    // Contador propio de fps reales: fotogramas presentados en cada segundo de tiempo real.
    readonly System.Diagnostics.Stopwatch fpsClock = new();
    long fpsWindowStart;                                         // inicio (ticks) del segundo en curso
    int fpsFrames;                                               // fotogramas presentados en el segundo en curso
    int realFps;                                                 // total del ultimo segundo completo

    // Llamar justo tras cada EndDrawing: suma 1; al cumplirse cada segundo guarda el total y reinicia.
    public void CountFrame()
    {
        if (!fpsClock.IsRunning) { fpsClock.Start(); fpsWindowStart = 0; }
        fpsFrames++;
        long now = fpsClock.ElapsedTicks, sec = System.Diagnostics.Stopwatch.Frequency;
        if (now - fpsWindowStart < sec) return;
        realFps = fpsFrames;
        fpsFrames = 0;
        fpsWindowStart += sec;                                   // segundos exactos, sin deriva
        if (now - fpsWindowStart >= sec) fpsWindowStart = now;   // tras una pausa larga, empezar de nuevo
    }

    // Titulo "WonderBoy PC - Fps XX" (o "WonderBoy PC" con ShowFps=F); solo se cambia si varia.
    public void UpdateTitle()
    {
        string t = cfg.ShowFps ? $"WonderBoy PC - Fps {realFps}" : "WonderBoy PC";
        if (t == title) return;
        title = t;
        Raylib.SetWindowTitle(t);
    }

    // WIDESCREEN: ancho (espacio 512, par) de la imagen que llena la pantalla con la misma escala
    // uniforme que ORIGINAL (altura 448); 0 si no aplica o la pantalla no es mas ancha que 8:7.
    public int WideWidth
    {
        get
        {
            if (!cfg.Widescreen) return 0;
            int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
            if (sh <= 0) return 0;
            int w = ((int)MathF.Ceiling(sw * BaseH / (float)sh) + 3) & ~3;
            return w > BaseW ? w : 0;
        }
    }

    // Imagen del juego escalada a la ventana manteniendo el aspecto, centrada (barras negras) + fps.
    // La imagen puede ser mas ancha que 512 (WideView): misma escala, ocupa todo el ancho.
    public void Draw(Texture2D tex)
    {
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        float s = tex.Width > BaseW ? sh / (float)BaseH                   // ancha: llena alto, sobra <4 px
                                    : MathF.Min(sw / (float)BaseW, sh / (float)BaseH);
        float w = tex.Width * s, h = BaseH * s;
        // Ancha: x 0 de la imagen (ScreenBounds.Left) = borde izquierdo de la pantalla; lo que sobra (<4 px) a la derecha.
        float dx = tex.Width > BaseW ? 0f : (sw - w) / 2;
        var dst = new Rectangle(dx, (sh - h) / 2, w, h);
        if (cfg.Filters == ScreenFilter.None)
            Raylib.DrawTexturePro(tex, new Rectangle(0, 0, tex.Width, tex.Height), dst, System.Numerics.Vector2.Zero, 0f, Color.White);
        else (filters ??= new FilterRenderer()).Draw(tex, dst, cfg.Filters);   // Filters= (menu GRAPHICS > Filters)
        if (cfg.ShowFps && cfg.Fullscreen) DrawFps(Math.Max(1, (int)(2 * s)));
    }

    // Digitos de la fuente del juego (tile $30 + d, 8x8): pixeles 1-6 en amarillo, 7 (sombra) en negro.
    // ORIGINAL: arriba a la izquierda; WIDESCREEN: abajo a la derecha (mismo margen de 2 px logicos;
    // la barra de vidas/inventario esta arriba, asi que no la tapa).
    void DrawFps(int px)
    {
        string fps = realFps.ToString();
        bool wide = cfg.Widescreen;
        int x0 = wide ? Raylib.GetScreenWidth() - px * 2 - fps.Length * 8 * px : px * 2;
        int y0 = wide ? Raylib.GetScreenHeight() - px * 2 - 8 * px : px * 2;
        for (int c = 0; c < fps.Length; c++)
        {
            var t = tiles[0x30 + fps[c] - '0'];
            for (int i = 0; i < 64; i++)
            {
                if (t[i] == 0) continue;
                Raylib.DrawRectangle(x0 + (c * 8 + (i & 7)) * px, y0 + (i >> 3) * px, px, px, t[i] == 7 ? FpsShadow : FpsColor);
            }
        }
    }
}
