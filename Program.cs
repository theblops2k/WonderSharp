using Raylib_cs;
using WonderBoy.Game;
using WonderBoy.Graphics;

// Sin argumentos (doble clic) no hay consola. Con argumentos se usa la del terminal;
// solo se crea una nueva si no hay terminal y el modo no es el juego (--play).
if (args.Length > 0) WonderBoy.ConsoleHost.Attach(allocIfMissing: !args.Contains("--play"));

// --tiles [--pitch N]: decodifica tiles (8x8, 3bpp) y sprites (4bpp crudo) de wboy2u.zip y
// guarda docs/analysis/gfx/tiles_sheet.png (+ tiles_sheet_x2.png y sprites_sheet.png).
if (args.Contains("--tiles"))
{
    string root = FindRoot();
    string zip = Path.Combine(root, "Mame", "roms", "wboy2u.zip");
    string outDir = Path.Combine(root, "docs", "analysis", "gfx");
    Directory.CreateDirectory(outDir);
    int pi = Array.IndexOf(args, "--pitch");
    int pitch = pi >= 0 && pi + 1 < args.Length ? int.Parse(args[pi + 1]) : 16;

    Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
    var tiles = TileDecoder.DecodeTiles(TileDecoder.LoadTileRom(zip));
    var sheet = TileDecoder.BuildTileSheet(tiles);
    var sheet2 = TileDecoder.Scale(sheet, 2);
    string tPath = Path.Combine(outDir, "tiles_sheet.png");
    Raylib.ExportImage(sheet, tPath);
    Raylib.ExportImage(sheet2, Path.Combine(outDir, "tiles_sheet_x2.png"));

    var spr = TileDecoder.LoadSpriteRom(zip);
    var sSheet = TileDecoder.BuildSpriteSheet(spr, pitch);
    string sPath = Path.Combine(outDir, "sprites_sheet.png");
    Raylib.ExportImage(sSheet, sPath);
    Console.WriteLine($"{tiles.Length} tiles -> {tPath} ({sheet.Width}x{sheet.Height})");
    Console.WriteLine($"{spr.Length} bytes sprites ({spr.Length / TileDecoder.SpriteBankSize} bancos, pitch {pitch}) -> {sPath}");
    Raylib.UnloadImage(sheet); Raylib.UnloadImage(sheet2); Raylib.UnloadImage(sSheet);
    return;
}

// --title-rom [--frame N] [--cmp ref.png] [--bins dir] [--diff suf] [--start F] [--lost L]: construye
// paleta, VRAM y spriteram del attract (titulo, records 1000, licencia 2400)
// solo desde la ROM principal (Game/TitleRom.cs, rutinas $0F52/$101A/$10D7) en el fotograma N
// (300 por defecto) y guarda docs/analysis/title/title_rom.png. --bins escribe pal/vram/spr
// generados en 'dir' para compararlos fuera del port.
if (args.Contains("--title-rom"))
{
    string root = FindRoot();
    string zip = Path.Combine(root, "Mame", "roms", "wboy2u.zip");
    int fi = Array.IndexOf(args, "--frame");
    int frame = fi >= 0 && fi + 1 < args.Length ? int.Parse(args[fi + 1]) : 300;
    var mainRom = TileDecoder.LoadRegion(zip, new[] { "ic129_02.bin", "ic130_03.bin", "ic131_04.bin", "ic132_05.bin", "epr-7591.133", "epr-7592.134" });
    int ii = Array.IndexOf(args, "--irq");
    int irqStart = ii >= 0 && ii + 1 < args.Length ? int.Parse(args[ii + 1]) : TitleRom.DefaultIrqStart;
    int li0 = Array.IndexOf(args, "--lost");
    if (li0 >= 0 && li0 + 1 < args.Length) TitleRom.ClearLost = int.Parse(args[li0 + 1]);
    int si0 = Array.IndexOf(args, "--start");
    int titleStart = si0 >= 0 && si0 + 1 < args.Length ? int.Parse(args[si0 + 1]) : 29;
    var t = TitleRom.Build(mainRom, frame, titleStart, irqStart);
    int bi = Array.IndexOf(args, "--bins");
    if (bi >= 0 && bi + 1 < args.Length)
    {
        Directory.CreateDirectory(args[bi + 1]);
        File.WriteAllBytes(Path.Combine(args[bi + 1], "pal.bin"), t.Palette);
        File.WriteAllBytes(Path.Combine(args[bi + 1], "vram.bin"), t.Vram);
        File.WriteAllBytes(Path.Combine(args[bi + 1], "spr.bin"), t.SpriteRam);
    }
    // --diff <sufijo>: bytes distintos contra docs/analysis/title/{pal,vram,spr}_<sufijo>.bin
    int di = Array.IndexOf(args, "--diff");
    if (di >= 0 && di + 1 < args.Length)
    {
        string d = Path.Combine(root, "docs", "analysis", "title");
        foreach (var (n, gen) in new[] { ("pal", t.Palette), ("vram", t.Vram), ("spr", t.SpriteRam) })
        {
            var r = File.ReadAllBytes(Path.Combine(d, $"{n}_{args[di + 1]}.bin"));
            var diffs = Enumerable.Range(0, Math.Min(r.Length, gen.Length)).Where(i => r[i] != gen[i]).ToList();
            Console.WriteLine($"{n}: {diffs.Count} diferencias" + string.Concat(diffs.Take(12).Select(i => $" [{i:X3}]{gen[i]:X2}/{r[i]:X2}")));
        }
        Console.WriteLine($"estado {t.State}, $C002={t[0xC002]:X2}, $C010={t[0xC010]:X2}");
    }

    Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
    var tiles = TileDecoder.DecodeTiles(TileDecoder.LoadTileRom(zip));
    var sprRom = TileDecoder.LoadSpriteRom(zip);
    var prom = TileDecoder.LoadRegion(zip, new[] { "pr-5317.76" });
    var img = TitleRenderer.Render(t.Palette, t.Vram, t.SpriteRam, tiles, sprRom, prom);
    string outPath = Path.Combine(root, "docs", "analysis", "title", "title_rom.png");
    Raylib.ExportImage(img, outPath);
    Console.WriteLine($"title-rom f{frame} -> {outPath}, scroll X={TitleRenderer.ScrollX(t.Vram)} Y={TitleRenderer.ScrollY(t.Vram)}");
    int ci = Array.IndexOf(args, "--cmp");
    if (ci >= 0 && ci + 1 < args.Length)
    {
        var refImg = Raylib.LoadImage(args[ci + 1]);
        Console.WriteLine($"coincidencia exacta {TitleRenderer.Compare(img, refImg, 0):F2}%, tol 16 {TitleRenderer.Compare(img, refImg, 16):F2}%");
        Raylib.UnloadImage(refImg);
    }
    Raylib.UnloadImage(img);
    return;
}

// --title <pal> <vram> [out.png] [--spr spr.bin] [--cmp ref.png]: compone BG+FG+sprites
// (512x224) desde volcados de paleta ($D800), VRAM ($E000) y spriteram ($D000; por defecto
// spr_<sufijo>.bin junto al vram) con tiles/sprites de la ROM y la PROM pr-5317.76.
// Salida por defecto: docs/analysis/title/title_<sufijo del vram>.png.
{
    int ti = Array.IndexOf(args, "--title");
    if (ti >= 0)
    {
        if (ti + 2 >= args.Length) { Console.WriteLine("uso: --title <pal.bin> <vram.bin> [out.png] [--spr spr.bin] [--cmp ref.png]"); return; }
        string root = FindRoot();
        string zip = Path.Combine(root, "Mame", "roms", "wboy2u.zip");
        byte[] pal = File.ReadAllBytes(args[ti + 1]);
        byte[] vram = File.ReadAllBytes(args[ti + 2]);
        string tag = Path.GetFileNameWithoutExtension(args[ti + 2]).Replace("vram_", "").TrimStart('0');
        string outPath = ti + 3 < args.Length && !args[ti + 3].StartsWith("--") ? args[ti + 3]
            : Path.Combine(root, "docs", "analysis", "title", $"title_{tag}.png");
        int si = Array.IndexOf(args, "--spr");
        string sprPath = si >= 0 ? args[si + 1]
            : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[ti + 2]))!, Path.GetFileName(args[ti + 2]).Replace("vram_", "spr_"));
        byte[]? sprRam = File.Exists(sprPath) ? File.ReadAllBytes(sprPath) : null;

        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        var tiles = TileDecoder.DecodeTiles(TileDecoder.LoadTileRom(zip));
        var sprRom = TileDecoder.LoadSpriteRom(zip);
        var prom = TileDecoder.LoadRegion(zip, new[] { "pr-5317.76" });
        var img = TitleRenderer.Render(pal, vram, sprRam!, tiles, sprRom, prom);
        Raylib.ExportImage(img, outPath);
        Console.WriteLine($"title -> {outPath} ({TitleRenderer.Width}x{TitleRenderer.Height}), " +
            $"scroll X={TitleRenderer.ScrollX(vram)} Y={TitleRenderer.ScrollY(vram)}, sprites={(sprRam != null ? sprPath : "no")}");
        int ci = Array.IndexOf(args, "--cmp");
        if (ci >= 0)
        {
            var refImg = Raylib.LoadImage(args[ci + 1]);
            Console.WriteLine($"coincidencia exacta {TitleRenderer.Compare(img, refImg, 0):F1}%, tol 16 {TitleRenderer.Compare(img, refImg, 16):F1}%");
            if (args.Contains("--pairs")) TitleRenderer.DumpColorPairs(img, refImg);
            if (args.Contains("--scan"))
                for (int dy = -24; dy <= 24; dy += 1)
                    for (int dx = -8; dx <= 8; dx += 2)
                    {
                        double s = TitleRenderer.Compare(img, refImg, 16, dx, dy);
                        if (s > 60) Console.WriteLine($"  dx={dx} dy={dy}: {s:F1}%");
                    }
            Raylib.UnloadImage(refImg);
        }
        Raylib.UnloadImage(img);
        return;
    }
}

// --count-enemy-types: sin ventana. Por carpeta de RAM, fotogramas con >= 1 ranura de enemigo de cada
// tipo. Ranuras $C8A0 + n*$20 (n = 0..11) como EnemySlots: tipo = IX+$18, activa si != 0 y bit7 = 0.
if (args.Contains("--count-enemy-types"))
{
    string[] dirs = { "ram_auto_r2", "ram_round2_move", "ram_huevo", "ram_40c4_auto" };
    string root = AppContext.BaseDirectory;
    while (root != null && !Directory.Exists(Path.Combine(root, "docs", "analysis"))) root = Path.GetDirectoryName(root)!;
    var counts = new SortedDictionary<int, int[]>();
    for (int d = 0; d < dirs.Length; d++)
        foreach (string f in Directory.GetFiles(Path.Combine(root!, "docs", "analysis", dirs[d]), "frame_*.bin"))
        {
            byte[] ram = File.ReadAllBytes(f);
            var seen = new HashSet<int>();
            for (int n = 0; n < 12; n++)
            {
                int t = ram[0xC8A0 - 0xC000 + n * 0x20 + 0x18];
                if (t != 0 && (t & 0x80) == 0) seen.Add(t);
            }
            foreach (int t in seen)
            {
                if (!counts.ContainsKey(t)) counts[t] = new int[dirs.Length];
                counts[t][d]++;
            }
        }
    Console.WriteLine("tipo  " + string.Join("  ", dirs));
    foreach (var kv in counts)
        Console.WriteLine($"{kv.Key:X2}    " + string.Join("  ", kv.Value.Select((v, i) => v.ToString().PadLeft(dirs[i].Length))));
    return;
}

// --play: partida con teclado sobre el modelo TitleRom (TitleRom.StartPlay): misma ventana y render
// que --attract; un Step() por frame de ventana con las entradas del frame en $C00C (mando) y $C00E
// (moneda/start). Teclas: flechas izq/der, Alt izq = BUTTON2 (salto), Ctrl izq = BUTTON1 (ataque),
// 5 = moneda, 1 = START1. Escape: salir.
// --play --frames N [--inputs csv]: sin ventana. N pasos desde el fotograma 29 con las entradas de
// inputs.csv (por defecto ram_rand) del mismo fotograma; compara $C07E, $C302, $C85F, $C850/$C851 y
// $C852/$C853 y la cola de sonido $C0DB-$C0DD con frame_NNNNNN.bin del mismo directorio.
// --round-load N [--out dir]: sin ventana. Partida desde el fotograma 29 con moneda f120-124 y START
// f180-184 (como random_play.lua) y ninguna otra entrada; al ver $C07E=5 pone $C301=N (area N>>2,
// ronda N&3, como $165F) y en el primer fotograma con $C07E=7 vuelca $C000-$CFFF y $E000-$EFFF en
// port_ram_NN.bin / port_vram_NN.bin. Referencia MAME: el mismo guion con escritura de $C301.
// --walk-all [--only N] [--frames F] [--verbose]: sin ventana. Recorre las 32 rondas desde --round-load
// con el jugador invencible, vitalidad y vidas fijas y volando; anota en Wonderboy_errors.log cada
// rutina sin portar (direccion y ronda) y la lista final sale por consola (WalkAll.cs).
if (args.Contains("--walk-all"))
{
    string zip = Path.Combine(FindRoot(), "Mame", "roms", "wboy2u.zip");
    var mainRom = TileDecoder.LoadRegion(zip, new[] { "ic129_02.bin", "ic130_03.bin", "ic131_04.bin", "ic132_05.bin", "epr-7591.133", "epr-7592.134" });
    WonderBoy.WalkAll.Run(mainRom, args);
    return;
}

