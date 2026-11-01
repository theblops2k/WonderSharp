namespace WonderBoy.Audio;

// Lectura independiente de las tablas de sonido de la ROM (sin la RAM de SoundProgram): cada canal es
// un objeto con campos con nombre. Da, por cada avance (2 por fotograma), el periodo y la atenuacion
// esperados de cada voz y el ruido de cada chip. Solo para un sonido por vez desde el arranque
// (sin prioridades entre efectos). Sirve para contrastar la salida de SoundProgram en --check-sound.
public sealed class SoundScore
{
    sealed class Ch
    {
        public bool Active, ToneDone, VolDone, Noise, NoiseOwnVol, Music;
        public int Loop, Table, Vol, Freq, Seq, Div, Dur, DefDur, Ret, LoopStart;
        public int TCnt, TPtr, TStart, TStep, VCnt, VPtr, VStart, VStep;
    }

    readonly byte[] rom;
    readonly Ch?[] ch = new Ch?[6];                       // k 0-2 sn2, 3-5 sn1
    readonly int[] noiseCtrl = new int[2], noiseAtt = { 15, 15 };   // por chip (0 sn1, 1 sn2)
    readonly int[] lastFlags = new int[6];
    int tempo = 8, tempoSaved = 8;

    public SoundScore(byte[] soundRom, int code)
    {
        rom = soundRom;
        int c = code & 0x3F;
        if ((code & 0xF0) == 0xF0 || code < 0x08 || (code & 0xC0) != 0xC0 && c < 0x08) return; // ordenes: sin notas
        if ((code & 0xC0) == 0xC0)
        {
            int p = W(0x0B2A + 2 * c);
            int n = rom[p++] == 0 ? 3 : 6;
            for (int i = 0; i < n; i++) { var x = New(true); x.Seq = W(p); p += 2; ch[5 - i] = x; }
        }
        else
        {
            int p = W(0x0B00 + 2 * (c - 8));
            int n = rom[p++] == 0 ? 3 : 6;
            for (int i = 0; i < n; i++)
            {
                var x = New(false);
                x.VPtr = x.VStart = W(p); x.TPtr = x.TStart = W(p + 2); p += 4;
                ch[i] = x;
            }
        }
    }

    int W(int a) => a < rom.Length - 1 ? rom[a] | rom[a + 1] << 8 : 0;

    // Valores iniciales: plantilla de canal de la ROM ($0770).
    Ch New(bool music)
    {
        const int T = 0x0770;
        byte f = rom[T];
        return new Ch
        {
            Music = music, Active = (f & 0x80) != 0, ToneDone = (f & 0x08) != 0, VolDone = (f & 0x10) != 0,
            Noise = (f & 0x20) != 0, NoiseOwnVol = (f & 0x40) != 0, Loop = f & 7,
            Table = W(T + 1), Vol = W(T + 3), Freq = W(T + 5), Seq = W(T + 7), Div = rom[T + 9], Dur = rom[T + 0x0A],
            DefDur = rom[T + 0x0B], Ret = W(T + 0x0C), LoopStart = W(T + 0x0E),
            TCnt = W(T + 0x10), TPtr = W(T + 0x12), TStart = W(T + 0x14), TStep = W(T + 0x16),
            VCnt = W(T + 0x18), VPtr = W(T + 0x1A), VStart = W(T + 0x1C), VStep = W(T + 0x1E),
        };
    }

    public bool Playing => ch.Any(x => x != null && x.Active);

    // Un avance: canales y luego valores de salida.
    public void Step()
    {
        for (int k = 0; k < 6; k++)
        {
            var x = ch[k];
            if (x == null) continue;
            int chip = k < 3 ? 1 : 0;
            if (x.Music) { Sequence(x); Envelopes(x, chip); }
            else
            {
                Envelopes(x, chip);
                if (x.ToneDone && x.VolDone) x.Active = false;
            }
            if (!x.Active) { x.Vol &= 0x00FF; ch[k] = null; lastFlags[k] = 0; finished[k] = x; }
        }
    }

    readonly Ch?[] finished = new Ch?[6];

