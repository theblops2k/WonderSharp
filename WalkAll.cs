using WonderBoy.Game;

namespace WonderBoy;

// --walk-all [--only N] [--frames F]: sin ventana. Recorre las 32 rondas (area N>>2, ronda N&3) como
// --round-load N (moneda f120-124, START f180-184, $C301=N al ver $C07E=5) y, con $C07E=7, ayudas
// SOLO de esta prueba antes de cada Step() (no tocan la logica del port):
//   - invencible: $C85E bit2 (golpe) a 0 y estado $C85F 1 (muerte) -> $84 (cae).
//   - tiempo ilimitado: vitalidad $C469 = $18FF (hasta la meta, $C440 = 0); vidas $C302 >= 3.
//   - vuela: Y del jugador ($C852) = suelo maximo de X-$0A..X+$40 ($2660) + $40 (o mas, si queda
//     por debajo del borde inferior $C444-$100+$40); joystick derecha.
//   - X <= $C442 + $60 (no sale por la derecha con la camara parada); con $C479 >= 2 (meta o rey
//     puestos, $2C39) vuela bajo (suelo + $18) para tocarlos; con un objetivo (Target: rey $2C, su
//     cabeza $2D por recoger o la plataforma final) va a su X e Y (al rey, $50 a su derecha y mirandole).
//   - hacha: $C407 bit1 y boton 1 a rafagas (8 fotogramas si, 8 no), para poder derribar al rey.
//   - atasco (X sin avanzar 900 fotogramas): con $C479 != 0 (zona final/jefe) se vacian las ranuras de
//     enemigo para que salga la meta ($2C39); si sigue atascado, se pasa a la siguiente ronda.
// Fin de ronda: tras $C440 != 0 (meta), cuando $C07E deja de ser 7 (siguiente ronda/fin de juego);
// despues, sin ayudas, hasta TailFrames fotogramas de transicion (otra vez $C07E=7 o titulo).
// Si una ranura de enemigo llega a una rutina sin portar ($2D9B), se anota, se vacia la ranura y se
// sigue (asi salen varios valores en una ronda); otra rutina sin portar detiene la ronda. Cada valor
// se anota en Wonderboy_errors.log (ErrorLog) y la lista sale al final por consola.
static class WalkAll
{
    const int MaxFrames = 40000;     // por ronda (~11 min de juego)
    const int StuckFrames = 900;     // 15 s sin avanzar en X
    const int TailFrames = 3000;     // tras la meta: transicion a la siguiente ronda o fin de partida
    static int every = 600;         // --verbose: cada cuantos fotogramas se imprime el estado (--every)

    public static void Run(byte[] mainRom, string[] args)
    {
        int oi = Array.IndexOf(args, "--only");
        int first = oi >= 0 && oi + 1 < args.Length ? int.Parse(args[oi + 1]) : 0;
        int last = oi >= 0 && oi + 1 < args.Length ? first : 31;
        int fi = Array.IndexOf(args, "--frames");
        int maxFrames = fi >= 0 && fi + 1 < args.Length ? int.Parse(args[fi + 1]) : MaxFrames;
        bool verbose = args.Contains("--verbose");
        int ei = Array.IndexOf(args, "--every");
        every = ei >= 0 && ei + 1 < args.Length ? int.Parse(args[ei + 1]) : 600;

        var found = new SortedDictionary<string, SortedSet<int>>();   // valor -> rondas
        int completed = 0;
        for (int n = first; n <= last; n++)
        {
            var (result, values) = Round(mainRom, n, maxFrames, verbose);
            if (result.StartsWith("completada")) completed++;
            foreach (var v in values)
            {
                if (!found.TryGetValue(v, out var set)) found[v] = set = new SortedSet<int>();
                set.Add(n);
            }
            Console.WriteLine($"ronda {n:D2} (area {(n >> 2) + 1}-{(n & 3) + 1}): {result}" +
                              (values.Count > 0 ? "  sin portar: " + string.Join(", ", values) : ""));
        }
        Console.WriteLine($"rondas recorridas {last - first + 1}, completadas {completed}");
        Console.WriteLine("valores sin portar: " + (found.Count == 0 ? "ninguno" :
            string.Join(", ", found.Select(kv => $"{kv.Key} (rondas {string.Join(" ", kv.Value.Select(r => $"{(r >> 2) + 1}-{(r & 3) + 1}"))})"))));
        Console.WriteLine("registro: " + ErrorLog.FilePath);
    }