if (args.Contains("--round-load"))
{
    int ri = Array.IndexOf(args, "--round-load");
    int n = int.Parse(args[ri + 1]);
    int oi = Array.IndexOf(args, "--out");
    string outDir = oi >= 0 && oi + 1 < args.Length ? args[oi + 1] : ".";
    string zip = Path.Combine(FindRoot(), "Mame", "roms", "wboy2u.zip");
    var mainRom = TileDecoder.LoadRegion(zip, new[] { "ic129_02.bin", "ic130_03.bin", "ic131_04.bin", "ic132_05.bin", "epr-7591.133", "epr-7592.134" });
    var game = TitleRom.StartPlay(mainRom, 29);
    bool written = false;
    for (int f = 29; f < 3000; f++)
    {
        game.Joystick = 0;
        game.Inputs = (byte)((f >= 120 && f < 125 ? 0x01 : 0) | (f >= 180 && f < 185 ? 0x10 : 0));
        game.Step();
        if (game.Unported != null) { Console.WriteLine($"N={n}: sin portar {game.Unported} en f{f}"); return; }
        if (!written && game[0xC07E] == 5) { game.Poke(0xC301, (byte)n); written = true; Console.Write($"N={n} escrito f{f}, "); }
        if (written && game[0xC07E] == 7)
        {
            File.WriteAllBytes(Path.Combine(outDir, $"port_ram_{n:D2}.bin"), Enumerable.Range(0xC000, 0x1000).Select(a => game[a]).ToArray());
            File.WriteAllBytes(Path.Combine(outDir, $"port_vram_{n:D2}.bin"), Enumerable.Range(0xE000, 0x1000).Select(a => game[a]).ToArray());
            Console.WriteLine($"juego f{f}");
            return;
        }
    }
    Console.WriteLine($"N={n}: sin llegar a $C07E=7");
    return;
}

// --check-config: sin ventana. Lee Config.cfg de la carpeta del ejecutable (lo crea con los valores
// por defecto si falta) e imprime los valores que usaran --attract y --play.
if (args.Contains("--check-config"))
{
    var cfg = WindowConfig.Load();
    Console.WriteLine($"config: {cfg.Path} ({(cfg.Created ? "creado con valores por defecto" : "existente")})");
    Console.WriteLine($"Fullscreen={(cfg.Widescreen ? "WIDESCREEN" : cfg.Fullscreen ? "ORIGINAL" : "NO")} ShowFps={(cfg.ShowFps ? "T" : "F")}"
                      + $" CorrectLicensed={(cfg.CorrectLicensed ? "T" : "F")} OpacityBars={cfg.OpacityBars}"
                      + $" LivesAtStart={cfg.LivesAtStart} InfinityCredit={(cfg.InfinityCredit ? "T" : "F")} SoundVolume={cfg.SoundVolume}");
    return;
}

if (args.Length == 0 || args.Contains("--play"))
{
    int fi = Array.IndexOf(args, "--frames");
    int maxFrames = fi >= 0 && fi + 1 < args.Length ? int.Parse(args[fi + 1]) : 0;
    // El juego original se lee de files\wboy2u.zip junto al ejecutable. Si falta: ventana
    // "No se encontro el juego original" (Examinar / Salir, RomSetupWindow). --frames (prueba sin
    // ventana) sigue usando Mame/roms/wboy2u.zip si files no lo tiene.
    string zip = RomSetupWindow.GameZip;
    if (!File.Exists(zip))
    {
        if (maxFrames > 0) zip = Path.Combine(FindRoot(), "Mame", "roms", "wboy2u.zip");
        else if (!RomSetupWindow.Run()) return;
    }
    const int TitleStart = 29;
    var mainRom = TileDecoder.LoadRegion(zip, new[] { "ic129_02.bin", "ic130_03.bin", "ic131_04.bin", "ic132_05.bin", "epr-7591.133", "epr-7592.134" });
    var game = TitleRom.StartPlay(mainRom, TitleStart);
    // Bits de inputs.csv / teclado -> $C00C y $C00E (correlacion con ram_rand: 7013/7013 fotogramas).
    static (byte joy, byte sys) Pack(bool coin, bool start, bool left, bool right, bool b1, bool b2, bool start2 = false) =>
        ((byte)((left ? 0x80 : 0) | (right ? 0x40 : 0) | (b2 ? 0x04 : 0) | (b1 ? 0x02 : 0)),
         (byte)((coin ? 0x01 : 0) | (start ? 0x10 : 0) | (start2 ? 0x20 : 0)));   // $C00E bit4 START1, bit5 START2

    if (maxFrames > 0)
    {
        int ii = Array.IndexOf(args, "--inputs");
        string csv = ii >= 0 && ii + 1 < args.Length ? args[ii + 1] : Path.Combine(FindDir("ram_rand"), "inputs.csv");
        string dir = Path.GetDirectoryName(Path.GetFullPath(csv))!;
        var inputs = new Dictionary<int, (byte joy, byte sys)>();
        foreach (var line in File.ReadLines(csv).Skip(1))
        {
            var v = line.Split(',').Select(int.Parse).ToArray(); // frame,COIN1,START1,LEFT,RIGHT,UP,DOWN,BUTTON1,BUTTON2
            inputs[v[0]] = Pack(v[1] != 0, v[2] != 0, v[3] != 0, v[4] != 0, v[7] != 0, v[8] != 0);
        }
        int[] addrs = { 0x07E, 0x302, 0x85F, 0x850, 0x851, 0x852, 0x853, 0x0DB, 0x0DC, 0x0DD };
        int ok = 0, run = 0, firstBad = -1;
        string firstInfo = "", unportedAt = "";
        var firstBy = new SortedDictionary<int, string>();
        for (int f = TitleStart; f < TitleStart + maxFrames; f++)
        {
            string path = Path.Combine(dir, $"frame_{f:D6}.bin");
            if (!File.Exists(path)) break;
            var (joy, sys) = inputs.GetValueOrDefault(f);
            game.Joystick = joy; game.Inputs = sys;
            game.Step();
            run++;
            if (game.Unported != null && unportedAt == "") unportedAt = $"{game.Unported} en f{f}";
            var cur = File.ReadAllBytes(path);
            var wrong = addrs.Where(o => game[0xC000 + o] != cur[o]).ToArray();
            foreach (int o in wrong)
                if (!firstBy.ContainsKey(o)) firstBy[o] = $"f{f} port={game[0xC000 + o]:X2} ram={cur[o]:X2}";
            if (wrong.Length == 0) { ok++; continue; }
            if (firstBad < 0)
            {
                firstBad = f;
                firstInfo = string.Join(" ", wrong.Select(o => $"${0xC000 + o:X4} port={game[0xC000 + o]:X2} ram={cur[o]:X2}"));
            }
        }
        Console.WriteLine($"play: {run} fotogramas (f{TitleStart}..f{TitleStart + run - 1}), coinciden {ok}, distintos {run - ok}");
        Console.WriteLine(firstBad < 0 ? "sin divergencias" : $"primera divergencia f{firstBad}: {firstInfo}");
        Console.WriteLine("primera por direccion: " + string.Join(", ", addrs.Select(o => $"${0xC000 + o:X4} " + firstBy.GetValueOrDefault(o, "ok"))));
        Console.WriteLine($"sin portar: {(unportedAt == "" ? "ninguno" : unportedAt)}; modo final {(game.GameMode ? "juego" : "attract")}, $C07E={game[0xC07E]}, $C016={game[0xC016]}");
        return;
    }

    Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
    var tiles = TileDecoder.DecodeTiles(TileDecoder.LoadTileRom(zip));
    var sprRom = TileDecoder.LoadSpriteRom(zip);
    var prom = TileDecoder.LoadRegion(zip, new[] { "pr-5317.76" });

    var cfg = WindowConfig.Load();                               // Config.cfg: Fullscreen / ShowFps / CorrectLicensed
    if (cfg.CorrectLicensed) game.CorrectLicensed();             // "LISENCED" -> "LICENSED" (todos los modos)
    TitleRenderer.BarOpacity = cfg.OpacityBars;                  // OpacityBars: fondo negro de las barras del HUD
    var window = GameWindow.Open(cfg, tiles);                    // SetTargetFPS(MaxFPS) y VSync (ApplyFrameRate)
    Raylib.SetExitKey(KeyboardKey.Escape);
    // Sonido: cada codigo de TitleRom.SoundPort ($0428 OUT ($14)) va al programa de sonido portado.
    using var audio = WonderBoy.Audio.GameAudio.Open(TileDecoder.LoadRegion(zip, new[] { "epr7498a.3" }));
    if (audio != null) TitleRom.SoundPort = audio.Send;
    audio?.SetVolume(cfg.SoundVolume);                           // SoundVolume (0..100, defecto 80)
    game.Poke(0xC027, (byte)cfg.LivesAtStart);                   // LivesAtStart: $C027 (vidas de cada partida, $1388)
    var clock = new FrameClock();                                // 60 pasos de logica por segundo (MaxFPS / VSync)
    // Controles (Controllers.cfg) y menu de opciones (Tab): con el menu abierto el juego queda en pausa.
    var controls = WonderBoy.Input.ControllerConfig.Load();
    var menu = new OptionsMenu(cfg, controls, window, game, audio, tiles);   // Dispose antes de CloseWindow (textura)

    int frame = 0;
    bool levelStartDone = false;                                 // LevelStart ya aplicado en esta partida
    bool checkPointDone = false;                                 // CheckPoint ya aplicado en esta partida
    bool afterBonus = false;                                     // WIDESCREEN: telon tras la pantalla de bonus
    Texture2D tex = default;
    var fb = new WonderBoy.Graphics.FrameBuffer();               // fotograma y su unica textura (se reutilizan)
    var wide = new WonderBoy.Graphics.WideView();
    // Errores y bloqueos -> Wonderboy_errors.log junto al ejecutable (ErrorLog.cs); el juego se detiene.
    var errorLog = new WonderBoy.ErrorLog("--play", game);
    string? stopReason = null;
    try
    {
    while (!Raylib.WindowShouldClose())
    {
        menu.Update();
        // MaxFPS = 60 sin VSync: un paso por fotograma, como siempre. Si no, los pasos que tocan por tiempo
        // real (0, 1 o mas); sin pasos nuevos se vuelve a dibujar el ultimo estado.
        bool fixedStep = FrameClock.Fixed(cfg);
        int steps = clock.Steps(fixedStep);
        for (int s = 0; s < steps && stopReason == null; s++)
        {
        if (!menu.IsOpen)
        {
            // Moneda 5, START1 (tecla 1) y START2 (tecla 2) en el teclado; izquierda / derecha / salto (BUTTON2) /
            // disparo (BUTTON1) de Controllers.cfg (teclado y / o joysticks).
            var pad = controls.Read();
            var (joy, sys) = Pack(Raylib.IsKeyDown(KeyboardKey.Five), Raylib.IsKeyDown(KeyboardKey.One),
                                  pad.left, pad.right, pad.fire, pad.jump, Raylib.IsKeyDown(KeyboardKey.Two));
            // InfinityCredit=T: sin monedas. START con $C016=0 pone 1 credito en attract ($0195 pasa a $4F20:
            // $1283 y $132C, que empieza con START pulsado) y en la cuenta atras de continuar ($1E36 sub 1, $1E95).
            if (cfg.InfinityCredit && (sys & 0x10) != 0 && game[0xC016] == 0)
            {
                int r = game[0xC081] | game[0xC082] << 8;
                if (!game.GameMode || (r == 0x1E36 && game[0xC07F] == 1)) game.Poke(0xC016, 1);
            }
            // START2 en attract: $132C ($1353-$1358) pide $C016 >= 2 para dos jugadores ($1399 resta $C083+1).
            if (cfg.InfinityCredit && (sys & 0x30) == 0x20 && game[0xC016] < 2 && !game.GameMode) game.Poke(0xC016, 2);
            game.Joystick = joy; game.Inputs = sys;
            game.Step();
            // LevelStart=N (Config.cfg, oculto): como --round-load, en el primer $C07E=5 de cada partida
            // pone $C301=N-1 antes de la carga de ronda; se rearma al volver al attract (fin de partida).
            if (cfg.LevelStart > 0)
            {
                if (!game.GameMode) levelStartDone = false;
                else if (!levelStartDone && game[0xC07E] == 5) { game.Poke(0xC301, (byte)(cfg.LevelStart - 1)); levelStartDone = true; }
            }
            // CheckPoint=N (Config.cfg, oculto): punto de reinicio del cartel N como lo deja $1B76 al perder una
            // vida (H = $C443 & $F0, o 1; $C30B = H*256, $C30F = H-1). Se aplica en $13D9 tras el sub 0 ($13E7
            // copia $C340 y pone $C30B=$0100) y antes del sub 2 ($147A coloca al jugador en $C30B-$40); $1651
            // (camara $172D, columnas $1431, objetos $1818) lo usa despues. Una vez por partida, como LevelStart.
            if (cfg.CheckPoint > 0)
            {
                if (!game.GameMode) checkPointDone = false;
                else if (!checkPointDone && game[0xC07E] == 4 && (game[0xC07F] == 1 || game[0xC07F] == 2))
                {
                    int h = cfg.CheckPoint == 1 ? 0x01 : (cfg.CheckPoint - 1) << 4;   // $01 $10 $20 $30
                    game.Poke(0xC30B, 0x00); game.Poke(0xC30C, (byte)h); game.Poke(0xC30F, (byte)(h - 1));
                    checkPointDone = true;
                }
            }
            frame++;
            stopReason = errorLog.Check(frame);
            if (stopReason != null) { errorLog.Write(stopReason, null, frame); break; }
        }
        audio?.Frame();                                          // tambien en pausa: se oye el volumen del menu
        }
        if (stopReason != null) break;

        // WIDESCREEN en nivel: mas columnas del mapa (WideView); en la pantalla de titulo y en la de
        // PUSH START (creditos $1283 y espera START $132C, rutina en $C081) y en la de continuar ($1E36) el fondo llena el ancho
        // (TitleRenderer con width); resto igual que ORIGINAL.
        int wideW = window.WideWidth;
        // Telon de $199A al llegar a la meta (RoundEndCurtain): el BG sigue siendo el del nivel, mismo dibujo ancho.
        if (wideW > 0 && (game.LevelTilesDrawn || game.RoundEndCurtain)) { wide.Render(fb, game, tiles, sprRom, prom, wideW); afterBonus = false; }
        else
        {
            wide.Reset();
            int routine = game[0xC081] | game[0xC082] << 8;
            // $1E36: pantalla de continuar (tras su $0A24($1F) ya no hay tiles del nivel): igual que START.
            bool startScreen = game.GameMode && (routine == 0x1283 || routine == 0x132C || routine == 0x1E36);
            bool titleScreen = !game.GameMode && game.State == TitleRom.StateTitle;
            // Records (attract State 3 y tabla tras la partida $C07E=10): solo la franja superior a todo el
            // ancho (barOnly). Recuento de puntos / bonus del fin de ronda ($C07E=7 con $C440 != 0): fondo a
            // todo el ancho y vidas pegadas al borde izquierdo como en el nivel (TitleRenderer, bonus).
            bool bonusScreen = game.GameMode && game[0xC07E] == 7 && game[0xC440] != 0;
            bool barScreen = game.GameMode ? game[0xC07E] == 10 : game.State == TitleRom.StateRecords;
            // Franjas y telon antes de cada nivel ($13D9, $152D, $1651) sin tiles del nivel (inicio de partida,
            // tras el fin de ronda): leyendas PLAYER1 / HIGH / PLAYER2 a todo el ancho (WideView.WipeRoutine).
            bool wipe = game.GameMode && WideView.WipeRoutine(routine);
            bool full = titleScreen || startScreen;
            // Telon tras el recuento de puntos (franjas $13D9 / $152D / $1651 que siguen al bonus): el fondo
            // sigue a todo el ancho como en la pantalla de bonus (mismo dibujo, bonus = true). Incluye el
            // fotograma de $1944 ($C07E=4 con $C081 aun $18B7, $C07E 5 / $152D llega en el siguiente).
            afterBonus = bonusScreen || (afterBonus && (wipe || game[0xC07E] == 4));
            bool bonusWide = bonusScreen || afterBonus;
            int titleW = wideW > 0 && (full || barScreen || wipe || bonusWide) ? wideW : TitleRenderer.Width;
            // Inventario (filas 4-5) en el nivel y en el telon previo, no en el bonus (OpacityBars).
            bool items = game.GameMode && !bonusWide && (game.LevelTilesDrawn || wipe);
            TitleRenderer.Render(fb, game.Palette, game.Vram, game.SpriteRam, tiles, sprRom, prom, titleW,
                                 !full && !bonusWide, wipe, bonusWide, items);
        }
        // Bordes del mundo visible por modo: WideView.Render / Reset fijan WonderBoy.Game.ScreenBounds
        // (ORIGINAL / NO = ventana original; WIDESCREEN en nivel = ancho del dibujo).
        // Una sola textura: se actualiza con UpdateTexture y solo se rehace si cambia el ancho (filtro Point).
        tex = fb.Upload();

        window.UpdateTitle();

        Raylib.BeginDrawing();
        Raylib.ClearBackground(Color.Black);
        window.Draw(tex);
        menu.Draw();                                             // menu de opciones encima (si esta abierto)
        Raylib.EndDrawing();
        window.CountFrame();                                     // fps reales: +1 fotograma presentado
        window.ShowAfterFirstFrame();
    }
    }
    catch (Exception ex)
    {
        stopReason = "excepcion no controlada: " + ex.Message;
        errorLog.Write(stopReason, ex, frame);
    }
    if (stopReason != null)
    {
        WonderBoy.ErrorLog.ShowStopScreen(stopReason);
        Console.WriteLine($"play detenido en f{frame}: {stopReason} (ver {WonderBoy.ErrorLog.FilePath})");
        Environment.ExitCode = 1;
    }

    fb.Unload();
    menu.Dispose();
    TitleRom.SoundPort = null;
    Raylib.CloseWindow();
    Console.WriteLine($"play: {frame} fotogramas, $C07E={game[0xC07E]}, sin portar {game.Unported ?? "ninguno"}");
    return;
}

