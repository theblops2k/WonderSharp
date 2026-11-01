namespace WonderBoy.Audio;

// --check-sound: comprobaciones en consola (sin ventana ni dispositivo de audio).
//   Notas: por cada codigo, SoundProgram desde el arranque (4 IRQ por fotograma), los bytes a los
//   chips pasan por ChipNotes y se comparan avance a avance con SoundScore (lectura independiente de
//   las tablas). Lista de notas = tramos de periodo constante con volumen > 0 por voz.
//   Ordenes ($00, $02, $F6/$F7): efecto sobre una musica en curso.
//   Generador: frecuencia y nivel medidos sobre las muestras de ToneGenerator.
public static class SoundCheck
{
    const double TickSeconds = 2.0 / 240.0;   // un avance cada 2 IRQ; 4 IRQ por fotograma de ~60 Hz

    record Note(int Tick, int Chip, int Voice, int Period, int Ticks, int MaxVol, int NoiseCtrl, int LastVol);

    sealed class Run
    {
        public readonly SoundProgram Prog;
        public readonly ChipNotes Chips = new();
        public Run(byte[] rom)
        {
            Prog = new SoundProgram(rom);
            Prog.ChipWrite = Chips.Write;
            for (int i = 0; i < 4; i++) Prog.Irq();           // fases 1,2,3,0 tras el arranque
        }
        // Envia el codigo y deja el programa justo tras la IRQ de ordenes que lo procesa.
        public void Send(byte code) { Prog.Nmi(code); Prog.Irq(); Prog.Irq(); }
        // Un avance (fase impar) y la IRQ siguiente, que vuelca sus registros a los chips.
        public void Tick() { Prog.Irq(); Prog.Irq(); }
        public bool AnyChannel => Enumerable.Range(0x8021, 6).Any(a => Prog[a] != 0);
    }

    public static int CheckAll(byte[] rom, IReadOnlyCollection<int> codes, int notesFor)
    {
        int bad = 0;
        Console.WriteLine($"codigos de las grabaciones ({codes.Count}): " + string.Join(" ", codes.Select(c => $"{c:X2}")));
        foreach (int code in codes)
        {
            if ((code & 0xF0) == 0xF0 || (code & 0xC0) != 0xC0 && (code & 0x3F) < 0x08) continue; // ordenes: abajo
            bad += Sound(rom, code, code == notesFor) ? 0 : 1;
        }
        bad += Orders(rom);
        bad += Generator();
        Console.WriteLine($"TOTAL: comprobaciones con fallo {bad}");
        return bad;
    }

    static bool Sound(byte[] rom, int code, bool full)
    {
        bool music = (code & 0xC0) == 0xC0;
        var run = new Run(rom);
        run.Send((byte)code);
        var score = new SoundScore(rom, code);
        int maxTicks = music ? 7200 : 2400;                   // 60 s / 20 s
        int ticks = 0, wrong = 0, end = -1;
        string first = "";
        var notes = new List<Note>();
        var open = new Note?[8];
        for (; ticks < maxTicks; ticks++)
        {
            run.Tick();
            score.Step();
            var (ep, ea, en) = score.Output();
            var c = run.Chips;
            string diff = "";
            for (int chip = 0; chip < 2; chip++)
            {
                for (int v = 0; v < 3; v++)
                {
                    if (c.Period[chip, v] != ep[chip, v]) diff += $" {Chip(chip)} t{v} periodo {c.Period[chip, v]:X3}/{ep[chip, v]:X3}";
                    if (c.Atten[chip, v] != ea[chip, v]) diff += $" {Chip(chip)} t{v} aten {c.Atten[chip, v]}/{ea[chip, v]}";
                }
                if (c.Atten[chip, 3] != ea[chip, 3]) diff += $" {Chip(chip)} ruido aten {c.Atten[chip, 3]}/{ea[chip, 3]}";
                if (c.NoiseCtrl[chip] != en[chip]) diff += $" {Chip(chip)} ruido ctrl {c.NoiseCtrl[chip]}/{en[chip]}";
            }
            if (diff != "") { if (wrong++ == 0) first = $"avance {ticks}:{diff}"; }
            Track(c, ticks, open, notes);
            if (!run.AnyChannel && !score.Playing) { end = ticks; ticks++; break; }
        }
        for (int i = 0; i < 8; i++) if (open[i] is Note o) notes.Add(o);
        notes.Sort((a, b) => a.Tick != b.Tick ? a.Tick.CompareTo(b.Tick) : (a.Chip * 4 + a.Voice).CompareTo(b.Chip * 4 + b.Voice));

        string kind = music ? "musica" : "efecto";
        string len = end >= 0 ? $"dura {(end + 1) * TickSeconds:F2} s" : $"sigue sonando a los {maxTicks * TickSeconds:F0} s (bucle)";
        Console.WriteLine($"{code:X2} {kind}: {len}, {notes.Count} notas, avances iguales {ticks - wrong}/{ticks}" +
                          (wrong > 0 ? $"  primera diferencia (port/esperado) {first}" : ""));
        foreach (var g in notes.GroupBy(n => n.Chip * 4 + n.Voice).OrderBy(g => g.Key))
        {
            var list = g.ToList();
            int show = full ? list.Count : Math.Min(list.Count, 10);
            string s = string.Join(" ", list.Take(show).Select(n => $"{Name(n, false)}x{n.Ticks}"));
            Console.WriteLine($"   {Voice(g.Key)}: {s}{(list.Count > show ? $" ... (+{list.Count - show})" : "")}");
        }
        if (full)
            foreach (var n in notes)
                Console.WriteLine($"   t={n.Tick * TickSeconds,7:F3} s  {Voice(n.Chip * 4 + n.Voice),-10} {Name(n, true),-12} {Hz(n),9:F1} Hz  {n.Ticks * TickSeconds:F3} s  vol {n.MaxVol}");
        return wrong == 0;
    }