    static (string result, List<string> values) Round(byte[] mainRom, int n, int maxFrames, bool verbose)
    {
        var values = new List<string>();
        var game = TitleRom.StartPlay(mainRom, 29);
        var log = new ErrorLog($"walk-all ronda {n}", game);
        bool written = false, goal = false, cleared = false;
        int bestX = -1, bestAt = 0, inLevelAt = -1;
        int f = 29;
        void Report(string v, string why)
        {
            if (!values.Contains(v))
            {
                values.Add(v);
                Console.WriteLine($"  Area {(n >> 2) + 1}, Ronda {(n & 3) + 1}: encontrado {v} en f{f} ({why})");
            }
            log.Write($"walk-all ronda {n} (area {(n >> 2) + 1}-{(n & 3) + 1}): {why}", null, f);
        }
        try
        {
            for (; f < 29 + maxFrames; f++)
            {
                bool play = written && game[0xC07E] == 7;
                if (play)
                {
                    if (inLevelAt < 0) inLevelAt = f;
                    Assist(game);
                }
                // derecha + hacha a rafagas; con un objetivo de fin de area (Target) va hacia el.
                int dir = 0x40;
                var t = Target(game);
                if (t != null && X(game) + 4 > t.Value.x) dir = 0x80;
                game.Joystick = (byte)(play ? dir | ((f & 0x08) != 0 ? 0x02 : 0) : 0);
                game.Inputs = (byte)((f >= 120 && f < 125 ? 0x01 : 0) | (f >= 180 && f < 185 ? 0x10 : 0));
                game.Step();
                log.Check(f);
                if (game.Unported != null)
                {
                    string v = game.Unported;
                    int slot = EnemySlotWith(game, v);
                    if (slot != 0)
                    {
                        int h = Enemies.HandlerOf(game.CopyMemory(), game[slot + 0x18]);
                        if (verbose) Console.WriteLine($"  f{f} {v} manejador ${h:X4} tipo ${game[slot + 0x18]:X2} estado $1F={game[slot + 0x1F]:X2}");
                        Report(v, $"rutina sin portar {v} en la ranura de enemigo ${slot:X4} (tipo ${game[slot + 0x18]:X2}," +
                                  $" manejador ${h:X4}); se vacia y se sigue");
                        game.Poke(slot + 0x18, 0); game.Poke(slot + 0x1F, 0);
                        game.ResumeAfterUnported();
                        continue;
                    }
                    Report(v, $"rutina sin portar {v}; la ronda se detiene");
                    return ($"detenida en f{f} (X=${X(game):X4})", values);
                }
                if (!written && game[0xC07E] == 5) { game.Poke(0xC301, (byte)n); written = true; }
                if (!written) continue;
                if (game[0xC440] != 0) goal = true;
                if (goal && game[0xC07E] != 7 && game[0xC07E] != 8)
                {
                    // Transicion: hasta TailFrames mas, hasta que vuelva a jugarse ($C07E=7, siguiente ronda)
                    // o se salga del modo juego (fin de partida -> titulo). Sin ayudas; boton 1 a rafagas
                    // (para la entrada de nombre de records, $1C24).
                    int end = f;
                    string tail = "siguiente ronda sin llegar a jugarse";
                    for (f++; f <= end + TailFrames; f++)
                    {
                        game.Joystick = (byte)((f & 0x08) != 0 ? 0x02 : 0); game.Inputs = 0;
                        game.Step();
                        log.Check(f);
                        if (game.Unported != null)
                        {
                            Report(game.Unported, $"rutina sin portar {game.Unported} tras la meta; se detiene");
                            return ($"completada en f{end}; detenida despues en f{f}", values);
                        }
                        if (game[0xC07E] == 7) { tail = $"siguiente ronda jugandose en f{f}"; break; }
                        if (!game.GameMode) { tail = $"fin de partida, titulo en f{f}"; break; }
                    }
                    return ($"completada en f{end} (meta, $C07E={game[0xC07E]}; {tail})", values);
                }
                if (!play) continue;
                int x = X(game);
                if (x > bestX) { bestX = x; bestAt = f; }
                if (verbose && (f - inLevelAt) % every == 0)
                    Console.WriteLine($"  f{f} X=${x:X4} Y=${game[0xC852] | game[0xC853] << 8:X4} cam=${game[0xC442] | game[0xC443] << 8:X4}" +
                                      $" $C444=${game[0xC444] | game[0xC445] << 8:X4} $C85F={game[0xC85F]:X2} $C479={game[0xC479]} $C440={game[0xC440]}" +
                                      $" $C07F={game[0xC07F]} $C313={game[0xC313]} $C85E={game[0xC85E]:X2} $C477={game[0xC477]} $C400={game[0xC400]}" +
                                      " ranuras " + string.Join(" ", Enumerable.Range(0, 12).Select(i => $"{game[0xC8B8 + i * 0x20]:X2}/{game[0xC8BF + i * 0x20]:X2}")) +
                                      " armas " + string.Join(" ", new[] { 0xC860, 0xC880 }.Select(a => $"{game[a + 0x1F]:X2}@{game[a + 0x10] | game[a + 0x11] << 8:X4},{game[a + 0x12] | game[a + 0x13] << 8:X4}")) +
                                      $" rey@{KingX(game):X4} cabeza@{game[0xC910] | game[0xC911] << 8:X4},{game[0xC912] | game[0xC913] << 8:X4}" +
                                      $" C960@{game[0xC970] | game[0xC971] << 8:X4},{game[0xC972] | game[0xC973] << 8:X4} 1E={game[0xC97E]:X2} 19={game[0xC979]:X2}");
                if (goal || f - bestAt < StuckFrames) continue;
                if (game[0xC479] != 0 && !cleared)
                {
                    for (int i = 0; i < 12; i++) { game.Poke(0xC8A0 + i * 0x20 + 0x18, 0); game.Poke(0xC8A0 + i * 0x20 + 0x1F, 0); }
                    cleared = true;
                    bestAt = f;
                    continue;
                }
                Console.WriteLine($"  Area {(n >> 2) + 1}, Ronda {(n & 3) + 1}: atasco en X=${x:X4} f{f} ($C479={game[0xC479]})");
                log.Write($"walk-all ronda {n} (area {(n >> 2) + 1}-{(n & 3) + 1}): atasco, X=${x:X4} sin avanzar {StuckFrames} fotogramas", null, f);
                return ($"atascada en X=${x:X4} f{f} ($C479={game[0xC479]})", values);
            }
        }
        catch (Exception ex)
        {
            string v = $"excepcion {ex.GetType().Name}";
            if (!values.Contains(v)) values.Add(v);
            Console.WriteLine($"  Area {(n >> 2) + 1}, Ronda {(n & 3) + 1}: encontrado {v} en f{f} ({ex.Message})");
            log.Write($"walk-all ronda {n} (area {(n >> 2) + 1}-{(n & 3) + 1}): excepcion", ex, f);
            return ($"excepcion en f{f}: {ex.Message}", values);
        }
        return ($"sin terminar tras {maxFrames} fotogramas (X=${X(game):X4}, $C07E={game[0xC07E]})", values);
    }