// --check-sound [--notes XX]: sin ventana ni audio. Codigos de sonido de ram_auto_r2 y ram_40c4_auto
// ($C0DC con $C0DB >= 1, $C0DD con $C0DB >= 2); para cada uno, notas del programa de sonido portado
// (Audio/SoundProgram) contrastadas con Audio/SoundScore, ordenes y generador (Audio/SoundCheck).
// --notes XX: lista completa de notas del codigo XX (hex).
if (args.Contains("--check-sound"))
{
    byte[] srom = File.ReadAllBytes(Path.Combine(FindDir("ram"), "..", "wboy2u_soundcpu.bin"));
    var codes = new SortedSet<int>();
    var head = new byte[3];
    foreach (var sub in new[] { "ram_auto_r2", "ram_40c4_auto" })
        foreach (var path in Directory.EnumerateFiles(FindDir(sub), "frame_*.bin"))
        {
            using var fs = File.OpenRead(path);
            fs.Position = 0x0DB;
            if (fs.Read(head, 0, 3) < 3) continue;
            if (head[0] >= 1) codes.Add(head[1]);
            if (head[0] >= 2) codes.Add(head[2]);
        }
    int ni = Array.IndexOf(args, "--notes");
    int notesFor = ni >= 0 && ni + 1 < args.Length ? Convert.ToInt32(args[ni + 1], 16) : -1;
    WonderBoy.Audio.SoundCheck.CheckAll(srom, codes, notesFor);

    // Enlace TitleRom.SoundPort -> SoundProgram (como en las ventanas): partida de --play --frames 6985
    // (ram_rand/inputs.csv), 4 IRQ de sonido por fotograma. Cuenta envios y si cada uno arranca algo.
    var mainRom = TileDecoder.LoadRegion(Path.Combine(FindRoot(), "Mame", "roms", "wboy2u.zip"),
        new[] { "ic129_02.bin", "ic130_03.bin", "ic131_04.bin", "ic132_05.bin", "epr-7591.133", "epr-7592.134" });
    var inp = new Dictionary<int, (byte joy, byte sys)>();
    foreach (var line in File.ReadLines(Path.Combine(FindDir("ram_rand"), "inputs.csv")).Skip(1))
    {
        var v = line.Split(',').Select(int.Parse).ToArray(); // frame,COIN1,START1,LEFT,RIGHT,UP,DOWN,BUTTON1,BUTTON2
        inp[v[0]] = ((byte)((v[3] != 0 ? 0x80 : 0) | (v[4] != 0 ? 0x40 : 0) | (v[8] != 0 ? 0x04 : 0) | (v[7] != 0 ? 0x02 : 0)),
                     (byte)((v[1] != 0 ? 0x01 : 0) | (v[2] != 0 ? 0x10 : 0)));
    }
    var sp = new WonderBoy.Audio.SoundProgram(srom);
    var sc = new WonderBoy.Audio.ChipNotes();
    sp.ChipWrite = sc.Write;
    var game = TitleRom.StartPlay(mainRom, 29);
    var sent = new List<byte>();
    int started = 0, kept = 0, orders = 0, audible = 0;
    TitleRom.SoundPort = c => { sent.Add(c); sp.Nmi(c); };
    for (int f = 29; f < 29 + 6985; f++)
    {
        (game.Joystick, game.Inputs) = inp.GetValueOrDefault(f);
        int n0 = sent.Count;
        game.Step();
        for (int i = 0; i < 4; i++) sp.Irq();
        foreach (byte c in sent.Skip(n0))
        {
            bool music = (c & 0xC0) == 0xC0 && c < 0xF0;
            if (c >= 0xF0 || !music && (c & 0x3F) < 0x08) orders++;
            else if (music ? sp[0x80F3] == 0 && sp[0x8003] != 0 : sp[0x8004] == (c & 0x3F) && sp[0x8002] != 0) started++;
            else kept++;
        }
        if (Enumerable.Range(0, 8).Any(i => sc.Atten[i / 4, i % 4] < 15)) audible++;
    }
    TitleRom.SoundPort = null;
    Console.WriteLine($"enlace SoundPort: {sent.Count} envios en 6985 fotogramas ({sent.Distinct().Count()} codigos: " +
        string.Join(" ", sent.Distinct().OrderBy(c => c).Select(c => $"{c:X2}")) + $"); arrancan {started}, ordenes {orders}, " +
        $"no arrancan {kept} (sigue un efecto de mas prioridad); fotogramas con sonido {audible}");
    return;
}

// --attract [--frames N]: ventana segun Config.cfg (GameWindow) con la secuencia de attract (título, récords y
// licencia) en vivo: un TitleRom avanza un fotograma por frame de ventana (Start + Step, 60 fps)
// y se dibuja desde su paleta, VRAM y spriteram. Escape: salir. --frames N cierra tras N fotogramas.
if (args.Contains("--attract"))
{
    string root = FindRoot();
    string zip = Path.Combine(root, "Mame", "roms", "wboy2u.zip");
    int fi = Array.IndexOf(args, "--frames");
    int maxFrames = fi >= 0 && fi + 1 < args.Length ? int.Parse(args[fi + 1]) : 0;
    const int TitleStart = 29;   // primer fotograma del titulo (attract_flow.md)

    Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
    var tiles = TileDecoder.DecodeTiles(TileDecoder.LoadTileRom(zip));
    var sprRom = TileDecoder.LoadSpriteRom(zip);
    var prom = TileDecoder.LoadRegion(zip, new[] { "pr-5317.76" });
    var mainRom = TileDecoder.LoadRegion(zip, new[] { "ic129_02.bin", "ic130_03.bin", "ic131_04.bin", "ic132_05.bin", "epr-7591.133", "epr-7592.134" });
    var game = TitleRom.Start(mainRom, TitleStart);

    var cfg = WindowConfig.Load();                               // Config.cfg: Fullscreen / ShowFps / CorrectLicensed
    if (cfg.CorrectLicensed) game.CorrectLicensed();             // "LISENCED" -> "LICENSED" (todos los modos)
    TitleRenderer.BarOpacity = cfg.OpacityBars;                  // OpacityBars: fondo negro de las barras del HUD
    var window = GameWindow.Open(cfg, tiles);                    // SetTargetFPS(MaxFPS) y VSync (ApplyFrameRate)
    Raylib.SetExitKey(KeyboardKey.Escape);
    // Sonido: cada codigo de TitleRom.SoundPort ($0428 OUT ($14)) va al programa de sonido portado.
    using var audio = WonderBoy.Audio.GameAudio.Open(TileDecoder.LoadRegion(zip, new[] { "epr7498a.3" }));
    if (audio != null) TitleRom.SoundPort = audio.Send;
    audio?.SetVolume(cfg.SoundVolume);                           // SoundVolume (0..100, defecto 80)
    var clock = new FrameClock();                                // 60 pasos de logica por segundo (MaxFPS / VSync)

    int frame = 0;
    Texture2D tex = default;
    var fb = new WonderBoy.Graphics.FrameBuffer();               // fotograma y su unica textura (se reutilizan)
    string label = "";
    var stepTime = new System.Diagnostics.Stopwatch();
    var renderTime = new System.Diagnostics.Stopwatch();
    // Errores y bloqueos -> Wonderboy_errors.log junto al ejecutable (ErrorLog.cs); el juego se detiene.
    var errorLog = new WonderBoy.ErrorLog("--attract", game);
    string? stopReason = null;
    try
    {
    while (!Raylib.WindowShouldClose() && (maxFrames == 0 || frame < maxFrames))
    {
        // MaxFPS = 60 sin VSync: un paso por fotograma, como siempre; si no, pasos por tiempo real (sin interpolar).
        bool fixedStep = FrameClock.Fixed(cfg);
        int steps = clock.Steps(fixedStep);
        for (int s = 0; s < steps && stopReason == null && (maxFrames == 0 || frame < maxFrames); s++)
        {
            stepTime.Start();
            game.Step();
            stepTime.Stop();
            audio?.Frame();
            frame++;
            stopReason = errorLog.Check(frame);
            if (stopReason != null) errorLog.Write(stopReason, null, frame);
        }
        if (stopReason != null) break;

        // Imagen de la paleta/VRAM/spriteram actuales en fb; una sola textura actualizada con UpdateTexture.
        renderTime.Start();
        // WIDESCREEN: en la pantalla de titulo el fondo llena el ancho; en records solo la franja superior
        // (barOnly); licencia como ORIGINAL.
        int wideW = window.WideWidth;
        bool recs = game.State == TitleRom.StateRecords;
        int titleW = wideW > 0 && (game.State == TitleRom.StateTitle || recs) ? wideW : TitleRenderer.Width;
        TitleRenderer.Render(fb, game.Palette, game.Vram, game.SpriteRam, tiles, sprRom, prom, titleW, recs);
        renderTime.Stop();
        tex = fb.Upload();
        label = game.State switch { 2 => "Titulo", 3 => "Records", 4 => "Licencia", _ => $"estado {game.State}" };

        // Titulo de la ventana con los fps actuales (refrescado cada fotograma).
        window.UpdateTitle();

        Raylib.BeginDrawing();
        Raylib.ClearBackground(Color.Black);
        window.Draw(tex);
        int ly = Raylib.GetScreenHeight() - 24;
        Raylib.DrawRectangle(4, ly - 2, Raylib.MeasureText(label, 20) + 8, 22, new Color(0, 0, 0, 160));
        Raylib.DrawText(label, 8, ly, 20, Color.White);
        Raylib.EndDrawing();
        window.CountFrame();                                     // fps reales: +1 fotograma presentado
        window.ShowAfterFirstFrame();
    }
    }
    catch (Exception ex)
    {
        stopReason = "excepcion no controlada: " + ex.Message;
        errorLog.Write(stopReason, ex, frame);
    }
    if (stopReason != null)
    {
        WonderBoy.ErrorLog.ShowStopScreen(stopReason);
        Console.WriteLine($"attract detenido en f{frame}: {stopReason} (ver {WonderBoy.ErrorLog.FilePath})");
        Environment.ExitCode = 1;
    }

    fb.Unload();
    TitleRom.SoundPort = null;
    Raylib.CloseWindow();
    Console.WriteLine($"attract: {frame} fotogramas, ultimo estado {game.State} ({label})");
    Console.WriteLine($"coste medio por fotograma: Step {stepTime.Elapsed.TotalMilliseconds / Math.Max(frame, 1):F2} ms, Render {renderTime.Elapsed.TotalMilliseconds / Math.Max(frame, 1):F2} ms");
    return;
}