    // Nota = tramo con volumen > 0 que empieza tras silencio, al subir el volumen 3 pasos o mas
    // (nuevo ataque) o al alejarse el periodo mas de un 3% del inicial (el vibrato no corta la nota).
    static void Track(ChipNotes c, int tick, Note?[] open, List<Note> notes)
    {
        for (int chip = 0; chip < 2; chip++)
            for (int v = 0; v < 4; v++)
            {
                int i = chip * 4 + v, vol = 15 - c.Atten[chip, v];
                int period = v < 3 ? c.Period[chip, v] : (c.NoiseCtrl[chip] & 3) == 3 ? c.Period[chip, 2] : 0;
                int ctrl = v < 3 ? -1 : c.NoiseCtrl[chip];
                var o = open[i];
                if (vol == 0) { if (o != null) { notes.Add(o); open[i] = null; } continue; }
                bool same = o != null && o.NoiseCtrl == ctrl && vol < o.LastVol + 3 &&
                            Math.Abs(period - o.Period) <= Math.Max(o.Period, 1) * 0.03;
                if (same)
                    open[i] = o! with { Ticks = o.Ticks + 1, MaxVol = Math.Max(o.MaxVol, vol), LastVol = vol };
                else
                {
                    if (o != null) notes.Add(o);
                    open[i] = new Note(tick, chip, v, period, 1, vol, ctrl, vol);
                }
            }
    }

    static string Chip(int chip) => chip == SoundProgram.Sn1 ? "sn1" : "sn2";
    static string Voice(int i) => $"{Chip(i / 4)} {(i % 4 == 3 ? "ruido" : $"tono{i % 4}")}";

    static double Hz(Note n)
    {
        double clk = ChipNotes.Clock[n.Chip];
        if (n.Voice < 3 || (n.NoiseCtrl & 3) == 3) return clk / (32.0 * (n.Period == 0 ? 1024 : n.Period));
        return clk / (512 << (n.NoiseCtrl & 3));
    }

    static string Name(Note n, bool cents)
    {
        if (n.Voice == 3) return ((n.NoiseCtrl & 4) != 0 ? "blanco" : "periodico") + ((n.NoiseCtrl & 3) == 3 ? $"@{ChipNotes.NoteName(Hz(n), cents)}" : $"/{n.NoiseCtrl & 3}");
        double hz = Hz(n);
        return hz > 20000 ? "agudo" : ChipNotes.NoteName(hz, cents);
    }