    static int X(TitleRom g) => g[0xC850] | g[0xC851] << 8;

    // X del rey (ranura de enemigo con tipo $2C y estado != 0), o -1.
    static int KingX(TitleRom g)
    {
        for (int i = 0; i < 12; i++)
        {
            int ix = 0xC8A0 + i * 0x20;
            if (g[ix + 0x18] == 0x2C && g[ix + 0x1F] != 0) return g[ix + 0x10] | g[ix + 0x11] << 8;
        }
        return -1;
    }

    // Objetivo de fin de area (posicion a la que va el jugador), o null:
    //   - rey ($2C) ya en el suelo ($C8DF >= 3): $50 a su derecha, a la altura de su cabeza ($30E1).
    //   - cabeza del rey ($2D) en $30C7 (estado 5, hay que tocarla).
    //   - con $C479 >= 3 (cabeza recogida): la plataforma $3A/$3E ($3FF4: subirse pone $C440 = 1); land =
    //     true: encima de ella se deja caer (sin fijar Y) para posarse.
    // Lejos en X (> $10) se va $30 por encima del objetivo (para no posarse antes en una plataforma).
    static (int x, int y, bool land)? Target(TitleRom g)
    {
        int px = X(g);
        (int x, int y, bool land) Near(int x, int y, bool land) =>
            Math.Abs(px - x) > 0x10 ? (x, y + 0x30, false) : (x, y, land);
        for (int i = 0; i < 12; i++)
        {
            int ix = 0xC8A0 + i * 0x20;
            int t = g[ix + 0x18], s = g[ix + 0x1F];
            int x = g[ix + 0x10] | g[ix + 0x11] << 8, y = g[ix + 0x12] | g[ix + 0x13] << 8;
            if (t == 0x2D && (s & 0x0F) == 0x05) return Near(x, y, false);
            if (g[0xC479] >= 3 && (t == 0x3A || t == 0x3E) && s >= 0x03) return Near(x, y + 0x10, true);
        }
        int k = KingX(g);
        if (k >= 0 && g[0xC8DF] >= 3) return ((k + 0x50) & 0xFFFF, (g[0xC8D2] | g[0xC8D3] << 8) + 0x1C, false);
        return null;
    }
    static int W(byte[] m, int a) => m[a] | m[a + 1] << 8;

