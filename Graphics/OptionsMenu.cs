using Raylib_cs;
using WonderBoy.Game;
using WonderBoy.Input;

namespace WonderBoy.Graphics;

// Menu de opciones de --play (tecla Tab): panel semitransparente sobre el juego (en pausa: no hay Step,
// el sonido sigue para oir el volumen) con la fuente del juego (tiles $20-$7F, ASCII: pens 1-6 degradado
// de arriba a abajo, pen 7 sombra). Lienzo logico de 256x224 escalado a la ventana (escala entera).
//   Raiz: GRAPHICS, SOUND, GAME, CONTROLLER (Enter abre, Esc / Tab cierra).
//   Paginas: arriba / abajo eligen, izquierda / derecha cambian, Enter cambia o redefine, Esc vuelve.
// Cada cambio se aplica al momento y se guarda (Config.cfg: WindowConfig.Save; Controllers.cfg:
// ControllerConfig.Save). Con el menu abierto Escape no cierra el juego (SetExitKey(Null)).
public sealed class OptionsMenu : IDisposable
{
    const int LogW = 256, LogH = 224;                              // pantalla original
    const int PX = 8, PY = 8, PW = 240, PH = 208;                  // panel
    const int RowsTop = 38, RowH = 10, FooterH = 20;
    const int VisibleRows = (PH - FooterH - RowsTop) / RowH;       // 15 filas
    const int FadeFrames = 8, SliderW = 32;
    const int L = PX + 18, R = PX + PW - 10;                       // columna de etiquetas / borde de valores

    enum Page { Root, Graphics, Sound, Game, Controller, Filters }   // Filters: subpagina de GRAPHICS
    static readonly string[] RootNames = { "GRAPHICS", "SOUND", "GAME", "CONTROLLER" };
    static readonly string[] RootHints = { "Display, HUD bars, filters", "Game volume", "Lives and credits", "Keyboard and joysticks" };
    static readonly string[] DisplayNames = { "Windowed", "Fullscreen", "Widescreen" };
    static readonly string[] KeyLabels = { "Move Left", "Move Right", "Jump", "Fire" };
    static readonly string[] ButtonLabels = { "Jump", "Fire" };
    static readonly byte[] PenLight = { 0, 255, 242, 228, 214, 198, 182, 0 };   // brillo de los pens 1-6

    static readonly Color Gold = new(255, 204, 51, 255), Amber = new(150, 80, 0, 255), Yellow = new(255, 226, 70, 255),
                          White = new(236, 236, 236, 255), Gray = new(168, 172, 196, 255), DimGray = new(120, 124, 150, 255),
                          Orange = new(255, 140, 40, 255), Red = new(255, 64, 64, 255), Cyan = new(130, 220, 255, 255),
                          OnColor = new(120, 255, 120, 255), OffColor = new(255, 110, 110, 255), Highlight = new(255, 170, 0, 55);

    sealed class Row
    {
        public string Label = "", Value = "";
        public Color ValueColor = Cyan;
        public Action<int>? Change;     // izquierda (-1) / derecha (+1)
        public Action? Activate;        // Enter (redefinir)
        public bool Header, Info;       // no seleccionables
        public int Slider = -1;         // 0..100: barra
        public int Hearts;              // vidas: corazones (tile $7F)
        public bool Selectable => !Header && !Info;
    }

    readonly WindowConfig cfg;
    readonly ControllerConfig ctl;
    readonly GameWindow window;
    readonly TitleRom game;
    readonly WonderBoy.Audio.GameAudio? audio;
    readonly Texture2D font;
    Page page;
    int rootSel, sel, scroll, fade;
    int filtersRow;                                                // fila "Filters" de GRAPHICS (volver con Esc)
    int captureKey = -1, captureButton = -1;                       // accion que se redefine (-1 = ninguna)
    List<JoyInfo> joys = new();
    int u = 1, ox, oy;                                             // escala y origen del lienzo en pantalla
    float alpha = 1f;

