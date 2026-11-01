using System.IO.Compression;
using System.Numerics;
using System.Runtime.InteropServices;
using Raylib_cs;

namespace WonderBoy.Graphics;

// Ventana "No se encontro el juego original" (doble clic / --play). El juego se lee de files\wboy2u.zip
// junto al ejecutable (AppContext.BaseDirectory). Si falta, esta ventana deja buscarlo:
//   EXAMINAR: dialogo de Windows (GetOpenFileNameW, hilo STA) para elegir un .zip. Se comprueba que es
//             wboy2u (nombre, tamano y CRC32 de las 18 ROM que carga el juego); si no, aviso y no se copia.
//             Si es valido se copia a files\wboy2u.zip (crea la carpeta) y el juego continua.
//   SALIR:    cierra sin jugar (tambien Esc o la X de la ventana).
// Estilo de OptionsMenu (lienzo logico de 256x224, panel semitransparente, oro / ambar). La fuente del
// juego esta en la ROM que falta, asi que se dibuja una 8x8 propia con su estilo (degradado de arriba a
// abajo como los pens 1-6 y sombra negra como el pen 7). Fondo: cielo, nubes y suelo de la isla.
public sealed class RomSetupWindow
{
    public const string FolderName = "files", ZipName = "wboy2u.zip";
    public static string GameDir => Path.Combine(AppContext.BaseDirectory, FolderName);
    public static string GameZip => Path.Combine(GameDir, ZipName);

    const int LogW = 256, LogH = 224;                              // pantalla original
    const int PX = 12, PY = 32, PW = 232, PH = 164;                // panel
    const int BtnY = PY + 122, BtnW = 88, BtnH = 20;               // botones EXAMINAR / SALIR
    const int GroundY = 200;                                       // suelo del fondo
    const int FadeFrames = 20, DoneFrames = 90;                    // fundido de entrada; "CARGANDO..." 1,5 s

    static readonly Color Gold = new(255, 204, 51, 255), Amber = new(150, 80, 0, 255), Yellow = new(255, 226, 70, 255),
                          White = new(236, 236, 236, 255), Gray = new(168, 172, 196, 255), DimGray = new(120, 124, 150, 255),
                          Red = new(255, 64, 64, 255), Cyan = new(130, 220, 255, 255), OnColor = new(120, 255, 120, 255),
                          Highlight = new(255, 170, 0, 70), GoldLine = new(255, 204, 51, 160);

    // ROM de wboy2u que carga el juego (Program.cs --play, TileDecoder): nombre, tamano y CRC32 (MAME).
    static readonly (string Name, long Size, uint Crc)[] Roms =
    {
        ("ic129_02.bin", 8192, 0x32C4B709), ("ic130_03.bin", 8192, 0x56463EDE), ("ic131_04.bin", 8192, 0x775ED392),
        ("ic132_05.bin", 8192, 0x7B922708), ("epr-7591.133", 8192, 0x8379AA23), ("epr-7592.134", 8192, 0xC767A5D7),
        ("epr-7497.62", 8192, 0x08D609CA), ("epr-7496.61", 8192, 0x6F61FDF1), ("epr-7495.64", 8192, 0x6A0D2C2D),
        ("epr-7494.63", 8192, 0xA8E281C7), ("epr-7493.66", 8192, 0x89305DF4), ("epr-7492.65", 8192, 0x60F806B1),
        ("epr-7485.117", 16384, 0xC2891722), ("epr-7487.04", 16384, 0x2D3A421B), ("epr-7486.110", 16384, 0x8D622C50),
        ("epr-7488.05", 16384, 0x007C2F1B), ("pr-5317.76", 256, 0x648350B8), ("epr7498a.3", 8192, 0xC198205C),
    };

    enum Mode { Main, Browse, Notice, Done }

    readonly Texture2D font, icon;
    Mode mode = Mode.Main;
    int sel, hover = -1, frame, doneTimer;
    bool handCursor;
    string noticeTitle = "";
    string[] noticeLines = Array.Empty<string>();
    volatile bool browsing;                                        // dialogo de Windows abierto (otro hilo)
    volatile string? picked;                                       // zip elegido (null = cancelado)
    int u = 1, ox, oy;
    float alpha = 1f, mx, my;

    RomSetupWindow() { font = BuildFont(); icon = LoadIcon(); }