// --check-x: reproduce LEFT/RIGHT de inputs.csv y compara X con $C850/$C851 de los volcados.
if (args.Contains("--check-x"))
{
    // Tramos 376-401 (derecha, en salto) y 510-629 (izquierda, suelo/salto/tope).
    Check(new[] { (376, 401), (510, 629) }, (p, d, _) =>
    {
        int ram = d[0x850] | d[0x851] << 8;
        return ram == p.X ? null : $"port X=${p.X:X4} vel={(byte)p.VelX:X2}  RAM X=${ram:X4} vel={d[0x854]:X2}";
    });
    return;
}
// --check-jump: compara Y de mundo ($C852/$C853) y estado ($C85F) en los saltos J=360/450/540.
// El de 360 empieza en 374: hasta entonces el jugador no se actualiza (el salto queda
// pendiente en $C084 bit5 y se semilla desde el volcado 373).
if (args.Contains("--check-jump"))
{
    Check(new[] { (374, 449), (450, 539), (540, 629) }, (p, d, _) =>
    {
        int ram = d[0x852] | d[0x853] << 8;
        return ram == p.Y && d[0x85F] == p.State ? null
            : $"port Y=${p.Y:X4} st={p.State:X2} v={(byte)p.VelY:X2}  RAM Y=${ram:X4} st={d[0x85F]:X2} v={d[0x857]:X2}";
    });
    return;
}
// --check-screen-y: Y de pantalla del sprite del jugador = ($C444 - Y - alto/2) & $FF
// ($2475-$24E6), con Y y alto del port y $C444 de c000_N, contra $D020 de frame_N
// (docs/analysis/ram_sprites). $C840 -> $D020 se copia en el vblank siguiente ($07E9),
// asi que $D020 de N es la Y calculada en N-1: se informa tambien esa alineacion.
// 361-373 se excluyen: el original no actualiza al jugador (ver --check-jump).
if (args.Contains("--check-screen-y"))
{
    string sdir = FindDir("ram_sprites");
    byte[] Spr(int f) => File.ReadAllBytes(Path.Combine(sdir, $"frame_{f:D6}.bin"));
    int maxDiff = 0, lagMatches = 0, lagMax = 0;
    Check(new[] { (340, 360), (374, 449), (450, 539), (540, 600) }, (p, d, f) =>
    {
        int c444 = d[0x444] | d[0x445] << 8;
        int calc = (c444 - p.Y - (p.Height >> 1)) & 0xFF;
        int same = Spr(f)[0x20], next = Spr(f + 1)[0x20];
        int dn = Math.Abs(calc - next);
        lagMax = Math.Max(lagMax, dn);
        if (dn == 0) lagMatches++;
        int ds = Math.Abs(calc - same);
        maxDiff = Math.Max(maxDiff, ds);
        return ds == 0 ? null : $"port Y=${p.Y:X4} C444=${c444:X4} -> ${calc:X2}  D020=${same:X2} (dif {calc - same:+0;-0})  D020(f+1)=${next:X2}";
    }, "ram_sprites", "c000_");
    Console.WriteLine($"Diferencia maxima: {maxDiff}");
    Console.WriteLine($"Contra D020 de f+1 (copia en el vblank siguiente): {lagMatches} coincidencias, dif max {lagMax}");
    return;
}

// --check-camera: Camera.Update ($21CF) sobre la RAM del volcado N con las entradas que el
// mismo tick escribe antes de $21CF tomadas de N+1 (jugador $C850/$C851/$C85F de $274D, $C477
// de $2BD5); compara con N+1 las variables que escribe $21CF. Si $C477(N+1)!=0, $2193 no
// llama a $21CF (se omite el par). Recorre ram, ram_b1, ram_b2, ram_play y ram_sprites (c000_).
if (args.Contains("--check-camera"))
{
    byte[] rom = File.ReadAllBytes(Path.Combine(FindDir("ram"), "..", "wboy2u_maincpu.bin"));
    int[] outs = { 0x442, 0x443, 0x444, 0x445, 0x000, 0x001, 0x002, 0x448, 0x449, 0x450, 0x451, 0x440, 0x479 };
    int totalPairs = 0, totalMoves = 0, totalBad = 0;
    foreach (var (sub, prefix) in new[] { ("ram", "frame_"), ("ram_huevo", "frame_"), ("ram_b1", "frame_"), ("ram_b2", "frame_"),
                                          ("ram_play", "frame_"), ("ram_sprites", "c000_") })
    {
        string dir = FindDir(sub);
        int pairs = 0, moves = 0, dumpMoves = 0, bad = 0, skipped = 0, columns = 0, slopes = 0;
        var otherMoves = new Dictionary<int, int>();
        var diffs = new List<string>();
        byte[]? prev = null;
        for (int f = 1; ; f++)
        {
            string path = Path.Combine(dir, $"{prefix}{f:D6}.bin");
            if (!File.Exists(path)) break;
            var cur = File.ReadAllBytes(path);
            if (prev != null)
            {
                // Solo manejadores que llaman a $2193 ($C081 = manejador): partida $18B7, y attract
                // $11B4 en el sub-estado 6 ($1229, demo; $C07F=6 en N y N+1).
                int handler = cur[0x081] | cur[0x082] << 8;
                bool demo = handler == 0x11B4 && prev[0x07F] == 6 && cur[0x07F] == 6;
                if (handler != 0x18B7 && !demo)
                {
                    if (cur[0x442] != prev[0x442]) otherMoves[handler] = otherMoves.GetValueOrDefault(handler) + 1;
                    prev = cur; continue;
                }
                if (cur[0x450] != prev[0x450]) columns++;
                if (cur[0x444] != prev[0x444] || cur[0x445] != prev[0x445]) slopes++;
                if (cur[0x477] != 0) { skipped++; prev = cur; continue; }
                var mem = new byte[0x10000];
                Array.Copy(rom, mem, rom.Length);
                Array.Copy(prev, 0, mem, 0xC000, 0x1000);
                mem[0xC850] = cur[0x850]; mem[0xC851] = cur[0x851]; mem[0xC85F] = cur[0x85F];
                bool moved = Camera.Update(mem);
                pairs++;
                if (moved) moves++;
                if (cur[0x442] != prev[0x442] || cur[0x443] != prev[0x443]) dumpMoves++;
                var wrong = outs.Where(o => mem[0xC000 + o] != cur[o]).ToArray();
                if (wrong.Length > 0)
                {
                    bad++;
                    if (diffs.Count < 5)
                        diffs.Add($"  f{f}: " + string.Join(" ", wrong.Select(o =>
                            $"${0xC000 + o:X4} port={mem[0xC000 + o]:X2} ram={cur[o]:X2} (antes {prev[o]:X2})")));
                }
            }
            prev = cur;
        }
        Console.WriteLine($"{sub}: pares {pairs} (omitidos $C477 {skipped}), avances port {moves}, avances volcado {dumpMoves}, diferencias {bad}");
        Console.WriteLine($"  cambios de columna $C450 {columns}, cambios de $C444 {slopes}; avances de $C442 con otro manejador: " +
                          (otherMoves.Count == 0 ? "ninguno" : string.Join(", ", otherMoves.Select(kv => $"${kv.Key:X4}x{kv.Value}"))));
        foreach (var s in diffs) Console.WriteLine(s);
        totalPairs += pairs; totalMoves += moves; totalBad += bad;
    }
    Console.WriteLine($"TOTAL: pares {totalPairs}, avances {totalMoves}, diferencias {totalBad}");
    return;
}

// --check-tilemap: TilemapFill.Update ($2297) tras Camera.Update ($21CF) sobre la RAM del volcado N
// (mismas entradas de N+1 que --check-camera); compara con N+1 los bytes de RAM que escribe $2297
// ($C30F $C452-$C45E). Sus escrituras en VRAM $E000-$E7FF no estan en los volcados y no se comparan.
if (args.Contains("--check-tilemap"))
{
    byte[] rom = File.ReadAllBytes(Path.Combine(FindDir("ram"), "..", "wboy2u_maincpu.bin"));
    int[] outs = { 0x30F, 0x452, 0x453, 0x456, 0x457, 0x458, 0x459, 0x45A, 0x45B, 0x45C, 0x45D, 0x45E };
    int totalFrames = 0, totalPairs = 0, totalSteps = 0, totalBad = 0;
    foreach (var (sub, prefix) in new[] { ("ram", "frame_"), ("ram_huevo", "frame_"), ("ram_b1", "frame_"), ("ram_b2", "frame_"),
                                          ("ram_play", "frame_"), ("ram_sprites", "c000_") })
    {
        string dir = FindDir(sub);
        int frames = 0, pairs = 0, steps = 0, opens = 0, dumpSteps = 0, bad = 0, skipped = 0, endRound = 0;
        var otherSteps = new Dictionary<int, int>();
        var diffs = new List<string>();
        byte[]? prev = null;
        for (int f = 1; ; f++)
        {
            string path = Path.Combine(dir, $"{prefix}{f:D6}.bin");
            if (!File.Exists(path)) break;
            var cur = File.ReadAllBytes(path);
            frames++;
            if (prev != null)
            {
                int handler = cur[0x081] | cur[0x082] << 8;
                bool demo = handler == 0x11B4 && prev[0x07F] == 6 && cur[0x07F] == 6;
                bool changed = outs.Any(o => cur[o] != prev[o]);
                // $18BB: con $C440 != 0 el manejador salta al fin de ronda $1908 y $2297 solo corre en el
                // subestado 1 ($191D); el resto (p. ej. $1944, $C30F=0) no es de $2297.
                if (handler == 0x18B7 && prev[0x440] != 0 && prev[0x07F] != 1) { handler = 0x1908; endRound++; }
                if (handler != 0x18B7 && !demo)
                {
                    if (changed) otherSteps[handler] = otherSteps.GetValueOrDefault(handler) + 1;
                    prev = cur; continue;
                }
                if (cur[0x477] != 0) { skipped++; prev = cur; continue; }
                var mem = new byte[0x10000];
                Array.Copy(rom, mem, rom.Length);
                Array.Copy(prev, 0, mem, 0xC000, 0x1000);
                mem[0xC850] = cur[0x850]; mem[0xC851] = cur[0x851]; mem[0xC85F] = cur[0x85F];
                Camera.Update(mem);
                int r = TilemapFill.Update(mem);
                pairs++;
                if (r > 0) steps++;
                if (r == 2) opens++;
                if (cur[0x452] != prev[0x452]) dumpSteps++;
                var wrong = outs.Where(o => mem[0xC000 + o] != cur[o]).ToArray();
                if (wrong.Length > 0)
                {
                    bad++;
                    if (diffs.Count < 5)
                        diffs.Add($"  f{f}: " + string.Join(" ", wrong.Select(o =>
                            $"${0xC000 + o:X4} port={mem[0xC000 + o]:X2} ram={cur[o]:X2} (antes {prev[o]:X2})")));
                }
            }
            prev = cur;
        }
        Console.WriteLine($"{sub}: fotogramas {frames}, pares {pairs} (omitidos $C477 {skipped}), pasos port {steps} " +
                          $"(columnas nuevas {opens}), pasos volcado {dumpSteps}, diferencias {bad}");
        Console.WriteLine("  cambios en $C30F/$C452-$C45E con otro manejador: " +
                          (otherSteps.Count == 0 ? "ninguno" : string.Join(", ", otherSteps.Select(kv => $"${kv.Key:X4}x{kv.Value}"))) +
                          $" | pares omitidos por $C440!=0 y $C07F!=1 ($1908): {endRound}");
        foreach (var s in diffs) Console.WriteLine(s);
        totalFrames += frames; totalPairs += pairs; totalSteps += steps; totalBad += bad;
    }
    Console.WriteLine($"TOTAL: fotogramas {totalFrames}, pares {totalPairs}, pasos {totalSteps}, diferencias {totalBad}");
    return;
}

// --check-world: WorldToScreen.Update ($239E) tras Camera ($21CF) y TilemapFill ($2297) sobre la RAM
// del volcado N (mismas entradas de N+1 que --check-camera). Las ranuras $C800-$CA1F se actualizan
// en el mismo tick antes de $239E ($274D/$4CD2/$2BD5/$24EB), asi que sus bytes de entrada se toman de
// N+1; los de salida (+0 +1 +2 +3 +6 +7 y bits 1/5 de +$1E) se dejan con el valor de N para que el
// port tenga que escribirlos. Compara con N+1: esos bytes de las 17 ranuras, $C47A/$C47B y $C3DF.
if (args.Contains("--check-world"))
{
    byte[] rom = File.ReadAllBytes(Path.Combine(FindDir("ram"), "..", "wboy2u_maincpu.bin"));
    var outs = new List<int> { 0x3DF, 0x47A, 0x47B };
    int[] slotOuts = { 0x00, 0x01, 0x02, 0x03, 0x06, 0x07, 0x1E };
    for (int s = 0; s < WorldToScreen.SlotCount; s++)
        foreach (int o in slotOuts) outs.Add(0x800 + s * WorldToScreen.SlotSize + o);
    int totalPairs = 0, totalBad = 0, totalBytes = 0, totalWrong = 0;
    foreach (var (sub, prefix) in new[] { ("ram", "frame_"), ("ram_huevo", "frame_"), ("ram_b1", "frame_"), ("ram_b2", "frame_"),
                                          ("ram_play", "frame_"), ("ram_sprites", "c000_") })
    {
        string dir = FindDir(sub);
        int pairs = 0, bad = 0, skipped = 0, active = 0, visible = 0, bytes = 0, wrongBytes = 0, changed = 0;
        var wrongBy = new Dictionary<int, int>();
        var diffs = new List<string>();
        byte[]? prev = null;
        for (int f = 1; ; f++)
        {
            string path = Path.Combine(dir, $"{prefix}{f:D6}.bin");
            if (!File.Exists(path)) break;
            var cur = File.ReadAllBytes(path);
            if (prev != null)
            {
                int handler = cur[0x081] | cur[0x082] << 8;
                bool demo = handler == 0x11B4 && prev[0x07F] == 6 && cur[0x07F] == 6;
                if (handler != 0x18B7 && !demo) { prev = cur; continue; }
                if (cur[0x477] != 0) { skipped++; prev = cur; continue; }
                var mem = new byte[0x10000];
                Array.Copy(rom, mem, rom.Length);
                Array.Copy(prev, 0, mem, 0xC000, 0x1000);
                mem[0xC850] = cur[0x850]; mem[0xC851] = cur[0x851]; mem[0xC85F] = cur[0x85F];
                Camera.Update(mem);
                TilemapFill.Update(mem);
                for (int s = 0; s < WorldToScreen.SlotCount; s++)
                {
                    int b = 0x800 + s * WorldToScreen.SlotSize;
                    for (int o = 0; o < WorldToScreen.SlotSize; o++)
                        if (Array.IndexOf(slotOuts, o) < 0) mem[0xC000 + b + o] = cur[b + o];
                    mem[0xC000 + b + 0x1E] = (byte)((cur[b + 0x1E] & ~0x22) | (prev[b + 0x1E] & 0x22));
                    if (cur[b + 0x1F] != 0) active++;
                }
                WorldToScreen.Update(mem);
                for (int s = 0; s < WorldToScreen.SlotCount; s++)
                    if ((mem[0xC800 + s * WorldToScreen.SlotSize + 0x1E] & 0x02) != 0) visible++;
                pairs++;
                bytes += outs.Count;
                changed += outs.Count(o => cur[o] != prev[o]);
                var wrong = outs.Where(o => mem[0xC000 + o] != cur[o]).ToArray();
                if (wrong.Length > 0)
                {
                    bad++;
                    wrongBytes += wrong.Length;
                    foreach (int o in wrong) wrongBy[o] = wrongBy.GetValueOrDefault(o) + 1;
                    if (diffs.Count < 5)
                        diffs.Add($"  f{f}: " + string.Join(" ", wrong.Take(8).Select(o =>
                            $"${0xC000 + o:X4} port={mem[0xC000 + o]:X2} ram={cur[o]:X2} (antes {prev[o]:X2})")));
                }
            }
            prev = cur;
        }
        Console.WriteLine($"{sub}: pares {pairs} (omitidos $C477 {skipped}), ranuras activas {active}, en pantalla {visible}, " +
                          $"bytes comparados {bytes} (cambian N->N+1 {changed}), bytes distintos {wrongBytes}, diferencias {bad}");
        if (wrongBy.Count > 0)
            Console.WriteLine("  por direccion: " + string.Join(", ", wrongBy.OrderByDescending(kv => kv.Value).Take(10)
                                                         .Select(kv => $"${0xC000 + kv.Key:X4}x{kv.Value}")));
        foreach (var s in diffs) Console.WriteLine(s);
        totalPairs += pairs; totalBad += bad; totalBytes += bytes; totalWrong += wrongBytes;
    }
    Console.WriteLine($"TOTAL: pares {totalPairs}, bytes comparados {totalBytes}, bytes distintos {totalWrong}, diferencias {totalBad}");
    return;
}