    public bool IsOpen { get; private set; }

    public OptionsMenu(WindowConfig cfg, ControllerConfig ctl, GameWindow window, TitleRom game,
                       WonderBoy.Audio.GameAudio? audio, byte[][] tiles)
    {
        this.cfg = cfg; this.ctl = ctl; this.window = window; this.game = game; this.audio = audio;
        font = BuildFont(tiles);
    }

    public void Dispose() => Raylib.UnloadTexture(font);

    // Atlas 768x8 de los tiles $20-$7F: blanco con el degradado de los pens (se tine al dibujar).
    static Texture2D BuildFont(byte[][] tiles)
    {
        var img = Raylib.GenImageColor(96 * 8, 8, Color.Blank);
        for (int c = 0; c < 96; c++)
        {
            var t = tiles[(0x20 + c) % tiles.Length];
            for (int i = 0; i < 64; i++)
            {
                int p = t[i];
                if (p == 0) continue;
                byte l = PenLight[p];
                Raylib.ImageDrawPixel(ref img, c * 8 + (i & 7), i >> 3, p == 7 ? new Color(0, 0, 0, 220) : new Color(l, l, l, (byte)255));
            }
        }
        var tex = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        Raylib.SetTextureFilter(tex, TextureFilter.Point);
        return tex;
    }

    // ---- entrada ----------------------------------------------------------------------------

    static bool Hit(KeyboardKey k) => Raylib.IsKeyPressed(k) || Raylib.IsKeyPressedRepeat(k);

    // Llamar una vez por fotograma antes de Step: Tab abre / cierra; con el menu abierto lee el teclado.
    public void Update()
    {
        if (!IsOpen)
        {
            if (Raylib.IsKeyPressed(KeyboardKey.Tab)) Open();
            return;
        }
        if (fade < FadeFrames) fade++;
        joys = ControllerConfig.Detect();
        if (captureKey >= 0 || captureButton >= 0) { UpdateCapture(); return; }
        if (Raylib.IsKeyPressed(KeyboardKey.Tab)) { Close(); return; }

        bool back = Raylib.IsKeyPressed(KeyboardKey.Escape) || Raylib.IsKeyPressed(KeyboardKey.Backspace);
        bool enter = Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter)
                     || Raylib.IsKeyPressed(KeyboardKey.Space);
        int dy = (Hit(KeyboardKey.Down) ? 1 : 0) - (Hit(KeyboardKey.Up) ? 1 : 0);
        int dx = (Hit(KeyboardKey.Right) ? 1 : 0) - (Hit(KeyboardKey.Left) ? 1 : 0);