    // Ayudas de la prueba sobre la RAM antes del Step(): invencible, vitalidad/vidas y vuelo.
    static void Assist(TitleRom g)
    {
        g.Poke(0xC85E, (byte)(g[0xC85E] & 0xFB));                    // sin golpe ($289F/$29F3/$2A33/$2AEF)
        if ((g[0xC85F] & 0x0F) == 1) g.Poke(0xC85F, 0x84);           // sin muerte: cae (vuelo)
        if (g[0xC302] < 3) g.Poke(0xC302, 3);
        g.Poke(0xC407, (byte)(g[0xC407] | 0x02));                    // con hacha ($2B4F)
        if (g[0xC440] != 0) return;                                 // meta: la bonificacion vacia la vitalidad
        g.Poke(0xC469, 0xFF); g.Poke(0xC46A, 0x18);                  // vitalidad $18FF
        if ((g[0xC85F] & 0x0F) == 0) return;                         // jugador inactivo
        var m = g.CopyMemory();
        int x = W(m, 0xC850), floor = 0;
        int right = (W(m, 0xC442) + 0x60) & 0xFFFF;                  // con la camara parada (rey), sin salir por la derecha
        if (x > right) { x = right; g.Poke(0xC850, (byte)x); g.Poke(0xC851, (byte)(x >> 8)); }
        for (int dx = -0x0A; dx <= 0x40; dx += 8)
            floor = Math.Max(floor, Enemies.Terrain2660(m, (x + dx) & 0xFFFF));
        int y = floor + (m[0xC479] >= 2 ? 0x18 : 0x40);              // meta puesta ($2C39): vuelo bajo para tocarla
        int bottom = (W(m, 0xC444) - 0x100 + 0x40) & 0xFFFF;
        if (y < bottom) y = bottom;
        var t = Target(g);                                           // rey / cabeza / tipo $28 / plataforma
        if (t != null && t.Value.land) return;                       // encima de la plataforma: cae y se posa
        if (t != null && t.Value.y > bottom) y = t.Value.y & 0xFFFF;
        g.Poke(0xC852, (byte)y); g.Poke(0xC853, (byte)(y >> 8));
        if ((g[0xC85F] & 0x0F) == 2) g.Poke(0xC85F, 0x84);           // del suelo al aire
    }

    // Ranura de enemigo cuyo $2D9B (sobre una copia de la RAM) llega al mismo valor sin portar; 0 si no.
    static int EnemySlotWith(TitleRom g, string v)
    {
        if (v.Length != 5 || v[0] != '$') return 0;
        int miss = Convert.ToInt32(v[1..], 16);
        for (int i = 0; i < 12; i++)
        {
            int ix = 0xC8A0 + i * 0x20;
            int t = g[ix + 0x18];
            if (t == 0 || (t & 0x80) != 0) continue;
            var m = g.CopyMemory();
            if (Enemies.Dispatch(m, ix) == miss) return ix;
        }
        return 0;
    }
}