    // Abre la ventana y espera. true = files\wboy2u.zip ya copiado (seguir con el juego); false = salir.
    public static bool Run()
    {
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.InitWindow(LogW * 2, LogH * 2, "WonderBoy PC - No se encontró el juego original");
        GameWindow.SetIcon();
        Raylib.SetExitKey(KeyboardKey.Null);                       // Esc = SALIR (lo trata Update)
        Raylib.SetTargetFPS(60);
        var w = new RomSetupWindow();
        bool ok = w.Loop();
        Raylib.SetMouseCursor(MouseCursor.Default);
        Raylib.UnloadTexture(w.font);
        Raylib.UnloadTexture(w.icon);
        Raylib.CloseWindow();                                      // GameWindow.Open abre la del juego
        return ok;
    }

    bool Loop()
    {
        while (!Raylib.WindowShouldClose())
        {
            int r = Update();
            Raylib.BeginDrawing();
            Draw();
            Raylib.EndDrawing();
            if (r != 0) return r > 0;
        }
        return false;
    }

    // ---- comprobacion y copia ---------------------------------------------------------------

    // null = es wboy2u; si no, las lineas del aviso.
    static string[]? Check(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            foreach (var (name, size, crc) in Roms)
            {
                var e = zip.GetEntry(name);
                if (e != null && e.Length == size && Crc32(e) == crc) continue;
                return new[] { "ESE ZIP NO ES EL JUEGO", "WONDER BOY (WBOY2U).", "",
                               (e == null ? "FALTA: " : "NO COINCIDE: ") + name.ToUpperInvariant() };
            }
            return null;
        }
        catch (InvalidDataException) { return new[] { "ESE ARCHIVO NO ES UN ZIP", "O ESTA DANADO." }; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return new[] { "NO SE PUDO LEER", "EL ARCHIVO ELEGIDO." }; }
    }

    // Copia a files\wboy2u.zip (via .tmp: nunca queda un zip a medias). null = bien.
    static string[]? Copy(string src)
    {
        string tmp = GameZip + ".tmp";
        try
        {
            Directory.CreateDirectory(GameDir);
            File.Copy(src, tmp, true);
            File.Move(tmp, GameZip, true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(tmp); } catch (Exception) { }
            return new[] { "NO SE PUDO COPIAR EL ZIP", "A LA CARPETA FILES.", "", "COMPRUEBA LOS PERMISOS." };
        }
    }

    static readonly uint[] CrcTable = MakeCrcTable();

    static uint[] MakeCrcTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    // CRC32 de los datos descomprimidos (no el de la cabecera del zip).
    static uint Crc32(ZipArchiveEntry e)
    {
        using var s = e.Open();
        var buf = new byte[8192];
        uint c = 0xFFFFFFFFu;
        for (int n; (n = s.Read(buf, 0, buf.Length)) > 0;)
            for (int i = 0; i < n; i++) c = CrcTable[(c ^ buf[i]) & 0xFF] ^ (c >> 8);
        return ~c;
    }

    // ---- dialogo de Windows (Examinar) ------------------------------------------------------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct OpenFileNameW
    {
        public int lStructSize;
        public IntPtr hwndOwner, hInstance;
        public string lpstrFilter;
        public IntPtr lpstrCustomFilter;
        public int nMaxCustFilter, nFilterIndex;
        public IntPtr lpstrFile;
        public int nMaxFile;
        public IntPtr lpstrFileTitle;
        public int nMaxFileTitle;
        public string? lpstrInitialDir;
        public string lpstrTitle;
        public int Flags;
        public short nFileOffset, nFileExtension;
        public string lpstrDefExt;
        public IntPtr lCustData, lpfnHook, lpTemplateName, pvReserved;
        public int dwReserved, FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode)]
    static extern bool GetOpenFileNameW(ref OpenFileNameW ofn);

    const int OFN_HIDEREADONLY = 0x4, OFN_NOCHANGEDIR = 0x8, OFN_PATHMUSTEXIST = 0x800,
              OFN_FILEMUSTEXIST = 0x1000, OFN_EXPLORER = 0x80000;

    static string? AskZip(IntPtr owner)
    {
        const int Max = 4096;
        IntPtr buf = Marshal.AllocHGlobal(Max * 2);
        try
        {
            Marshal.WriteInt16(buf, 0);
            var ofn = new OpenFileNameW
            {
                lStructSize = Marshal.SizeOf<OpenFileNameW>(),
                hwndOwner = owner,
                lpstrFilter = "Archivo ZIP del juego (*.zip)\0*.zip\0Todos los archivos (*.*)\0*.*\0\0",
                nFilterIndex = 1,
                lpstrFile = buf,
                nMaxFile = Max,
                lpstrTitle = "Buscar el juego original (wboy2u.zip)",
                Flags = OFN_EXPLORER | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_HIDEREADONLY | OFN_NOCHANGEDIR,
                lpstrDefExt = "zip",
            };
            return GetOpenFileNameW(ref ofn) ? Marshal.PtrToStringUni(buf) : null;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    // El dialogo va en un hilo STA aparte: esta ventana sigue dibujandose mientras esta abierto.
    unsafe void StartBrowse()
    {
        IntPtr owner = (IntPtr)Raylib.GetWindowHandle();
        picked = null;
        browsing = true;
        mode = Mode.Browse;
        var t = new Thread(() =>
        {
            try { picked = AskZip(owner); }
            catch (Exception) { picked = null; }
            finally { browsing = false; }
        }) { IsBackground = true };
        if (OperatingSystem.IsWindows()) t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

    // ---- entrada ----------------------------------------------------------------------------

    static bool Hit(KeyboardKey k) => Raylib.IsKeyPressed(k) || Raylib.IsKeyPressedRepeat(k);
    static bool Pad(GamepadButton b) => Raylib.IsGamepadAvailable(0) && Raylib.IsGamepadButtonPressed(0, b);
    static bool Confirm() => Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter)
                             || Raylib.IsKeyPressed(KeyboardKey.Space) || Pad(GamepadButton.RightFaceDown);
    static bool Back() => Raylib.IsKeyPressed(KeyboardKey.Escape) || Pad(GamepadButton.RightFaceRight);

    bool Inside(int x, int y, int w, int h) => mx >= x && mx < x + w && my >= y && my < y + h;
    static int ButtonX(int b) => b == 0 ? PX + 20 : PX + PW - 20 - BtnW;

    // 1 = juego copiado, -1 = salir, 0 = sigue.
    int Update()
    {
        Layout();
        if (frame < FadeFrames) frame++;
        var m = Raylib.GetMousePosition();
        mx = (m.X - ox) / u; my = (m.Y - oy) / u;
        bool moved = Raylib.GetMouseDelta() != Vector2.Zero;
        bool click = Raylib.IsMouseButtonPressed(MouseButton.Left);
        hover = -1;

        int r = 0;
        switch (mode)
        {
            case Mode.Browse:
                if (browsing) break;
                mode = Mode.Main;
                string? p = picked;
                if (p == null) break;                              // cancelado
                var err = Check(p);
                if (err != null) { Notice("ARCHIVO NO VALIDO", err); break; }
                err = Copy(p);
                if (err != null) { Notice("ERROR AL COPIAR", err); break; }
                mode = Mode.Done; doneTimer = 0;
                break;

            case Mode.Done:
                if (++doneTimer >= DoneFrames) r = 1;
                break;

            case Mode.Notice:
            {
                var (bx, by) = NoticeButton();
                if (Inside(bx, by, 72, 18)) hover = 0;
                if (Confirm() || Back() || (click && hover == 0)) mode = Mode.Main;
                break;
            }

            default:
                for (int b = 0; b < 2; b++)
                    if (Inside(ButtonX(b), BtnY, BtnW, BtnH)) hover = b;
                if (moved && hover >= 0) sel = hover;
                if (Hit(KeyboardKey.Left) || Hit(KeyboardKey.Right) || Hit(KeyboardKey.Up) || Hit(KeyboardKey.Down)
                    || Hit(KeyboardKey.Tab) || Pad(GamepadButton.LeftFaceLeft) || Pad(GamepadButton.LeftFaceRight))
                    sel ^= 1;
                if (click && hover >= 0) { sel = hover; r = Activate(); }
                else if (Confirm()) r = Activate();
                else if (Back()) r = -1;
                break;
        }

        bool hand = hover >= 0;
        if (hand != handCursor) { Raylib.SetMouseCursor(hand ? MouseCursor.PointingHand : MouseCursor.Default); handCursor = hand; }
        return r;
    }

    int Activate()
    {
        if (sel == 1) return -1;
        StartBrowse();
        return 0;
    }

    void Notice(string title, string[] lines) { noticeTitle = title; noticeLines = lines; mode = Mode.Notice; }

    // ---- dibujo -----------------------------------------------------------------------------

    void Layout()
    {
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        u = Math.Max(1, (int)MathF.Min(sw / (float)LogW, sh / (float)LogH));
        ox = (sw - LogW * u) / 2; oy = (sh - LogH * u) / 2;
    }

    void Draw()
    {
        Layout();
        alpha = frame / (float)FadeFrames;
        Raylib.ClearBackground(Color.Black);
        Background();
        Header();

        Panel(PX, PY, PW, PH);
        const string t1 = "NO SE ENCONTRO", t2 = "EL JUEGO ORIGINAL";
        int tx = (LogW - t1.Length * 8) / 2;
        Text(t1, tx, PY + 8, Yellow, 1, 2);
        Text("\x7F", tx - 16, PY + 12, Red);                       // corazones como en OptionsMenu
        Text("\x7F", tx + t1.Length * 8 + 8, PY + 12, Red);
        Text(t2, (LogW - t2.Length * 8) / 2, PY + 26, Yellow, 1, 2);
        Rect(PX + 8, PY + 46, PW - 16, 1, GoldLine);

        Center("PARA JUGAR HACE FALTA EL", PY + 54, White);
        Center("ARCHIVO ZIP DEL JUEGO:", PY + 64, White);
        Center("WBOY2U.ZIP", PY + 77, Cyan);
        Center("PULSA EXAMINAR Y ELIGELO.", PY + 90, White);
        Center("SE COPIARA A LA CARPETA", PY + 100, Gray);
        Center("FILES JUNTO AL JUEGO.", PY + 110, Gray);

        Button(0, "EXAMINAR");
        Button(1, "SALIR");

        Rect(PX + 8, PY + PH - 16, PW - 16, 1, GoldLine);
        if (mode == Mode.Browse)
        {
            bool blink = ((int)(Raylib.GetTime() * 2) & 1) == 0;
            Center("ELIGE EL ZIP EN LA VENTANA", PY + PH - 11, blink ? Yellow : DimGray);
        }
        else Center("FLECHAS + ENTER O RATON", PY + PH - 11, Gray);

        if (mode == Mode.Notice) DrawNotice();
        if (mode == Mode.Done) DrawDone();
    }

    void Background()
    {
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        Raylib.DrawRectangleGradientV(0, 0, sw, sh, F(new Color(40, 110, 230, 255)), F(new Color(150, 210, 255, 255)));

        float t = (float)Raylib.GetTime();
        Cloud((int)(t * 4) % 352 - 48, 44);
        Cloud((int)(t * 4 + 150) % 352 - 48, 14);
        Cloud((int)(t * 4 + 260) % 352 - 48, 70);

        // Suelo: hierba y tierra a cuadros, de borde a borde de la ventana.
        int left = -(ox / u) - 8, right = LogW + ox / u + 8;
        Raylib.DrawRectangle(0, oy + GroundY * u, sw, sh - (oy + GroundY * u), F(new Color(170, 100, 40, 255)));
        for (int x = left - (left & 7); x < right; x += 8)
            for (int y = GroundY + 6; y < LogH + oy / u + 8; y += 8)
                if ((((x >> 3) + (y >> 3)) & 1) == 0) Rect(x, y, 8, 8, new Color(140, 80, 28, 255));
        Rect(left, GroundY, right - left, 6, new Color(56, 184, 56, 255));
        Rect(left, GroundY, right - left, 2, new Color(150, 240, 90, 255));
        Rect(left, GroundY + 6, right - left, 1, new Color(30, 110, 30, 255));
    }

    void Cloud(int x, int y)
    {
        var c = new Color(255, 255, 255, 220);
        Rect(x, y + 6, 44, 8, c);
        Rect(x + 6, y + 2, 18, 6, c);
        Rect(x + 18, y - 2, 16, 8, c);
        Rect(x + 2, y + 14, 40, 1, new Color(200, 220, 255, 220));
    }

    void Header()
    {
        const string s = "WONDER BOY";
        const int iconSize = 24, gap = 6;
        int w = iconSize + gap + s.Length * 16, x = (LogW - w) / 2;
        Raylib.DrawTexturePro(icon, new Rectangle(0, 0, icon.Width, icon.Height),
                              new Rectangle(ox + x * u, oy + 4 * u, iconSize * u, iconSize * u), Vector2.Zero, 0f, F(Color.White));
        Text(s, x + iconSize + gap, 8, Yellow, 2, 2);
    }

    void Button(int b, string label)
    {
        int x = ButtonX(b);
        bool on = sel == b && mode == Mode.Main;
        bool off = mode == Mode.Browse;
        Rect(x - 1, BtnY - 1, BtnW + 2, BtnH + 2, new Color(0, 0, 0, 200));
        Raylib.DrawRectangleGradientV(ox + x * u, oy + BtnY * u, BtnW * u, BtnH * u,
                                      F(new Color(40, 70, 160, 230)), F(new Color(12, 20, 70, 240)));
        if (on) Rect(x, BtnY, BtnW, BtnH, Highlight);
        Frame(x, BtnY, BtnW, BtnH, on ? Gold : Amber);
        int tx = x + (BtnW - label.Length * 8) / 2, ty = BtnY + 6;
        if (on) Text(">", tx - 12 + Bob(), ty, Red);
        Text(label, tx, ty, off ? DimGray : on ? Pulse(Yellow) : White);
    }

    (int x, int y, int w, int h) NoticeBox(int lines, bool button)
    {
        const int w = 224;
        int h = 28 + lines * 10 + (button ? 26 : 4);
        return ((LogW - w) / 2, (LogH - h) / 2, w, h);
    }

    (int x, int y) NoticeButton()
    {
        var (_, y, _, h) = NoticeBox(noticeLines.Length, true);
        return ((LogW - 72) / 2, y + h - 24);
    }

    void DrawNotice()
    {
        Rect(0, 0, LogW, LogH, new Color(0, 0, 0, 120));
        var (x, y, w, h) = NoticeBox(noticeLines.Length, true);
        Panel(x, y, w, h);
        Center(noticeTitle, y + 8, Red);
        Rect(x + 8, y + 20, w - 16, 1, GoldLine);
        for (int i = 0; i < noticeLines.Length; i++) Center(noticeLines[i], y + 28 + i * 10, White);
        var (bx, by) = NoticeButton();
        Rect(bx, by, 72, 18, Highlight);
        Frame(bx, by, 72, 18, Gold);
        Center("ACEPTAR", by + 5, Pulse(Yellow));
    }

    void DrawDone()
    {
        Rect(0, 0, LogW, LogH, new Color(0, 0, 0, 120));
        var (x, y, w, h) = NoticeBox(3, false);
        Panel(x, y, w, h);
        Center("JUEGO ENCONTRADO", y + 8, OnColor);
        Rect(x + 8, y + 20, w - 16, 1, GoldLine);
        Center("COPIADO EN LA CARPETA FILES.", y + 28, White);
        bool blink = ((int)(Raylib.GetTime() * 3) & 1) == 0;
        Center("CARGANDO...", y + 48, blink ? Yellow : DimGray);
    }

    void Panel(int x, int y, int w, int h)
    {
        Rect(x - 1, y - 1, w + 2, h + 2, new Color(0, 0, 0, 230));
        Raylib.DrawRectangleGradientV(ox + x * u, oy + y * u, w * u, h * u, F(new Color(28, 52, 132, 225)), F(new Color(10, 16, 56, 235)));
        Frame(x, y, w, h, Gold);
        Frame(x + 1, y + 1, w - 2, h - 2, Amber);
    }

    void Frame(int x, int y, int w, int h, Color c)
    {
        Rect(x, y, w, 1, c); Rect(x, y + h - 1, w, 1, c);
        Rect(x, y, 1, h, c); Rect(x + w - 1, y, 1, h, c);
    }

    void Rect(int x, int y, int w, int h, Color c)
    {
        if (w <= 0 || h <= 0) return;
        Raylib.DrawRectangle(ox + x * u, oy + y * u, w * u, h * u, F(c));
    }

    void Center(string s, int y, Color c) => Text(s, (LogW - s.Length * 8) / 2, y, c);

    // Texto con la fuente 8x8 en coordenadas del lienzo (sx / sy: escala horizontal / vertical).
    void Text(string s, int x, int y, Color c, int sx = 1, int sy = 1)
    {
        var tint = F(c);
        for (int i = 0; i < s.Length; i++)
        {
            int ch = char.ToUpperInvariant(s[i]);
            if (ch == ' ') continue;
            if (ch < 0x20 || ch > 0x7F) ch = '?';
            Raylib.DrawTexturePro(font, new Rectangle((ch - 0x20) * 8, 0, 8, 8),
                                  new Rectangle(ox + (x + i * 8 * sx) * u, oy + y * u, 8 * sx * u, 8 * sy * u),
                                  Vector2.Zero, 0f, tint);
        }
    }

    Color F(Color c) => new(c.R, c.G, c.B, (byte)(c.A * alpha));

    static Color Pulse(Color c)
    {
        float k = 0.82f + 0.18f * MathF.Sin((float)Raylib.GetTime() * 6f);
        return new Color((byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k), c.A);
    }

    static int Bob() => (int)(MathF.Sin((float)Raylib.GetTime() * 8f) * 1.5f + 1.5f);

    // ---- recursos ---------------------------------------------------------------------------

    // Icon/wonderboy.png incrustado (el mismo de la barra de titulo).
    static Texture2D LoadIcon()
    {
        using var s = typeof(GameWindow).Assembly.GetManifestResourceStream("WonderBoy.Icon.png");
        Image img;
        if (s == null) img = Raylib.GenImageColor(1, 1, Color.Blank);
        else
        {
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            img = Raylib.LoadImageFromMemory(".png", ms.ToArray());
        }
        var tex = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        Raylib.GenTextureMipmaps(ref tex);
        Raylib.SetTextureFilter(tex, TextureFilter.Trilinear);
        return tex;
    }

    // Atlas 768x8 de $20-$7F como el de OptionsMenu: glifo blanco con degradado por fila y sombra negra
    // abajo a la derecha (se tine al dibujar). Caracteres sin glifo: vacios.
    static Texture2D BuildFont()
    {
        byte[] rowLight = { 255, 246, 236, 224, 212, 198, 184 };
        var img = Raylib.GenImageColor(96 * 8, 8, Color.Blank);
        foreach (var (ch, rows) in Glyphs)
        {
            int c = ch - 0x20;
            if (c < 0 || c >= 96) continue;
            bool On(int x, int y) => y >= 0 && y < rows.Length && x >= 0 && x < rows[y].Length && rows[y][x] == '#';
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                {
                    if (On(x, y)) Raylib.ImageDrawPixel(ref img, c * 8 + x, y, new Color(rowLight[Math.Min(y, 6)], rowLight[Math.Min(y, 6)], rowLight[Math.Min(y, 6)], (byte)255));
                    else if (On(x - 1, y - 1)) Raylib.ImageDrawPixel(ref img, c * 8 + x, y, new Color(0, 0, 0, 220));
                }
        }
        var tex = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        Raylib.SetTextureFilter(tex, TextureFilter.Point);
        return tex;
    }

    // Glifos 7x7 (fila 7 y columna 7 quedan para la sombra). '\x7F' = corazon (tile $7F del juego).
    static readonly (char, string[])[] Glyphs =
    {
        ('A', new[] { "..###..", ".##.##.", "##...##", "##...##", "#######", "##...##", "##...##" }),
        ('B', new[] { "######.", "##...##", "##...##", "######.", "##...##", "##...##", "######." }),
        ('C', new[] { "..####.", ".##..##", "##.....", "##.....", "##.....", ".##..##", "..####." }),
        ('D', new[] { "#####..", "##..##.", "##...##", "##...##", "##...##", "##..##.", "#####.." }),
        ('E', new[] { "#######", "##.....", "##.....", "######.", "##.....", "##.....", "#######" }),
        ('F', new[] { "#######", "##.....", "##.....", "######.", "##.....", "##.....", "##....." }),
        ('G', new[] { "..#####", ".##....", "##.....", "##..###", "##...##", ".##..##", "..#####" }),
        ('H', new[] { "##...##", "##...##", "##...##", "#######", "##...##", "##...##", "##...##" }),
        ('I', new[] { ".#####.", "..###..", "..###..", "..###..", "..###..", "..###..", ".#####." }),
        ('J', new[] { "...####", ".....##", ".....##", ".....##", "##...##", "##...##", ".#####." }),
        ('K', new[] { "##...##", "##..##.", "##.##..", "####...", "#####..", "##..##.", "##...##" }),
        ('L', new[] { "##.....", "##.....", "##.....", "##.....", "##.....", "##.....", "#######" }),
        ('M', new[] { "##...##", "###.###", "#######", "##.#.##", "##...##", "##...##", "##...##" }),
        ('N', new[] { "##...##", "###..##", "####.##", "##.####", "##..###", "##...##", "##...##" }),
        ('O', new[] { ".#####.", "##...##", "##...##", "##...##", "##...##", "##...##", ".#####." }),
        ('P', new[] { "######.", "##...##", "##...##", "######.", "##.....", "##.....", "##....." }),
        ('Q', new[] { ".#####.", "##...##", "##...##", "##...##", "##.####", "##..##.", ".####.#" }),
        ('R', new[] { "######.", "##...##", "##...##", "######.", "####...", "##.###.", "##..###" }),
        ('S', new[] { ".#####.", "##...##", "##.....", ".#####.", ".....##", "##...##", ".#####." }),
        ('T', new[] { "#######", "..###..", "..###..", "..###..", "..###..", "..###..", "..###.." }),
        ('U', new[] { "##...##", "##...##", "##...##", "##...##", "##...##", "##...##", ".#####." }),
        ('V', new[] { "##...##", "##...##", "##...##", "###.###", ".#####.", "..###..", "...#..." }),
        ('W', new[] { "##...##", "##...##", "##...##", "##.#.##", "#######", "###.###", "##...##" }),
        ('X', new[] { "##...##", "###.###", ".#####.", "..###..", ".#####.", "###.###", "##...##" }),
        ('Y', new[] { "##...##", "##...##", "###.###", ".#####.", "..###..", "..###..", "..###.." }),
        ('Z', new[] { "#######", "....###", "...###.", "..###..", ".###...", "###....", "#######" }),
        ('0', new[] { ".#####.", "##...##", "##..###", "##.#.##", "###..##", "##...##", ".#####." }),
        ('1', new[] { "..###..", ".####..", "..###..", "..###..", "..###..", "..###..", ".#####." }),
        ('2', new[] { ".#####.", "##...##", ".....##", "..####.", ".###...", "###....", "#######" }),
        ('3', new[] { "######.", ".....##", ".....##", "..####.", ".....##", ".....##", "######." }),
        ('4', new[] { "...###.", "..####.", ".##.##.", "##..##.", "#######", "....##.", "....##." }),
        ('5', new[] { "######.", "##.....", "######.", ".....##", ".....##", "##...##", ".#####." }),
        ('6', new[] { "..####.", ".##....", "##.....", "######.", "##...##", "##...##", ".#####." }),
        ('7', new[] { "#######", "##...##", "....##.", "...##..", "..###..", "..###..", "..###.." }),
        ('8', new[] { ".#####.", "##...##", "##...##", ".#####.", "##...##", "##...##", ".#####." }),
        ('9', new[] { ".#####.", "##...##", "##...##", ".######", ".....##", "....##.", ".####.." }),
        ('.', new[] { ".......", ".......", ".......", ".......", ".......", "..##...", "..##..." }),
        (',', new[] { ".......", ".......", ".......", ".......", "..##...", "..##...", ".##...." }),
        (':', new[] { ".......", "..##...", "..##...", ".......", "..##...", "..##...", "......." }),
        ('-', new[] { ".......", ".......", ".......", ".#####.", ".......", ".......", "......." }),
        ('+', new[] { ".......", "..###..", "..###..", "#######", "..###..", "..###..", "......." }),
        ('!', new[] { "..###..", "..###..", "..###..", "..###..", "..###..", ".......", "..###.." }),
        ('?', new[] { ".#####.", "##...##", "....##.", "...##..", "...##..", ".......", "...##.." }),
        ('/', new[] { ".....##", "....##.", "...##..", "..##...", ".##....", "##.....", "#......" }),
        ('(', new[] { "...##..", "..##...", ".##....", ".##....", ".##....", "..##...", "...##.." }),
        (')', new[] { "..##...", "...##..", "....##.", "....##.", "....##.", "...##..", "..##..." }),
        ('\'', new[] { "..##...", "..##...", ".##....", ".......", ".......", ".......", "......." }),
        ('>', new[] { ".##....", "..##...", "...##..", "....##.", "...##..", "..##...", ".##...." }),
        ('<', new[] { "....##.", "...##..", "..##...", ".##....", "..##...", "...##..", "....##." }),
        ('\x7F', new[] { ".##.##.", "#######", "#######", "#######", ".#####.", "..###..", "...#..." }),
    };
}