// --check-slots: SlotUpdate.Update ($24EB, objeto 0 $C800) sobre la RAM del volcado N. Corre en el tick
// tras $274D/$4CD2/$2BD5 y antes de $21CF, asi que lee $C443 de N y solo toma de N+1 la X del jugador
// $C850/$C851 (la escriben $274D/$4CD2). Compara con N+1 los 26 bytes de la ranura 0 que $239E no
// escribe (todos salvo +0 +1 +2 +3 +6 +7) y +$1E sin los bits 1/5 de $239E.
if (args.Contains("--check-slots"))
{
    byte[] rom = File.ReadAllBytes(Path.Combine(FindDir("ram"), "..", "wboy2u_maincpu.bin"));
    int[] skip = { 0x00, 0x01, 0x02, 0x03, 0x06, 0x07 };
    int[] outs = Enumerable.Range(0x800, 0x20).Where(o => Array.IndexOf(skip, o - 0x800) < 0).ToArray();
    int Mask(int o) => o == 0x81E ? 0xDD : 0xFF;
    int totalPairs = 0, totalBad = 0, totalBytes = 0, totalWrong = 0;
    foreach (var (sub, prefix) in new[] { ("ram", "frame_"), ("ram_huevo", "frame_"), ("ram_b1", "frame_"), ("ram_b2", "frame_"),
                                          ("ram_play", "frame_"), ("ram_sprites", "c000_") })
    {
        string dir = FindDir(sub);
        int pairs = 0, bad = 0, skipped = 0, bytes = 0, wrongBytes = 0, changed = 0, offs = 0, steps = 0;
        var wrongBy = new Dictionary<int, int>();
        var otherSteps = new Dictionary<int, int>();
        var diffs = new List<string>();
        byte[]? prev = null;
        for (int f = 1; ; f++)
        {
            string path = Path.Combine(dir, $"{prefix}{f:D6}.bin");
            if (!File.Exists(path)) break;
            var cur = File.ReadAllBytes(path);
            if (prev != null)
            {
                int handler = cur[0x081] | cur[0x082] << 8;
                bool demo = handler == 0x11B4 && prev[0x07F] == 6 && cur[0x07F] == 6;
                if (handler != 0x18B7 && !demo)
                {
                    if (outs.Any(o => ((cur[o] ^ prev[o]) & Mask(o)) != 0))
                        otherSteps[handler] = otherSteps.GetValueOrDefault(handler) + 1;
                    prev = cur; continue;
                }
                if (cur[0x477] != 0) { skipped++; prev = cur; continue; }
                var mem = new byte[0x10000];
                Array.Copy(rom, mem, rom.Length);
                Array.Copy(prev, 0, mem, 0xC000, 0x1000);
                mem[0xC850] = cur[0x850]; mem[0xC851] = cur[0x851];
                int r = SlotUpdate.Update(mem);
                if (r == 1) offs++;
                if (r == 2) steps++;
                pairs++;
                bytes += outs.Length;
                changed += outs.Count(o => ((cur[o] ^ prev[o]) & Mask(o)) != 0);
                var wrong = outs.Where(o => ((mem[0xC000 + o] ^ cur[o]) & Mask(o)) != 0).ToArray();
                if (wrong.Length > 0)
                {
                    bad++;
                    wrongBytes += wrong.Length;
                    foreach (int o in wrong) wrongBy[o] = wrongBy.GetValueOrDefault(o) + 1;
                    if (diffs.Count < 5)
                        diffs.Add($"  f{f}: " + string.Join(" ", wrong.Take(8).Select(o =>
                            $"${0xC000 + o:X4} port={mem[0xC000 + o]:X2} ram={cur[o]:X2} (antes {prev[o]:X2})")));
                }
            }
            prev = cur;
        }
        Console.WriteLine($"{sub}: pares {pairs} (omitidos $C477 {skipped}), via $2D7F {offs}, avances $C819 {steps}, " +
                          $"bytes comparados {bytes} (cambian N->N+1 {changed}), bytes distintos {wrongBytes}, diferencias {bad}");
        Console.WriteLine("  cambios en la ranura 0 con otro manejador: " +
                          (otherSteps.Count == 0 ? "ninguno" : string.Join(", ", otherSteps.Select(kv => $"${kv.Key:X4}x{kv.Value}"))));
        if (wrongBy.Count > 0)
            Console.WriteLine("  por direccion: " + string.Join(", ", wrongBy.OrderByDescending(kv => kv.Value).Take(10)
                                                         .Select(kv => $"${0xC000 + kv.Key:X4}x{kv.Value}")));
        foreach (var s in diffs) Console.WriteLine(s);
        totalPairs += pairs; totalBad += bad; totalBytes += bytes; totalWrong += wrongBytes;
    }
    Console.WriteLine($"TOTAL: pares {totalPairs}, bytes comparados {totalBytes}, bytes distintos {totalWrong}, diferencias {totalBad}");
    return;
}

// --check-spawn: ObjectSpawn.Spawn ($2CA5 + $2D46) sobre la RAM del volcado N, solo en los pares donde
// IX+$18 de alguna ranura $C8A0 + n*$20 (n = 0..11) pasa de 0 a distinto de 0. Recorre las ranuras en el
// orden de $2BD5: llama a $2CA5 si IX+$18 bit7 = 1, o si IX+$18 = 0 y $C479 = 0 (las activas van a
// $2D9B, sin portar, y se saltan). Compara con N+1 los bytes que escribe: ranuras creadas/afectadas
// (+$10-$1B, +$1E sin bits 1/5 de $239E, +$1F), $C401-$C406 y la entrada escrita de la cola $C0C7.
if (args.Contains("--check-spawn"))
{
    byte[] rom = File.ReadAllBytes(Path.Combine(FindDir("ram"), "..", "wboy2u_maincpu.bin"));
    int[] slotOffs = { 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1A, 0x1B, 0x1E, 0x1F };
    int Mask(int o) => (o & 0x1F) == 0x1E ? 0xDD : 0xFF;
    int totalPairs = 0, totalBad = 0, totalBytes = 0, totalWrong = 0;
    foreach (var (sub, prefix) in new[] { ("ram", "frame_"), ("ram_huevo", "frame_"), ("ram_b1", "frame_"), ("ram_b2", "frame_"),
                                          ("ram_play", "frame_"), ("ram_sprites", "c000_") })
    {
        string dir = FindDir(sub);
        int pairs = 0, bad = 0, skipped = 0, bytes = 0, wrongBytes = 0, other = 0, queueDrained = 0;
        var results = new Dictionary<int, int>();
        var wrongBy = new Dictionary<int, int>();
        var diffs = new List<string>();
        byte[]? prev = null;
        for (int f = 1; ; f++)
        {
            string path = Path.Combine(dir, $"{prefix}{f:D6}.bin");
            if (!File.Exists(path)) break;
            var cur = File.ReadAllBytes(path);
            if (prev != null)
            {
                var born = Enumerable.Range(0, 12).Select(n => 0x8A0 + n * 0x20)
                                     .Where(s => prev[s + 0x18] == 0 && cur[s + 0x18] != 0).ToList();
                int handler = cur[0x081] | cur[0x082] << 8;
                bool demo = handler == 0x11B4 && prev[0x07F] == 6 && cur[0x07F] == 6;
                if (born.Count > 0 && handler != 0x18B7 && !demo) other++;
                if (born.Count == 0 || (handler != 0x18B7 && !demo)) { prev = cur; continue; }
                if (cur[0x477] != 0) { skipped++; prev = cur; continue; }
                var mem = new byte[0x10000];
                Array.Copy(rom, mem, rom.Length);
                Array.Copy(prev, 0, mem, 0xC000, 0x1000);
                if (mem[0xC009] != 0) ObjectSpawn.Drain(mem);   // IRQ $0038 -> $07A6 antes del tick
                var touched = new HashSet<int>(born);
                int queueAt = -1;
                for (int n = 0; n < 12; n++)
                {
                    int ix = 0xC8A0 + n * 0x20;
                    byte t = mem[ix + 0x18];
                    if ((t & 0x80) == 0 && (t != 0 || mem[0xC479] != 0)) continue;  // $2BE2-$2BF4
                    int q0 = Array.FindIndex(Enumerable.Range(0, 6).ToArray(), i => mem[ObjectSpawn.Queue + 3 * i] == 0);
                    int r = ObjectSpawn.Spawn(mem, ix);
                    results[r] = results.GetValueOrDefault(r) + 1;
                    if (r >= 2) { touched.Add(ix - 0xC000); if (r != 4) queueAt = ObjectSpawn.Queue + 3 * q0 - 0xC000; }
                }
                var outs = touched.SelectMany(s => slotOffs.Select(o => s + o))
                                  .Concat(Enumerable.Range(0x401, 6)).ToList();
                if (queueAt >= 0)
                {
                    if (cur[queueAt] == 0) queueDrained++;   // la vacio $07A6 (IRQ) antes del volcado
                    else outs.AddRange(Enumerable.Range(0x0C7, 0x13));   // cola completa $C0C7-$C0D9
                }
                pairs++;
                bytes += outs.Count;
                var wrong = outs.Where(o => ((mem[0xC000 + o] ^ cur[o]) & Mask(o)) != 0).ToArray();
                if (wrong.Length > 0)
                {
                    bad++;
                    wrongBytes += wrong.Length;
                    foreach (int o in wrong) wrongBy[o] = wrongBy.GetValueOrDefault(o) + 1;
                    if (diffs.Count < 5)
                        diffs.Add($"  f{f}: " + string.Join(" ", wrong.Take(8).Select(o =>
                            $"${0xC000 + o:X4} port={mem[0xC000 + o]:X2} ram={cur[o]:X2} (antes {prev[o]:X2})")));
                }
            }
            prev = cur;
        }
        Console.WriteLine($"{sub}: pares de creacion {pairs} (omitidos $C477 {skipped}, con otro manejador {other}), " +
                          $"bytes comparados {bytes}, bytes distintos {wrongBytes}, diferencias {bad}, " +
                          $"cola ya vaciada por $07A6 {queueDrained}");
        Console.WriteLine("  llamadas a $2CA5 por resultado: " + string.Join(", ", results.OrderBy(kv => kv.Key)
                                                                   .Select(kv => $"r{kv.Key}x{kv.Value}")));
        if (wrongBy.Count > 0)
            Console.WriteLine("  por direccion: " + string.Join(", ", wrongBy.OrderByDescending(kv => kv.Value).Take(10)
                                                         .Select(kv => $"${0xC000 + kv.Key:X4}x{kv.Value}")));
        foreach (var s in diffs) Console.WriteLine(s);
        totalPairs += pairs; totalBad += bad; totalBytes += bytes; totalWrong += wrongBytes;
    }
    Console.WriteLine($"TOTAL: pares {totalPairs}, bytes comparados {totalBytes}, bytes distintos {totalWrong}, diferencias {totalBad}");
    return;
}