    void Sequence(Ch x)
    {
        x.Div = (x.Div - 1) & 0xFF;
        if (x.Div != 0) return;
        x.Div = tempo;
        x.Dur = (x.Dur - 1) & 0xFF;
        if (x.Dur != 0) return;
        bool stop = false;
        while (!stop)
        {
            int b = rom[x.Seq++];
            switch ((b >> 5) & 3)
            {
                case 0: x.DefDur = b; break;
                case 1:
                    x.Dur = x.DefDur; stop = true;
                    if ((b & 0x1F) == 0x1F) { x.VolDone = true; x.Vol &= 0x00FF; }
                    else { x.Freq = W(x.Table + 2 * (b & 0x1F)); x.ToneDone = false; x.VolDone = false; }
                    break;
                case 2:
                    switch (b & 7)
                    {
                        case 0: x.Loop = rom[x.Seq++] & 7; x.LoopStart = x.Seq; break;
                        case 1: { int a = (x.Loop - 1) & 0xFF; if (a != 0) { x.Loop = a & 7; if (a > 7) { AllFlags(x, a); } x.Seq = x.LoopStart; } break; }
                        case 2: { int t = W(x.Seq); x.Ret = x.Seq + 2; x.Seq = t; break; }
                        case 3:
                            if (x.Ret == 0) { stop = true; Clear(x); }
                            else { x.Seq = x.Ret; x.Ret = 0; }
                            break;
                        case 4: x.Seq = W(x.Seq); break;
                    }
                    break;
                case 3:
                    switch (b & 7)
                    {
                        case 0: x.Table = 0x07A0 + 2 * (rom[x.Seq++] & 0x3F); break;
                        case 1: tempo = tempoSaved = x.Div = rom[x.Seq++]; break;
                        case 2: x.Vol = W(x.Seq); x.Seq += 2; break;
                        case 3: x.VStart = W(0x1D18 + ((rom[x.Seq++] * 2) & 0xFF)); break;
                        case 4: x.TStart = W(0x1D08 + ((rom[x.Seq++] * 2) & 0xFF)); break;
                    }
                    break;
            }
        }
        x.TCnt = 1; x.VCnt = 1; x.TPtr = x.TStart; x.VPtr = x.VStart;
    }

    // Bucle con cuenta 0 (fin de bucle 0 - 1 = $FF): todas las banderas quedan a 1.
    static void AllFlags(Ch x, int a)
    {
        x.Active = x.ToneDone = x.VolDone = x.Noise = x.NoiseOwnVol = true; x.Loop = a & 7;
    }

    static void Clear(Ch x)
    {
        x.Active = x.ToneDone = x.VolDone = x.Noise = x.NoiseOwnVol = false; x.Loop = 0;
    }

    void Envelopes(Ch x, int chip)
    {
        if (!x.ToneDone)
        {
            int cnt = (x.TCnt - 1) & 0xFFFF;
            bool run = true;
            if (cnt == 0)
            {
                int p = x.TPtr;
                while (true)
                {
                    int e = rom[p], d = rom[p + 1]; p += 2;
                    if (d != 0xFF) { x.TStep = W(p); p += 2; x.TPtr = p; cnt = d << 8 | e; break; }
                    int op = e & 7;
                    if (op == 0) { x.ToneDone = true; run = false; break; }
                    if (op == 1) { x.Freq = W(p); p += 2; }
                    else if (op == 2) { x.Freq = (x.Freq + W(p)) & 0xFFFF; p += 2; }
                    else if (op == 3) p = W(p);
                    else if (op == 4) { x.Noise = false; x.NoiseOwnVol = true; }
                    else if (op == 5)
                    {
                        int b = rom[p]; p += 2;
                        noiseCtrl[chip] = b & 7; noiseAtt[chip] = ~(b >> 4) & 0x0F;
                        x.Noise = true; x.NoiseOwnVol = true;
                    }
                    else if (op == 6) { x.Noise = true; x.NoiseOwnVol = false; noiseCtrl[chip] = 3; }
                }
            }
            if (run) { x.TCnt = cnt; x.Freq = (x.Freq + x.TStep) & 0xFFFF; }
        }
        if (!x.VolDone)
        {
            int cnt = (x.VCnt - 1) & 0xFFFF;
            if (cnt == 0)
            {
                int p = x.VPtr;
                while (true)
                {
                    int e = rom[p], d = rom[p + 1]; p += 2;
                    if (d != 0xFF) { x.VStep = W(p); p += 2; x.VPtr = p; cnt = d << 8 | e; break; }
                    int op = e & 7;
                    if (op == 0) { x.VolDone = true; return; }
                    if (op == 1) { x.Vol = W(p); p += 2; }
                    else if (op == 2) { x.Vol = (x.Vol + W(p)) & 0xFFFF; p += 2; }
                    else if (op == 3) p = W(p);
                }
            }
            x.VCnt = cnt; x.Vol = (x.Vol + x.VStep) & 0xFFFF;
        }
    }

    Ch? Any(int k) => ch[k] ?? finished[k];

    // Salida esperada tras el avance: periodo y atenuacion de las 3 voces de tono y del ruido.
    public (int[,] period, int[,] att, int[] noiseCtrl) Output()
    {
        var period = new int[2, 3];
        var att = new int[2, 4];
        for (int k = 0; k < 6; k++)
        {
            int chip = k < 3 ? 1 : 0, v = k % 3;
            var x = Any(k);
            period[chip, v] = x == null ? 0 : (x.Freq >> 4) & 0x3FF;
            att[chip, v] = x == null ? 15 : (~(x.Vol >> 8) >> 4) & 0x0F;
        }
        for (int chip = 0; chip < 2; chip++)
        {
            var x = ch[chip == 1 ? 2 : 5];                     // tercer canal del chip (activo)
            int a = 15;
            if (x != null && x.Noise)
            {
                if (x.NoiseOwnVol) a = noiseAtt[chip];
                else { a = att[chip, 2]; att[chip, 2] = 15; }
            }
            att[chip, 3] = a;
        }
        return (period, att, (int[])noiseCtrl.Clone());
    }
}