    // Ordenes de las grabaciones sobre una musica en curso ($C2) o antes de ella.
    static int Orders(byte[] rom)
    {
        int bad = 0;
        bool Audible(Run r, int chip) => Enumerable.Range(0, 4).Any(v => r.Chips.Atten[chip, v] < 15);
        void Report(string what, bool ok) { Console.WriteLine($"orden {what}: {(ok ? "ok" : "FALLO")}"); if (!ok) bad++; }

        var r0 = new Run(rom);
        r0.Send(0xC2); for (int i = 0; i < 60; i++) r0.Tick();
        bool before = Audible(r0, SoundProgram.Sn1);
        r0.Send(0x00); r0.Tick(); r0.Tick();
        Report("00 (para todo) tras C2", before && !Audible(r0, 0) && !Audible(r0, 1) && !r0.AnyChannel);

        var r2 = new Run(rom);
        r2.Send(0xC2); for (int i = 0; i < 60; i++) r2.Tick();
        r2.Send(0x0B); r2.Tick();                             // efecto en sn2 + musica en sn1
        bool both = Audible(r2, 0) && Audible(r2, 1);
        r2.Send(0x02); r2.Tick(); r2.Tick();
        Report("02 (para canales 3-5, sn1) tras C2 + 0B", both && !Audible(r2, SoundProgram.Sn1) && r2.Prog[0x8021] == 1);

        var r6 = new Run(rom);
        r6.Prog.Nmi(0xF6); r6.Send(0xC2); for (int i = 0; i < 30; i++) r6.Tick();
        bool blocked = !Audible(r6, SoundProgram.Sn1);
        r6.Prog.Nmi(0xF7); r6.Send(0xC2); for (int i = 0; i < 30; i++) r6.Tick();
        Report("F6 bloquea / F7 desbloquea la musica", blocked && Audible(r6, SoundProgram.Sn1));
        return bad;
    }

    // ToneGenerator: frecuencia (flancos de subida en 1 s), nivel (2 dB por paso) y ruido periodico.
    static int Generator()
    {
        int bad = 0;
        var buf = new short[ToneGenerator.SampleRate];
        foreach (var (chip, period) in new[] { (0, 956), (0, 0x1AC), (1, 0x0FE), (1, 0x020) })
        {
            var g = new ToneGenerator(1.0);
            double hz = ChipNotes.Clock[chip] / (32.0 * period);
            g.SetNote(0, Wave.Square, hz, 1.0);
            g.Render(buf);
            int edges = Rising(buf);
            bool ok = Math.Abs(edges - hz) <= 1.0;
            Console.WriteLine($"generador tono {(chip == 0 ? "sn1" : "sn2")} periodo {period}: esperado {hz:F1} Hz ({ChipNotes.NoteName(hz, true)}), medido {edges} Hz: {(ok ? "ok" : "FALLO")}");
            if (!ok) bad++;
        }
        double[] rms = new double[16];
        for (int att = 0; att < 16; att++)
        {
            var g = new ToneGenerator(1.0, 1.0);   // escala 1: una voz a nivel 1 llega al maximo sin recortar
            g.SetNote(0, Wave.Square, 440.0, ChipNotes.Level(att));
            g.Render(buf);
            rms[att] = Math.Sqrt(buf.Average(s => (double)s * s));
        }
        bool lv = rms[15] == 0 && Enumerable.Range(1, 14).All(a => Math.Abs(20 * Math.Log10(rms[a] / rms[0]) + 2 * a) < 0.1);
        Console.WriteLine($"generador volumen: 0..14 = -2 dB por paso, 15 = silencio (nivel 0: {rms[0]:F0}, 7: {rms[7]:F0}, 14: {rms[14]:F0}): {(lv ? "ok" : "FALLO")}");
        if (!lv) bad++;
        {
            var g = new ToneGenerator(1.0);
            double shift = ChipNotes.Clock[1] / 2048;         // sn2, velocidad 2
            g.SetNote(0, Wave.PeriodicNoise, shift, 1.0);
            g.Render(buf);
            int edges = Rising(buf);
            bool ok = Math.Abs(edges - shift / 15) <= 1.0;
            Console.WriteLine($"generador ruido periodico: esperado {shift / 15:F1} Hz, medido {edges} Hz: {(ok ? "ok" : "FALLO")}");
            if (!ok) bad++;
            g = new ToneGenerator(1.0);
            g.SetNote(0, Wave.WhiteNoise, ChipNotes.Clock[1] / 512, 1.0);
            g.Render(buf);
            int e2 = Rising(buf);
            bool ok2 = e2 > 1000 && buf.Count(s => s > 0) is > 20000 and < 24100;
            Console.WriteLine($"generador ruido blanco: {e2} cambios/s, {buf.Count(s => s > 0) * 100 / buf.Length}% positivo: {(ok2 ? "ok" : "FALLO")}");
            if (!ok2) bad++;
        }
        return bad;
    }

    static int Rising(short[] b)
    {
        int n = 0;
        for (int i = 1; i < b.Length; i++) if (b[i - 1] <= 0 && b[i] > 0) n++;
        return n;
    }
}