// --check-enemies: bucle de ranuras de $2BD5 ($2CA5 o $2D9B por ranura $C8A0 + n*$20, n = 0..11, y
// IX+$18 = 0 si IX+$1F = 0) sobre la RAM del volcado N. $274D y $4CD2 corren antes en el tick, asi que
// el jugador y las armas $C840-$C89F se toman de N+1 (salvo IX+$1E bit2 de las armas, que pone $20D0).
// Compara con N+1, en las ranuras que pasan por $2D9B con camino portado, +$04 +$05 +$08-$1F
// (+$1E sin bits 1/5 de $239E). Las ranuras cuyo manejador o estado no esta portado no se comparan.
if (args.Contains("--check-enemies"))
{
    byte[] rom = File.ReadAllBytes(Path.Combine(FindDir("ram"), "..", "wboy2u_maincpu.bin"));
    int[] slotOffs = new[] { 0x04, 0x05 }.Concat(Enumerable.Range(0x08, 0x18)).ToArray();
    int Mask(int o) => (o & 0x1F) == 0x1E ? 0xDD : 0xFF;
    int totalPairs = 0, totalSlots = 0, totalBad = 0, totalBytes = 0, totalWrong = 0;
    var allType = new SortedDictionary<int, (int slots, int bytes, int wrong)>();
    var allMissing = new SortedDictionary<int, int>();
    foreach (var (sub, prefix) in new[] { ("ram", "frame_"), ("ram_huevo", "frame_"), ("ram_b1", "frame_"), ("ram_b2", "frame_"),
                                          ("ram_play", "frame_"), ("ram_sprites", "c000_"), ("ram_auto_r2", "frame_"), ("ram_40c4_auto", "frame_") })
    {
        string dir = FindDir(sub);
        int pairs = 0, slots = 0, bad = 0, skipped = 0, bytes = 0, wrongBytes = 0, changed = 0;
        var wrongBy = new Dictionary<int, int>();
        var diffs = new List<string>();
        byte[]? prev = null;
        for (int f = 1; ; f++)
        {
            string path = Path.Combine(dir, $"{prefix}{f:D6}.bin");
            if (!File.Exists(path)) break;
            var cur = File.ReadAllBytes(path);
            if (prev != null)
            {
                int handler = cur[0x081] | cur[0x082] << 8;
                bool demo = handler == 0x11B4 && prev[0x07F] == 6 && cur[0x07F] == 6;
                if (handler != 0x18B7 && !demo) { prev = cur; continue; }
                if (handler == 0x18B7 && prev[0x440] != 0 && prev[0x07F] != 1) { prev = cur; continue; } // $1908: solo $191D llama a $2193
                if (cur[0x477] != 0) { skipped++; prev = cur; continue; }
                var mem = new byte[0x10000];
                Array.Copy(rom, mem, rom.Length);
                Array.Copy(prev, 0, mem, 0xC000, 0x1000);
                Array.Copy(cur, 0x840, mem, 0xC840, 0x60);              // $274D / $4CD2
                foreach (int w in new[] { 0xC87E, 0xC89E })
                    mem[w] = (byte)((cur[w - 0xC000] & ~0x04) | (prev[w - 0xC000] & 0x04));
                if (cur[0x85F] == 0x83 && cur[0x857] == 0x70 && prev[0x85F] != 0x83)
                    mem[0xC85F] = 0x82;                                 // $4084 pone $C857/$C85F despues de $274D ($3F80 pide estado 2)
                if (mem[0xC009] != 0) ObjectSpawn.Drain(mem);           // IRQ $0038 -> $07A6 antes del tick
                mem[0xC46F] = 0x00;                                     // $2BDE
                var compared = new List<(int s, int type)>();
                for (int n = 0; n < 12; n++)
                {
                    int ix = 0xC8A0 + n * 0x20;
                    mem[0xC3DF] = (byte)(12 - n);
                    byte t = mem[ix + 0x18];
                    if ((t & 0x80) != 0 || (t == 0 && mem[0xC479] == 0)) { ObjectSpawn.Spawn(mem, ix); continue; }
                    if (t == 0) continue;                               // $2BF4
                    int miss = Enemies.Dispatch(mem, ix);               // $2C06
                    if (miss != 0)
                    {
                        allMissing[miss] = allMissing.GetValueOrDefault(miss) + 1;
                        continue;
                    }
                    if (mem[ix + 0x1F] == 0) mem[ix + 0x18] = 0x00;     // $2C09-$2C0F
                    compared.Add((ix - 0xC000, t));
                }
                if (compared.Count == 0) { prev = cur; continue; }
                pairs++;
                foreach (var (s, type) in compared)
                {
                    slots++;
                    bytes += slotOffs.Length;
                    changed += slotOffs.Count(o => ((cur[s + o] ^ prev[s + o]) & Mask(o)) != 0);
                    var wrong = slotOffs.Where(o => ((mem[0xC000 + s + o] ^ cur[s + o]) & Mask(o)) != 0).ToArray();
                    var st = allType.GetValueOrDefault(type);
                    allType[type] = (st.slots + 1, st.bytes + slotOffs.Length, st.wrong + wrong.Length);
                    if (wrong.Length == 0) continue;
                    bad++;
                    wrongBytes += wrong.Length;
                    foreach (int o in wrong) wrongBy[o] = wrongBy.GetValueOrDefault(o) + 1;
                    if (diffs.Count < 6)
                        diffs.Add($"  f{f} tipo ${type:X2} ${0xC000 + s:X4} ($1F {prev[s + 0x1F]:X2}): " + string.Join(" ", wrong.Take(8).Select(o =>
                            $"+{o:X2} port={mem[0xC000 + s + o]:X2} ram={cur[s + o]:X2} (antes {prev[s + o]:X2})")));
                }
            }
            prev = cur;
        }
        Console.WriteLine($"{sub}: pares {pairs} (omitidos $C477 {skipped}), ranuras comparadas {slots}, bytes comparados {bytes} " +
                          $"(cambian N->N+1 {changed}), bytes distintos {wrongBytes}, ranuras con diferencias {bad}");
        if (wrongBy.Count > 0)
            Console.WriteLine("  por desplazamiento: " + string.Join(", ", wrongBy.OrderByDescending(kv => kv.Value).Take(10)
                                                         .Select(kv => $"+{kv.Key:X2}x{kv.Value}")));
        foreach (var s in diffs) Console.WriteLine(s);
        totalPairs += pairs; totalSlots += slots; totalBad += bad; totalBytes += bytes; totalWrong += wrongBytes;
    }
    Console.WriteLine("por tipo (ranuras, bytes, distintos): " + string.Join(", ", allType.Select(kv =>
        $"${kv.Key:X2}->${Enemies.HandlerOf(rom.Concat(new byte[0x10000 - rom.Length]).ToArray(), kv.Key):X4} {kv.Value.slots}/{kv.Value.bytes}/{kv.Value.wrong}")));
    Console.WriteLine("sin portar (ranura-pares): " + (allMissing.Count == 0 ? "ninguno" :
        string.Join(", ", allMissing.Select(kv => $"${kv.Key:X4}x{kv.Value}"))));
    Console.WriteLine($"choques: jugador $205A {Enemies.TouchHits}, arma $20A9 {Enemies.WeaponHits}");
    Console.WriteLine($"TOTAL: pares {totalPairs}, ranuras {totalSlots}, bytes comparados {totalBytes}, bytes distintos {totalWrong}, diferencias {totalBad}");
    return;
}

// --check-player: PlayerRoutine.Update ($274D) sobre la RAM del volcado N; compara con N+1 X ($C850/$C851),
// Y ($C852/$C853), estado ($C85F) y velocidad X ($C854). Entradas: $C086 de N+1 (lo escribe la IRQ
// o $1249 antes de $2193) y $C084/$C085 recalculados con $00A4 sobre los de N. Mismos filtros que
// --check-camera; se omiten los pares con $C477 != 0 en N (no se llama a $274D) o en N+1.
if (args.Contains("--check-player"))
{
    byte[] rom = File.ReadAllBytes(Path.Combine(FindDir("ram"), "..", "wboy2u_maincpu.bin"));
    int[] outs = { 0x850, 0x851, 0x852, 0x853, 0x85F, 0x854 };
    int totalPairs = 0, totalBad = 0, totalInput = 0;
    var byState = new SortedDictionary<int, (int pairs, int bad)>();
    var wrongBy = new SortedDictionary<int, int>();
    var allMissing = new Dictionary<int, int>();
    foreach (var (sub, prefix) in new[] { ("ram", "frame_"), ("ram_huevo", "frame_"), ("ram_b1", "frame_"), ("ram_b2", "frame_"),
                                          ("ram_play", "frame_"), ("ram_sprites", "c000_") })
    {
        string dir = FindDir(sub);
        int pairs = 0, bad = 0, skipped = 0, inputBad = 0, moved = 0, enemyWrites = 0;
        var diffs = new List<string>();
        byte[]? prev = null;
        for (int f = 1; ; f++)
        {
            string path = Path.Combine(dir, $"{prefix}{f:D6}.bin");
            if (!File.Exists(path)) break;
            var cur = File.ReadAllBytes(path);
            if (prev != null)
            {
                int handler = cur[0x081] | cur[0x082] << 8;
                bool demo = handler == 0x11B4 && prev[0x07F] == 6 && cur[0x07F] == 6;
                if (handler != 0x18B7 && !demo) { prev = cur; continue; }
                if (prev[0x477] != 0 || cur[0x477] != 0) { skipped++; prev = cur; continue; }
                var mem = new byte[0x10000];
                Array.Copy(rom, mem, rom.Length);
                Array.Copy(prev, 0, mem, 0xC000, 0x1000);
                mem[0xC086] = cur[0x086];
                PlayerRoutine.ReadInputs(mem);
                if (mem[0xC085] != cur[0x085]) inputBad++;
                int miss = PlayerRoutine.Update(mem);
                if (miss != 0) allMissing[miss] = allMissing.GetValueOrDefault(miss) + 1;
                pairs++;
                if (outs.Any(o => cur[o] != prev[o])) moved++;
                int st = prev[0x85F];
                var s = byState.GetValueOrDefault(st);
                var wrong = outs.Where(o => mem[0xC000 + o] != cur[o]).ToArray();
                if (wrong.Length > 0)
                {
                    bad++;
                    foreach (int o in wrong) wrongBy[o] = wrongBy.GetValueOrDefault(o) + 1;
                    // Diagnostico: el bucle de enemigos $2BD5 corre despues de $274D en el mismo tick y
                    // tambien escribe $C85F ($2C45, $3352, $36F3...). Se ejecuta sobre la salida del port.
                    var after = (byte[])mem.Clone();
                    if (after[0xC009] != 0) ObjectSpawn.Drain(after);
                    after[0xC46F] = 0x00;
                    for (int n = 0; n < 12; n++)
                    {
                        int ix = 0xC8A0 + n * 0x20;
                        after[0xC3DF] = (byte)(12 - n);
                        byte t = after[ix + 0x18];
                        if ((t & 0x80) != 0 || (t == 0 && after[0xC479] == 0)) { ObjectSpawn.Spawn(after, ix); continue; }
                        if (t == 0) continue;
                        Enemies.Dispatch(after, ix);
                        if (after[ix + 0x1F] == 0) after[ix + 0x18] = 0x00;
                    }
                    bool explained = outs.All(o => after[0xC000 + o] == cur[o]);
                    if (explained) enemyWrites++;
                    if (diffs.Count < 6)
                        diffs.Add($"  f{f} ($1F {prev[0x85F]:X2}->{cur[0x85F]:X2}, $C084 {mem[0xC084]:X2}, $C086 {cur[0x086]:X2}): " +
                                  string.Join(" ", wrong.Select(o => $"${0xC000 + o:X4} port={mem[0xC000 + o]:X2} ram={cur[o]:X2} (antes {prev[o]:X2})")) +
                                  (explained ? " -> coincide tras $2BD5 (enemigos)" : " -> no lo explica $2BD5"));
                }
                byState[st] = (s.pairs + 1, s.bad + (wrong.Length > 0 ? 1 : 0));
            }
            prev = cur;
        }
        Console.WriteLine($"{sub}: pares {pairs} (omitidos $C477 {skipped}), con cambios {moved}, diferencias {bad} (explicadas por $2BD5 {enemyWrites}), $C085 recalculado distinto {inputBad}");
        foreach (var d in diffs) Console.WriteLine(d);
        totalPairs += pairs; totalBad += bad; totalInput += inputBad;
    }
    Console.WriteLine("por estado $C85F de N (pares/diferencias): " + string.Join(", ", byState.Select(kv => $"${kv.Key:X2} {kv.Value.pairs}/{kv.Value.bad}")));
    Console.WriteLine("por direccion: " + (wrongBy.Count == 0 ? "ninguna" : string.Join(", ", wrongBy.Select(kv => $"${0xC000 + kv.Key:X4}x{kv.Value}"))));
    Console.WriteLine("sin portar: " + (allMissing.Count == 0 ? "ninguno" : string.Join(", ", allMissing.Select(kv => $"${kv.Key:X4}x{kv.Value}"))));
    Console.WriteLine($"TOTAL: pares {totalPairs}, diferencias {totalBad}, $C085 distinto {totalInput}");
    return;
}

// --check-gameover: fin de partida de ram_rand ($C07E 9 -> 10 -> 11 -> 1 -> 2, fotogramas 4514..5366;
// en 5360..5366, vuelta a attract, tambien cuenta $C010).
// TitleRom.FromRam con la RAM del volcado 4513 (fin de $1B52 sub 2, $C302=0) y Step() por fotograma;
// solo se toma del volcado N la entrada $C00E. Se compara con el volcado N $C07E, $C081 (byte bajo)
// y $C302; otros bytes de control ($C07F, temporizadores, planificador, cuenta atras, cola de
// paleta) se informan aparte como "extra" (no cuentan como diferencia).
if (args.Contains("--check-gameover"))
{
    byte[] rom = File.ReadAllBytes(Path.Combine(FindDir("ram"), "..", "wboy2u_maincpu.bin"));
    string dir = FindDir("ram_rand");
    byte[] Dump(int f) => File.ReadAllBytes(Path.Combine(dir, $"frame_{f:D6}.bin"));
    const int First = 4514, Last = 5366, C010From = 5360; // $C010 cuenta como diferencia en 5360..5366
    var game = TitleRom.FromRam(rom, Dump(First - 1));
    int frames = 0, bad = 0, extraBad = 0;
    string prevKey = "";
    for (int f = First; f <= Last; f++)
    {
        var cur = Dump(f);
        game.Inputs = cur[0x00E];
        game.Step();
        frames++;
        bool ok = game[0xC07E] == cur[0x07E] && game[0xC081] == cur[0x081] && game[0xC302] == cur[0x302] &&
                  (f < C010From || game[0xC010] == cur[0x010]);
        int[] extraAddrs = { 0x07F, 0x013, 0x014, 0x010, 0x005, 0x030, 0x032, 0x3C9, 0x3DF, 0x3C5, 0x0AB };
        var extraWrong = extraAddrs.Where(o => game[0xC000 + o] != cur[o]).ToArray();
        bool extra = extraWrong.Length == 0;
        if (!ok) bad++;
        if (!extra) extraBad++;
        string key = $"{cur[0x07E]:X2}{cur[0x07F]:X2}{game[0xC07E]:X2}{game[0xC07F]:X2}";
        if (!ok || !extra || key != prevKey || f == First || f == Last)
            Console.WriteLine($"{f}: $C07E {cur[0x07E]}/{game[0xC07E]} $C081 {cur[0x081]:X2}/{game[0xC081]:X2} " +
                              $"(${cur[0x081] | cur[0x082] << 8:X4}/${game[0xC081] | game[0xC082] << 8:X4}) $C302 {cur[0x302]}/{game[0xC302]} | " +
                              $"$C07F {cur[0x07F]}/{game[0xC07F]} $C013 {cur[0x013]}/{game[0xC013]} $C010 {cur[0x010]:X2}/{game[0xC010]:X2}" +
                              (ok ? "  ok" : "  DIF") + (extra ? "" : " (extra: " + string.Join(" ", extraWrong.Select(o => $"${0xC000 + o:X4}")) + ")"));
        prevKey = key;
    }
    Console.WriteLine("sin portar: " + (game.Unported ?? "ninguno") + $"; modo final: {(game.GameMode ? "juego" : "attract")}, estado {game.State}");
    Console.WriteLine($"TOTAL: fotogramas {frames}, con diferencias ($C07E/$C081/$C302; $C010 en {C010From}-{Last}) {bad}, " +
                      $"con extra distinto ($C07F $C013 $C014 $C010 $C005 $C030 $C032 $C3C9 $C3DF $C3C5 $C0AB) {extraBad}");
    return;
}