        if (page == Page.Root)
        {
            if (back) { Close(); return; }
            if (dy != 0) rootSel = (rootSel + dy + RootNames.Length) % RootNames.Length;
            if (enter || dx > 0)
            {
                page = (Page)(rootSel + 1); sel = 0; scroll = 0;
                FixSel(Rows());
            }
            return;
        }
        if (back)
        {
            if (page == Page.Filters) { page = Page.Graphics; sel = filtersRow; scroll = 0; }
            else page = Page.Root;
            return;
        }
        var rows = Rows();
        FixSel(rows);
        if (dy != 0) Move(rows, dy);
        if (sel >= rows.Count || !rows[sel].Selectable) return;
        var row = rows[sel];
        if (dx != 0) row.Change?.Invoke(dx);
        if (enter)
        {
            if (row.Activate != null) row.Activate();
            else row.Change?.Invoke(1);
        }
    }

    void Open()
    {
        IsOpen = true; fade = 0; page = Page.Root; captureKey = captureButton = -1;
        Raylib.SetExitKey(KeyboardKey.Null);                       // Escape = volver (no cierra el juego)
    }

    void Close()
    {
        IsOpen = false; captureKey = captureButton = -1;
        Raylib.SetExitKey(KeyboardKey.Escape);
    }

    // Redefinir: la siguiente tecla (o boton de cualquier joystick) pulsada; Escape / Tab cancelan.
    void UpdateCapture()
    {
        if (captureKey >= 0)
        {
            for (int k = Raylib.GetKeyPressed(); k != 0; k = Raylib.GetKeyPressed())
            {
                var key = (KeyboardKey)k;
                if (key == KeyboardKey.Escape || key == KeyboardKey.Tab) { captureKey = -1; return; }
                if (!ControllerConfig.Assignable(key)) continue;          // 5 (moneda) y 1 (START) reservadas
                ctl.SetKey(captureKey, key); ctl.Save();
                captureKey = -1;
                return;
            }
            return;
        }
        if (Raylib.IsKeyPressed(KeyboardKey.Escape) || Raylib.IsKeyPressed(KeyboardKey.Tab)) { captureButton = -1; return; }
        foreach (var j in joys)
            for (var b = GamepadButton.RightFaceUp; b <= GamepadButton.RightThumb; b++)
                if (Raylib.IsGamepadButtonPressed(j.Index, b))
                {
                    ctl.SetButton(captureButton, b); ctl.Save();
                    captureButton = -1;
                    return;
                }
    }

    void Move(List<Row> rows, int d)
    {
        int n = rows.Count, i = sel;
        for (int k = 0; k < n; k++)
        {
            i = (i + d + n) % n;
            if (rows[i].Selectable) { sel = i; return; }
        }
    }

    void FixSel(List<Row> rows)
    {
        if (rows.Count == 0) { sel = 0; return; }
        if (sel >= rows.Count) sel = 0;
        if (!rows[sel].Selectable) Move(rows, 1);
    }

    // ---- filas de cada pagina ---------------------------------------------------------------

    static Row Toggle(string label, bool on, Action<bool> set) => new()
    {
        Label = label, Value = on ? "On" : "Off", ValueColor = on ? OnColor : OffColor, Change = _ => set(!on),
    };
    static Row Header(string label) => new() { Label = label, Header = true };
    static Row Info(string label, string value = "") => new() { Label = label, Value = value, Info = true };

    void FilterRow(List<Row> r, ScreenFilter f) =>
        r.Add(Toggle(ScreenFilters.Label(f), (cfg.Filters & f) != 0, v => { cfg.Filters = ScreenFilters.Set(cfg.Filters, f, v); cfg.Save(); }));

    List<Row> Rows()
    {
        var r = new List<Row>();
        switch (page)
        {
            case Page.Graphics:
                r.Add(new Row
                {
                    Label = "Display Mode", Value = DisplayNames[cfg.DisplayMode],
                    Change = d => { cfg.DisplayMode = (cfg.DisplayMode + d + 3) % 3; window.ApplyDisplayMode(); cfg.Save(); },
                });
                r.Add(Toggle("Show FPS", cfg.ShowFps, v => { cfg.ShowFps = v; window.UpdateTitle(); cfg.Save(); }));
                // Max FPS (MaxFPS): fps de la ventana; la logica sigue a 60 pasos por segundo (FrameClock).
                r.Add(new Row
                {
                    Label = "Max FPS", Value = cfg.MaxFps.ToString(),
                    Change = d => { cfg.MaxFps = WindowConfig.NextMaxFps(cfg.MaxFps, d); window.ApplyFrameRate(); cfg.Save(); },
                });
                r.Add(Toggle("V-Sync", cfg.VSync, v => { cfg.VSync = v; window.ApplyFrameRate(); cfg.Save(); }));
                r.Add(Toggle("Fix License Text", cfg.CorrectLicensed, v => { cfg.CorrectLicensed = v; game.SetLicensed(v); cfg.Save(); }));
                r.Add(new Row
                {
                    Label = "HUD Bar Opacity", Value = cfg.OpacityBars + "%", Slider = cfg.OpacityBars,
                    Change = d =>
                    {
                        cfg.OpacityBars = Math.Clamp(cfg.OpacityBars + 5 * d, 0, 100);
                        TitleRenderer.BarOpacity = cfg.OpacityBars;
                        cfg.Save();
                    },
                });
                int active = ScreenFilters.All.Count(f => (cfg.Filters & f) != 0);
                int row = r.Count;
                r.Add(new Row
                {
                    Label = "Filters", Value = active == 0 ? "Off" : active + " On", ValueColor = active == 0 ? OffColor : OnColor,
                    Activate = () => { filtersRow = row; page = Page.Filters; sel = 0; scroll = 0; },
                });
                break;
            case Page.Filters:
                // Un filtro por grupo (al encender uno se apagan los otros del grupo); los efectos se suman.
                r.Add(Header("SCALING"));
                FilterRow(r, ScreenFilter.Bilinear);
                FilterRow(r, ScreenFilter.SharpBilinear);
                FilterRow(r, ScreenFilter.Fsr);
                r.Add(Header("PIXEL ART"));
                FilterRow(r, ScreenFilter.Xbr);
                FilterRow(r, ScreenFilter.Scale3x);
                r.Add(Header("EFFECTS"));
                FilterRow(r, ScreenFilter.Vibrance);
                FilterRow(r, ScreenFilter.Scanlines);
                r.Add(Info(""));
                r.Add(Info("One filter per group"));
                break;
            case Page.Sound:
                r.Add(new Row
                {
                    Label = "Sound Volume", Value = cfg.SoundVolume + "%", Slider = cfg.SoundVolume,
                    Change = d =>
                    {
                        cfg.SoundVolume = Math.Clamp(cfg.SoundVolume + 5 * d, 0, 100);
                        audio?.SetVolume(cfg.SoundVolume);
                        cfg.Save();
                    },
                });
                if (audio == null) { r.Add(Info("")); r.Add(Info("No audio device found")); }
                break;
            case Page.Game:
                r.Add(new Row
                {
                    Label = "Lives At Start", Value = cfg.LivesAtStart.ToString(), Hearts = cfg.LivesAtStart,
                    Change = d =>
                    {
                        cfg.LivesAtStart = Math.Clamp(cfg.LivesAtStart + d, 1, WindowConfig.MaxLives);
                        game.Poke(0xC027, (byte)cfg.LivesAtStart);       // $C027: vidas de cada partida
                        cfg.Save();
                    },
                });
                r.Add(Toggle("Infinite Credits", cfg.InfinityCredit, v => { cfg.InfinityCredit = v; cfg.Save(); }));
                r.Add(Info(""));
                r.Add(Info("Lives count from next game"));
                r.Add(Info("No coin: just press START"));
                break;
            case Page.Controller:
                var brand = joys.Count > 0 ? joys[0].Brand : JoyBrand.Compatible;
                r.Add(Header("KEYBOARD"));
                r.Add(Toggle("Use Keyboard", ctl.Keyboard, v => { if (!v && !ctl.Joystick) return; ctl.Keyboard = v; ctl.Save(); }));
                for (int i = 0; i < KeyLabels.Length; i++)
                {
                    int a = i;
                    r.Add(new Row { Label = KeyLabels[a], Value = ControllerConfig.KeyText(ctl.Keys[a]), Activate = () => captureKey = a });
                }
                r.Add(Header("JOYSTICK"));
                r.Add(Toggle("Use Joystick", ctl.Joystick, v => { if (!v && !ctl.Keyboard) return; ctl.Joystick = v; ctl.Save(); }));
                for (int i = 0; i < ButtonLabels.Length; i++)
                {
                    int a = i;
                    r.Add(new Row
                    {
                        Label = ButtonLabels[a], Value = ControllerConfig.ButtonText(ctl.Buttons[a], brand),
                        Activate = () => { if (joys.Count > 0) captureButton = a; },
                    });
                }
                r.Add(Header("DETECTED"));
                if (joys.Count == 0) r.Add(Info("No joystick connected"));
                foreach (var j in joys)
                {
                    int room = (R - (L + j.Label.Length * 8 + 8)) / 8;  // nombre del mando a la derecha, sin pisar
                    string name = j.Name.Length <= room ? j.Name : room > 1 ? j.Name[..(room - 1)] + "." : "";
                    r.Add(Info(j.Label, name));
                }
                break;
        }
        return r;
    }

    // ---- dibujo -----------------------------------------------------------------------------

    // Llamar entre BeginDrawing y EndDrawing, despues de GameWindow.Draw.
    public void Draw()
    {
        if (!IsOpen) return;
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        u = Math.Max(1, (int)MathF.Min(sw / (float)LogW, sh / (float)LogH));
        ox = (sw - LogW * u) / 2; oy = (sh - LogH * u) / 2;
        alpha = fade / (float)FadeFrames;

        Raylib.DrawRectangle(0, 0, sw, sh, F(new Color(0, 0, 0, 150)));   // el juego se ve atenuado detras
        Panel(PX, PY, PW, PH);
        if (page == Page.Root) DrawRoot(); else DrawPage();
        if (captureKey >= 0 || captureButton >= 0) DrawCapture();
    }

    void DrawRoot()
    {
        Title("OPTIONS");
        for (int i = 0; i < RootNames.Length; i++)
        {
            int y = PY + 46 + i * 28;
            bool on = i == rootSel;
            string s = RootNames[i];
            int w = s.Length * 16, x = (LogW - w) / 2;
            if (on)
            {
                Rect(PX + 16, y - 4, PW - 32, 24, Highlight);
                Rect(PX + 16, y - 4, 2, 24, Gold);
                Text("{", x - 26 + Bob(), y, Gold, 2);              // tile $7B: flecha
            }
            Text(s, x, y, on ? Pulse(Yellow) : White, 2);
        }
        Center(RootHints[rootSel], PY + PH - 36, Gray);
        Footer("Enter: Open   Tab: Close");
    }

    void DrawPage()
    {
        Title(page == Page.Filters ? "FILTERS" : RootNames[(int)page - 1]);
        var rows = Rows();
        FixSel(rows);
        int first = rows.FindIndex(x => x.Selectable), last = rows.FindLastIndex(x => x.Selectable);
        if (sel < scroll) scroll = sel;
        if (sel >= scroll + VisibleRows) scroll = sel - VisibleRows + 1;
        if (sel == first) scroll = 0;                                  // cabecera de arriba visible
        if (sel == last) scroll = Math.Max(scroll, rows.Count - VisibleRows);   // informacion de abajo visible
        scroll = Math.Clamp(scroll, 0, Math.Max(0, rows.Count - VisibleRows));

        for (int i = scroll; i < rows.Count && i < scroll + VisibleRows; i++)
        {
            var row = rows[i];
            int y = PY + RowsTop + (i - scroll) * RowH;
            if (row.Header)
            {
                Text(row.Label, L - 10, y, Orange);
                int hx = L - 10 + row.Label.Length * 8 + 4;
                Rect(hx, y + 3, R - hx, 1, new Color(255, 140, 40, 110));
                continue;
            }
            if (row.Info)
            {
                Text(row.Label, L, y, Gray);
                if (row.Value != "") Text(row.Value, R - row.Value.Length * 8, y, DimGray);
                continue;
            }
            bool on = i == sel;
            if (on)
            {
                Rect(PX + 4, y - 1, PW - 8, RowH, Highlight);
                Rect(PX + 4, y - 1, 2, RowH, Gold);
                Text("{", PX + 7 + Bob(), y, Gold);
            }
            Text(row.Label, L, y, on ? Pulse(Yellow) : White);

            // Valor a la derecha: [corazones] [barra] texto; flechas < > si se ajusta con izquierda / derecha.
            bool adjust = row.Change != null && row.Activate == null;
            int w = row.Value.Length * 8 + (row.Slider >= 0 ? SliderW + 4 : 0) + (row.Hearts > 0 ? row.Hearts * 8 + 4 : 0);
            int end = adjust ? R - 10 : R, x = end - w;
            if (adjust && on)
            {
                Text("<", x - 10, y, Gold);
                Text(">", R - 8, y, Gold);
            }
            if (row.Hearts > 0) { Text(new string('\x7F', row.Hearts), x, y, Red); x += row.Hearts * 8 + 4; }
            if (row.Slider >= 0)
            {
                Rect(x, y + 2, SliderW, 4, new Color(0, 0, 0, 200));
                Rect(x, y + 2, SliderW * row.Slider / 100, 4, on ? Gold : new Color(200, 150, 40, 255));
                Rect(x, y + 6, SliderW, 1, Amber);
                x += SliderW + 4;
            }
            Text(row.Value, x, y, row.ValueColor);
        }
        if (scroll > 0) Text("^", R - 8, PY + RowsTop - 9, Gray);
        if (scroll + VisibleRows < rows.Count) Text("_", R - 8, PY + PH - FooterH - 6, Gray);

        bool redefine = sel < rows.Count && rows[sel].Activate != null;
        Footer(!redefine ? "< >: Change   Esc: Back" : page == Page.Controller ? "Enter: Redefine   Esc: Back" : "Enter: Open   Esc: Back");
    }

    void DrawCapture()
    {
        const int w = 184, h = 52;
        int x = (LogW - w) / 2, y = (LogH - h) / 2;
        Rect(PX, PY, PW, PH, new Color(0, 0, 0, 110));
        Panel(x, y, w, h);
        string what = captureKey >= 0 ? "Keyboard: " + KeyLabels[captureKey] : "Joystick: " + ButtonLabels[captureButton];
        Center(what, y + 8, Yellow);
        bool blink = ((int)(Raylib.GetTime() * 2) & 1) == 0;
        Center(captureKey >= 0 ? "Press a key" : "Press a button", y + 22, blink ? White : DimGray);
        Center("Esc: Cancel", y + 37, Gray);
    }

    void Title(string s)
    {
        int w = s.Length * 16, x = (LogW - w) / 2, y = PY + 9;
        Text(s, x, y, Yellow, 2);
        Text("\x7F", x - 16, y + 4, Red);                              // tile $7F: corazon
        Text("\x7F", x + w + 8, y + 4, Red);
        Rect(PX + 8, PY + 30, PW - 16, 1, new Color(255, 204, 51, 160));
    }

    void Footer(string s)
    {
        Rect(PX + 8, PY + PH - FooterH, PW - 16, 1, new Color(255, 204, 51, 160));
        Center(s, PY + PH - 13, Gray);
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

    // Texto con la fuente del juego en coordenadas del lienzo (8x8 por caracter x scale).
    void Text(string s, int x, int y, Color c, int scale = 1)
    {
        var tint = F(c);
        float size = 8 * scale * u;
        for (int i = 0; i < s.Length; i++)
        {
            int ch = s[i];
            if (ch == ' ') continue;
            if (ch < 0x20 || ch > 0x7F) ch = '?';
            Raylib.DrawTexturePro(font, new Rectangle((ch - 0x20) * 8, 0, 8, 8),
                                  new Rectangle(ox + (x + i * 8 * scale) * u, oy + y * u, size, size),
                                  System.Numerics.Vector2.Zero, 0f, tint);
        }
    }

    Color F(Color c) => new(c.R, c.G, c.B, (byte)(c.A * alpha));

    static Color Pulse(Color c)
    {
        float k = 0.82f + 0.18f * MathF.Sin((float)Raylib.GetTime() * 6f);
        return new Color((byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k), c.A);
    }

    static int Bob() => (int)(MathF.Sin((float)Raylib.GetTime() * 8f) * 1.5f + 1.5f);
}