// --check-round-end: paso f8012 -> f8013 de ram_huevo (fin de ronda $1908, $C440=1). En f8012 $C07F=3:
// la ROM esta en la ultima espera de $199A; en f8013 ya ha hecho $C07F++ ($193F) y $1944 en el mismo
// fotograma. TitleRom.FromRam con la RAM de f8012, ResumeAtLastBonusWait() y un Step() con la entrada
// $C00E de f8013. Se comparan los bytes que escribe $1944 (y $C313/$C302, que lee); el resto de bytes
// distintos entre f8012 y f8013 se informa como "extra" (no cuenta).
if (args.Contains("--check-round-end"))
{
    byte[] rom = File.ReadAllBytes(Path.Combine(FindDir("ram"), "..", "wboy2u_maincpu.bin"));
    string dir = FindDir("ram_huevo");
    byte[] Dump(int f) => File.ReadAllBytes(Path.Combine(dir, $"frame_{f:D6}.bin"));
    const int N = 8012;
    byte[] before = Dump(N), after = Dump(N + 1);
    var game = TitleRom.FromRam(rom, before);
    game.ResumeAtLastBonusWait();
    game.Inputs = after[0x00E];
    game.Step();
    int[] addrs = { 0x30B, 0x30C, 0x30F, 0x311, 0x312, 0x07F, 0x313, 0x301, 0x07E, 0x302, 0x032 };
    int bad = 0;
    foreach (int o in addrs)
    {
        bool ok = game[0xC000 + o] == after[o];
        if (!ok) bad++;
        Console.WriteLine($"${0xC000 + o:X4}: f{N} {before[o]:X2} -> f{N + 1} {after[o]:X2} port {game[0xC000 + o]:X2}" + (ok ? "  ok" : "  DIF"));
    }
    var extra = Enumerable.Range(0, 0x1000).Where(o => !addrs.Contains(o) && (before[o] != after[o] || game[0xC000 + o] != after[o]))
                          .Select(o => $"${0xC000 + o:X4} {after[o]:X2}/{game[0xC000 + o]:X2}" + (game[0xC000 + o] == after[o] ? "" : "*"));
    Console.WriteLine("extra (RAM/port, * distinto): " + string.Join(", ", extra));
    Console.WriteLine("sin portar: " + (game.Unported ?? "ninguno") + $"; estado {game.State}");
    Console.WriteLine($"TOTAL: bytes {addrs.Length}, diferencias {bad}");
    return;
}

// --check-round2: ram_goal_scroll de f2091 a f5274 (ronda 2, bonificacion, transicion, ronda 3).
// TitleRom.FromRam con la RAM de f2090 ($152D acabado, $C07F=0, $C032=0) y Step() por fotograma con la
// entrada $C00E del volcado N. No se semilla de f2091: alli $1431 ($1651 sub 0) va por la primera de sus
// 4 pasadas ($C3DF=$16, $C010 sin decrementar en f2092-f2093) y FromRam no puede reanudarlo a medias. Se informa la primera divergencia por direccion y,
// si el port se detiene (Unported), el fotograma, el estado y la rutina.
if (args.Contains("--check-round2"))
{
    byte[] rom = File.ReadAllBytes(Path.Combine(FindDir("ram"), "..", "wboy2u_maincpu.bin"));
    string dir = FindDir("ram_goal_scroll");
    byte[] Dump(int f) => File.ReadAllBytes(Path.Combine(dir, $"frame_{f:D6}.bin"));
    const int Seed = 2090, Last = 5274;
    int[] addrs = { 0x07E, 0x07F, 0x440, 0x443, 0x450, 0x479, 0x313, 0x85F, 0x850, 0x851, 0x852, 0x853,
                    0x016, 0x302, 0x3DF, 0x032 };
    var game = TitleRom.FromRam(rom, Dump(Seed));
    var first = new Dictionary<int, string>();
    int frames = 0, bad = 0, stopFrame = -1;
    for (int f = Seed + 1; f <= Last; f++)
    {
        var cur = Dump(f);
        game.Inputs = cur[0x00E];
        game.Step();
        frames++;
        var wrong = addrs.Where(o => game[0xC000 + o] != cur[o]).ToArray();
        if (wrong.Length > 0) bad++;
        foreach (int o in wrong)
            if (!first.ContainsKey(o))
                first[o] = $"f{f} RAM {cur[o]:X2} port {game[0xC000 + o]:X2} " +
                           $"($C07E {cur[0x07E]}/{game[0xC07E]} $C07F {cur[0x07F]}/{game[0xC07F]} " +
                           $"$C081 ${cur[0x081] | cur[0x082] << 8:X4}/${game[0xC081] | game[0xC082] << 8:X4})";
        if (game.Unported != null) { stopFrame = f; break; }
    }
    foreach (int o in addrs)
        Console.WriteLine($"${0xC000 + o:X4}: " + (first.TryGetValue(o, out var s) ? s : "ok"));
    Console.WriteLine("sin portar: " + (game.Unported ?? "ninguno") +
                      (stopFrame >= 0 ? $" en f{stopFrame}" : "") +
                      $"; $C07E {game[0xC07E]}, $C07F {game[0xC07F]}, $C081 ${game[0xC081] | game[0xC082] << 8:X4}");
    Console.WriteLine($"TOTAL: fotogramas {frames}, con diferencias {bad}, direcciones distintas {first.Count}");
    return;
}

// --check-round2-clean: ram_round2_clean (MAME -state r2 sin entradas, round2_clean.lua) f1..ultimo.
// Semilla: ram_auto_r2 f31034 (RAM al guardar r2: $C07E=7, $C443=1, $C850=$00C1). El volcado N de
// ram_round2_clean es el fotograma N tras cargar r2. Step() por fotograma con $C00E = 0. Se informa la
// primera divergencia por direccion y, si el port se detiene (Unported), el fotograma y la rutina.
if (args.Contains("--check-round2-clean"))
{
    byte[] rom = File.ReadAllBytes(Path.Combine(FindDir("ram"), "..", "wboy2u_maincpu.bin"));
    string dir = FindDir("ram_round2_clean");
    byte[] Dump(int f) => File.ReadAllBytes(Path.Combine(dir, $"frame_{f:D6}.bin"));
    int last = Directory.GetFiles(dir, "frame_*.bin").Length;
    int[] addrs = { 0x07E, 0x07F, 0x440, 0x443, 0x450, 0x479, 0x313, 0x85F, 0x850, 0x851, 0x852, 0x853,
                    0x016, 0x302, 0x3DF, 0x032 };
    var game = TitleRom.FromRam(rom, File.ReadAllBytes(Path.Combine(FindDir("ram_auto_r2"), "frame_031034.bin")));
    var first = new Dictionary<int, string>();
    int frames = 0, bad = 0, stopFrame = -1;
    for (int f = 1; f <= last; f++)
    {
        var cur = Dump(f);
        game.Inputs = 0;
        game.Step();
        frames++;
        var wrong = addrs.Where(o => game[0xC000 + o] != cur[o]).ToArray();
        if (wrong.Length > 0) bad++;
        foreach (int o in wrong)
            if (!first.ContainsKey(o))
                first[o] = $"f{f} RAM {cur[o]:X2} port {game[0xC000 + o]:X2} " +
                           $"($C07E {cur[0x07E]}/{game[0xC07E]} $C07F {cur[0x07F]}/{game[0xC07F]} " +
                           $"$C081 ${cur[0x081] | cur[0x082] << 8:X4}/${game[0xC081] | game[0xC082] << 8:X4})";
        if (game.Unported != null) { stopFrame = f; break; }
    }
    foreach (int o in addrs)
        Console.WriteLine($"${0xC000 + o:X4}: " + (first.TryGetValue(o, out var s) ? s : "ok"));
    Console.WriteLine("sin portar: " + (game.Unported ?? "ninguno") +
                      (stopFrame >= 0 ? $" en f{stopFrame}" : "") +
                      $"; $C07E {game[0xC07E]}, $C07F {game[0xC07F]}, $C081 ${game[0xC081] | game[0xC082] << 8:X4}");
    Console.WriteLine($"TOTAL: fotogramas {frames}, con diferencias {bad}, direcciones distintas {first.Count}");
    return;
}

// --check-round2-move: ram_round2_move (MAME -state r2 con entradas, round2_move.lua) f1..ultimo.
// Misma semilla y comparacion que --check-round2-clean mas $C854, $C840, $C84A y $C84C. inputs.csv
// (frame,right,left,b1,b2): la fila N se pone en el frame_done N y el juego la lee en el fotograma N+1,
// asi que el Step() del fotograma f usa la fila f-1 (f1: sin entradas). Bits -> $C00C como en --play
// (LEFT $80, RIGHT $40, BUTTON2 $04, BUTTON1 $02); $C00E = 0. La escritura de vitalidad del script
// ($C469 < $0300 con $C07E = 7 y $C440 = 0) no ocurre en esta captura: no se reproduce.
if (args.Contains("--check-round2-move"))
{
    byte[] rom = File.ReadAllBytes(Path.Combine(FindDir("ram"), "..", "wboy2u_maincpu.bin"));
    string dir = FindDir("ram_round2_move");
    byte[] Dump(int f) => File.ReadAllBytes(Path.Combine(dir, $"frame_{f:D6}.bin"));
    int last = Directory.GetFiles(dir, "frame_*.bin").Length;
    var joyAt = new Dictionary<int, byte>();
    foreach (var line in File.ReadLines(Path.Combine(dir, "inputs.csv")).Skip(1))
    {
        var v = line.Split(',').Select(int.Parse).ToArray(); // frame,right,left,b1,b2
        joyAt[v[0]] = (byte)((v[2] != 0 ? 0x80 : 0) | (v[1] != 0 ? 0x40 : 0) | (v[4] != 0 ? 0x04 : 0) | (v[3] != 0 ? 0x02 : 0));
    }
    int[] addrs = { 0x07E, 0x07F, 0x440, 0x443, 0x450, 0x479, 0x313, 0x85F, 0x850, 0x851, 0x852, 0x853,
                    0x016, 0x302, 0x3DF, 0x032, 0x854, 0x840, 0x84A, 0x84C };
    // play: true -> PlayIrq copia $C00C a $C086 y llama a $00A4, como en --play (sin el, Joystick no llega al jugador).
    var game = TitleRom.FromRam(rom, File.ReadAllBytes(Path.Combine(FindDir("ram_auto_r2"), "frame_031034.bin")), play: true);
    var first = new Dictionary<int, string>();
    int frames = 0, bad = 0, stopFrame = -1, firstBad = -1;
    for (int f = 1; f <= last; f++)
    {
        var cur = Dump(f);
        game.Joystick = joyAt.GetValueOrDefault(f - 1);
        game.Inputs = 0;
        game.Step();
        frames++;
        var wrong = addrs.Where(o => game[0xC000 + o] != cur[o]).ToArray();
        if (wrong.Length > 0) { bad++; if (firstBad < 0) firstBad = f; }
        foreach (int o in wrong)
            if (!first.ContainsKey(o))
                first[o] = $"f{f} RAM {cur[o]:X2} port {game[0xC000 + o]:X2} " +
                           $"($C07E {cur[0x07E]}/{game[0xC07E]} $C07F {cur[0x07F]}/{game[0xC07F]} " +
                           $"$C081 ${cur[0x081] | cur[0x082] << 8:X4}/${game[0xC081] | game[0xC082] << 8:X4})";
        if (game.Unported != null) { stopFrame = f; break; }
    }
    foreach (int o in addrs)
        Console.WriteLine($"${0xC000 + o:X4}: " + (first.TryGetValue(o, out var s) ? s : "ok"));
    Console.WriteLine("primera divergencia: " + (firstBad < 0 ? "ninguna" : $"f{firstBad}"));
    Console.WriteLine("sin portar: " + (game.Unported ?? "ninguno") +
                      (stopFrame >= 0 ? $" en f{stopFrame}" : "") +
                      $"; $C07E {game[0xC07E]}, $C07F {game[0xC07F]}, $C081 ${game[0xC081] | game[0xC082] << 8:X4}");
    Console.WriteLine($"TOTAL: fotogramas {frames}/{last}, con diferencias {bad}, direcciones distintas {first.Count}");
    return;
}

// --check-game-over-play: ram_fin_juego (MAME -state r2 con entradas hasta el fin de partida) f1..ultimo.
// Misma semilla (ram_auto_r2 f31034), alineacion, entradas y comparacion que --check-round2-move: el volcado
// N es el fotograma N tras cargar r2 (f1..f3 coinciden byte a byte con ram_round2_move) y el Step() del
// fotograma f usa la fila f-1 de inputs.csv. Ademas informa la secuencia de estados $C07E/$C07F (RAM/port).
if (args.Contains("--check-game-over-play"))
{
    byte[] rom = File.ReadAllBytes(Path.Combine(FindDir("ram"), "..", "wboy2u_maincpu.bin"));
    string dir = FindDir("ram_fin_juego");
    byte[] Dump(int f) => File.ReadAllBytes(Path.Combine(dir, $"frame_{f:D6}.bin"));
    int last = Directory.GetFiles(dir, "frame_*.bin").Length;
    var joyAt = new Dictionary<int, byte>();
    foreach (var line in File.ReadLines(Path.Combine(dir, "inputs.csv")).Skip(1))
    {
        var v = line.Split(',').Select(int.Parse).ToArray(); // frame,right,left,b1,b2
        joyAt[v[0]] = (byte)((v[2] != 0 ? 0x80 : 0) | (v[1] != 0 ? 0x40 : 0) | (v[4] != 0 ? 0x04 : 0) | (v[3] != 0 ? 0x02 : 0));
    }
    int[] addrs = { 0x07E, 0x07F, 0x440, 0x443, 0x450, 0x479, 0x313, 0x85F, 0x850, 0x851, 0x852, 0x853,
                    0x016, 0x302, 0x3DF, 0x032, 0x854, 0x840, 0x84A, 0x84C };
    var game = TitleRom.FromRam(rom, File.ReadAllBytes(Path.Combine(FindDir("ram_auto_r2"), "frame_031034.bin")), play: true);
    var first = new Dictionary<int, string>();
    var seqRam = new List<string>(); var seqPort = new List<string>();
    int frames = 0, bad = 0, stopFrame = -1, firstBad = -1, ramKey = -1, portKey = -1;
    for (int f = 1; f <= last; f++)
    {
        var cur = Dump(f);
        game.Joystick = joyAt.GetValueOrDefault(f - 1);
        game.Inputs = 0;
        game.Step();
        frames++;
        int rk = cur[0x07E] << 8 | cur[0x07F], pk = game[0xC07E] << 8 | game[0xC07F];
        if (rk != ramKey) { seqRam.Add($"f{f}:{cur[0x07E]}/{cur[0x07F]}"); ramKey = rk; }
        if (pk != portKey) { seqPort.Add($"f{f}:{game[0xC07E]}/{game[0xC07F]}"); portKey = pk; }
        var wrong = addrs.Where(o => game[0xC000 + o] != cur[o]).ToArray();
        if (wrong.Length > 0) { bad++; if (firstBad < 0) firstBad = f; }
        foreach (int o in wrong)
            if (!first.ContainsKey(o))
                first[o] = $"f{f} RAM {cur[o]:X2} port {game[0xC000 + o]:X2} " +
                           $"($C07E {cur[0x07E]}/{game[0xC07E]} $C07F {cur[0x07F]}/{game[0xC07F]} " +
                           $"$C081 ${cur[0x081] | cur[0x082] << 8:X4}/${game[0xC081] | game[0xC082] << 8:X4})";
        if (game.Unported != null) { stopFrame = f; break; }
    }
    foreach (int o in addrs)
        Console.WriteLine($"${0xC000 + o:X4}: " + (first.TryGetValue(o, out var s) ? s : "ok"));
    Console.WriteLine("estados RAM  ($C07E/$C07F): " + string.Join(" ", seqRam));
    Console.WriteLine("estados port ($C07E/$C07F): " + string.Join(" ", seqPort));
    Console.WriteLine("primera divergencia: " + (firstBad < 0 ? "ninguna" : $"f{firstBad}"));
    Console.WriteLine("sin portar: " + (game.Unported ?? "ninguno") +
                      (stopFrame >= 0 ? $" en f{stopFrame}" : "") +
                      $"; $C07E {game[0xC07E]}, $C07F {game[0xC07F]}, $C081 ${game[0xC081] | game[0xC082] << 8:X4}");
    Console.WriteLine($"TOTAL: fotogramas {frames}/{last}, con diferencias {bad}, direcciones distintas {first.Count}");
    return;
}

// --check-round2-move-full: misma semilla, entradas y Step() que --check-round2-move, pero compara toda la
// RAM $C000-$CFFF por fotograma. Agrupa las diferencias en bloques de $10 y, por bloque, informa el primer
// fotograma, hasta 3 bytes de muestra (port/RAM) de ese fotograma, los fotogramas con diferencias y los
// manejadores activos ($C07E/$C07F de la RAM de referencia) en los que difiere. Solo informa, no corrige.
if (args.Contains("--check-round2-move-full"))
{
    byte[] rom = File.ReadAllBytes(Path.Combine(FindDir("ram"), "..", "wboy2u_maincpu.bin"));
    string dir = FindDir("ram_round2_move");
    byte[] Dump(int f) => File.ReadAllBytes(Path.Combine(dir, $"frame_{f:D6}.bin"));
    int last = Directory.GetFiles(dir, "frame_*.bin").Length;
    var joyAt = new Dictionary<int, byte>();
    foreach (var line in File.ReadLines(Path.Combine(dir, "inputs.csv")).Skip(1))
    {
        var v = line.Split(',').Select(int.Parse).ToArray(); // frame,right,left,b1,b2
        joyAt[v[0]] = (byte)((v[2] != 0 ? 0x80 : 0) | (v[1] != 0 ? 0x40 : 0) | (v[4] != 0 ? 0x04 : 0) | (v[3] != 0 ? 0x02 : 0));
    }
    var game = TitleRom.FromRam(rom, File.ReadAllBytes(Path.Combine(FindDir("ram_auto_r2"), "frame_031034.bin")), play: true);
    var firstAt = new SortedDictionary<int, string>();               // bloque -> primer fotograma y muestra
    var badFrames = new Dictionary<int, int>();                      // bloque -> fotogramas con diferencias
    var badAddrs = new Dictionary<int, HashSet<int>>();              // bloque -> direcciones distintas
    var handlers = new Dictionary<int, SortedDictionary<string, int>>(); // bloque -> manejador -> fotogramas
    int frames = 0, stopFrame = -1;
    for (int f = 1; f <= last; f++)
    {
        var cur = Dump(f);
        game.Joystick = joyAt.GetValueOrDefault(f - 1);
        game.Inputs = 0;
        game.Step();
        frames++;
        string h = $"{cur[0x07E]}/{cur[0x07F]}";
        for (int blk = 0; blk < 0x1000; blk += 0x10)
        {
            var wrong = Enumerable.Range(blk, 0x10).Where(o => game[0xC000 + o] != cur[o]).ToArray();
            if (wrong.Length == 0) continue;
            badFrames[blk] = badFrames.GetValueOrDefault(blk) + 1;
            if (!badAddrs.TryGetValue(blk, out var set)) badAddrs[blk] = set = new HashSet<int>();
            set.UnionWith(wrong);
            if (!handlers.TryGetValue(blk, out var hs)) handlers[blk] = hs = new SortedDictionary<string, int>();
            hs[h] = hs.GetValueOrDefault(h) + 1;
            if (!firstAt.ContainsKey(blk))
                firstAt[blk] = $"f{f} ($C07E/$C07F {h}, port {game[0xC07E]}/{game[0xC07F]}) muestra " +
                               string.Join(" ", wrong.Take(3).Select(o => $"${0xC000 + o:X4}={game[0xC000 + o]:X2}/{cur[o]:X2}"));
        }
        if (game.Unported != null) { stopFrame = f; break; }
    }
    foreach (var (blk, s) in firstAt)
        Console.WriteLine($"${0xC000 + blk:X4}-${0xC00F + blk:X4}: {s}; fotogramas {badFrames[blk]}, " +
                          $"bytes {badAddrs[blk].Count} (" +
                          string.Join(" ", badAddrs[blk].Order().Select(o => $"${0xC000 + o:X4}")) + "); manejadores " +
                          string.Join(" ", handlers[blk].Select(kv => $"{kv.Key}x{kv.Value}")));
    Console.WriteLine("sin portar: " + (game.Unported ?? "ninguno") + (stopFrame >= 0 ? $" en f{stopFrame}" : ""));
    Console.WriteLine($"TOTAL: fotogramas {frames}/{last}, bloques distintos {firstAt.Count}, " +
                      $"bytes distintos {badAddrs.Values.Sum(x => x.Count)}");
    return;
}

// --check-fall: caida fuera de pantalla de ram_manual (fotogramas 3841..3872). Mismas entradas que
// --check-player ($C086 de N+1, $C084/$C085 recalculados) y comparacion con N+1 de estado ($C85F),
// Y ($C852/$C853), X ($C850/$C851) y $C400. No se filtra por $C477 ni por manejador: se informa.
if (args.Contains("--check-fall"))
{
    byte[] rom = File.ReadAllBytes(Path.Combine(FindDir("ram"), "..", "wboy2u_maincpu.bin"));
    string dir = FindDir("ram_manual_old");
    int[] outs = { 0x85F, 0x852, 0x853, 0x850, 0x851, 0x400 };
    int pairs = 0, bad = 0, wrongBytes = 0;
    var missing = new Dictionary<int, int>();
    byte[]? last = null;
    for (int f = 3841; f < 3872; f++)
    {
        var prev = File.ReadAllBytes(Path.Combine(dir, $"frame_{f:D6}.bin"));
        var cur = File.ReadAllBytes(Path.Combine(dir, $"frame_{f + 1:D6}.bin"));
        var mem = new byte[0x10000];
        Array.Copy(rom, mem, rom.Length);
        Array.Copy(prev, 0, mem, 0xC000, 0x1000);
        mem[0xC086] = cur[0x086];
        PlayerRoutine.ReadInputs(mem);
        int miss = PlayerRoutine.Update(mem);
        if (miss != 0) missing[miss] = missing.GetValueOrDefault(miss) + 1;
        pairs++;
        var wrong = outs.Where(o => mem[0xC000 + o] != cur[o]).ToArray();
        if (wrong.Length > 0) { bad++; wrongBytes += wrong.Length; }
        int handler = cur[0x081] | cur[0x082] << 8;
        int yRam = cur[0x852] | cur[0x853] << 8, yPort = mem[0xC852] | mem[0xC853] << 8;
        int xRam = cur[0x850] | cur[0x851] << 8, xPort = mem[0xC850] | mem[0xC851] << 8;
        Console.WriteLine($"{f}->{f + 1} [$C081 {handler:X4} $C477 {prev[0x477]:X2}/{cur[0x477]:X2}{(miss != 0 ? $" sin portar ${miss:X4}" : "")}] " +
                          $"$1F esperado {cur[0x85F]:X2} port {mem[0xC85F]:X2} | Y {yRam}/{yPort} | X {xRam}/{xPort} | $C400 {cur[0x400]:X2}/{mem[0xC400]:X2}" +
                          (wrong.Length > 0 ? "  DIF " + string.Join(" ", wrong.Select(o => $"${0xC000 + o:X4}")) : "  ok"));
        if (f + 1 == 3872) last = mem;
    }
    Console.WriteLine("sin portar: " + (missing.Count == 0 ? "ninguno" : string.Join(", ", missing.Select(kv => $"${kv.Key:X4}x{kv.Value}"))));
    Console.WriteLine($"TOTAL: pares {pairs}, pares con diferencias {bad}, bytes distintos {wrongBytes}");
    if (last != null)
        Console.WriteLine($"3872 en el port: estado ${last[0xC85F]:X2}, Y={last[0xC852] | last[0xC853] << 8}");
    return;
}

// Ventana base del port a 60 FPS. Aún sin lógica del juego.
const int ScreenWidth = 256 * 3;
const int ScreenHeight = 224 * 3;

Raylib.InitWindow(ScreenWidth, ScreenHeight, "Wonder Boy (Sega System 1) - port");
GameWindow.SetIcon();
Raylib.SetTargetFPS(60);

while (!Raylib.WindowShouldClose())
{
    Raylib.BeginDrawing();
    Raylib.ClearBackground(Color.Black);
    Raylib.DrawText("Wonder Boy port - base", 20, 20, 20, Color.White);
    Raylib.EndDrawing();
}

Raylib.CloseWindow();

// Volcados: offset = direccion - $C000. El volcado N es la RAM tras el fotograma N.
// El jugador se semilla del volcado first-1; despues todo su estado (suelo/aire, Y, X,
// velocidades) lo calcula el port. Del volcado N-1 solo se toman entradas ajenas al
// jugador: camara $C442 y $C444, monopatin $C408. Terreno: ROM + punteros $C44A/$C44C.
// Botones (provisional, en la captura van siempre juntos): BUTTON1 -> $C086 bit1
// (correr/atacar), BUTTON2 -> $C086 bit2 (salto).
static void Check((int first, int last)[] windows, Func<Player, byte[], int, string?> compare,
                  string sub = "ram_play", string prefix = "frame_")
{
    string dir = FindDir(sub);
    var rows = new Dictionary<int, string[]>();
    foreach (var line in File.ReadLines(Path.Combine(dir, "inputs.csv")).Skip(1))
    {
        var c = line.Split(',');
        rows[int.Parse(c[0])] = c;
    }
    byte[] Dump(int f) => File.ReadAllBytes(Path.Combine(dir, $"{prefix}{f:D6}.bin"));
    byte[] rom = File.ReadAllBytes(Path.Combine(dir, "..", "wboy2u_maincpu.bin"));
    ushort W(byte[] d, int o) => (ushort)(d[o] | d[o + 1] << 8);

    int compared = 0, matches = 0;
    var diffs = new List<string>();
    foreach (var (first, last) in windows)
    {
        var seed = Dump(first - 1);
        var p = new Player
        {
            X = W(seed, 0x850), VelX = (sbyte)seed[0x854], XFrac = seed[0x855],
            BaseSpeed = seed[0x858], FacingLeft = (seed[0x85E] & 0x10) != 0,
            Y = W(seed, 0x852), YFrac = seed[0x856], VelY = (sbyte)seed[0x857],
            State = seed[0x85F], Height = seed[0x848],
            JumpLatch = (seed[0x084] & 0x20) != 0, JumpHeld = (seed[0x085] & 0x01) != 0,
            Terrain = new Terrain(rom, W(seed, 0x44A), W(seed, 0x44C)),
        };
        var prev = seed;
        for (int f = first; f <= last; f++)
        {
            var r = rows[f];
            bool left = r[3] == "1", right = r[4] == "1";
            bool run = r[7] == "1", jump = r[8] == "1";
            p.ReadJumpButton(jump);
            p.Step(left, right, run, W(prev, 0x442), W(prev, 0x444), skateboard: prev[0x408] != 0);
            var d = Dump(f);
            compared++;
            var diff = compare(p, d, f);
            if (diff == null) matches++;
            else if (diffs.Count < 5) diffs.Add($"  f{f}: {diff}");
            prev = d;
        }
    }
    Console.WriteLine($"Fotogramas comparados: {compared}");
    Console.WriteLine($"Coincidencias: {matches}");
    foreach (var s in diffs) Console.WriteLine(s);
}

static string FindRoot()
{
    for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        if (File.Exists(Path.Combine(d.FullName, "Mame", "roms", "wboy2u.zip"))) return d.FullName;
    throw new DirectoryNotFoundException("Mame/roms/wboy2u.zip no encontrado");
}

static string FindDir(string sub)
{
    for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
    {
        var p = Path.Combine(d.FullName, "docs", "analysis", sub);
        if (Directory.Exists(p)) return p;
    }
    throw new DirectoryNotFoundException($"docs/analysis/{sub} no encontrado");
}
