namespace WonderBoy.Audio;

// Programa de la CPU de sonido (epr7498a.3, docs/analysis/wboy2u_soundcpu.asm) traducido a C#.
// La ROM solo se lee como datos (tablas, secuencias, envolventes); la RAM $8000-$87FF es 'ram'.
//   $8000 / $8001   efecto / musica pendiente ($FF = nada)       $8002 / $8003  efecto / musica activos
//   $8004           efecto en curso (prioridad)                    $8005 / $8006  tempo / tempo guardado
//   $8007/$8008     ruido sn2 (control, volumen $Fx)               $8009/$800A    ruido sn1
//   $800B-$8015     11 bytes para sn2 ($C000)                      $8016-$8020    11 bytes para sn1 ($A000)
//   $8021-$8026     tipo de canal k (0 libre, 1 efecto, 2 musica)  $8027 + k*$20  canal k (k 0-2 sn2, 3-5 sn1)
//   $80E7 canal en curso (6..1)  $80E9 fin de lectura  $80EA volcar registros  $80EB puntero temporal
//   $80ED/$80EE aviso y fase de IRQ   $80F2 pausa   $80F3 musica bloqueada   $80F4 (sin uso)
// Canal (+n): 0 banderas (b0-2 bucle, b3 fin env. tono, b4 fin env. volumen, b5/b6 ruido, b7 activo),
//   1-2 base de tabla de tonos ($07A0+2t), 3-4 volumen (nibble alto de +4), 5-6 periodo <<4,
//   7-8 secuencia, 9 divisor de tempo, A duracion restante, B duracion por defecto, C-D retorno,
//   E-F inicio de bucle, 10-11/12-13/14-15/16-17 env. tono (cuenta, puntero, inicio, paso),
//   18-19/1A-1B/1C-1D/1E-1F env. volumen (igual).
// Ritmo: 4 IRQ por fotograma (lineas 32/96/160/224, system1.cpp). Fases 0/2 = ordenes ($01D0),
// fases 1/3 = avance de canales ($0330). Los registros calculados se envian en la IRQ siguiente.
public sealed class SoundProgram
{
    readonly byte[] rom;
    readonly byte[] ram = new byte[0x800];

    // Escritura de un byte a un chip: 0 = sn1 ($A000, 2 MHz), 1 = sn2 ($C000, 4 MHz).
    public Action<int, byte>? ChipWrite;

    public const int Sn1 = 0, Sn2 = 1;

    public SoundProgram(byte[] soundRom)
    {
        rom = soundRom;
        Reset();
    }

    public byte this[int addr] => Rd(addr);

    // Lectura del mapa de sonido: ROM $0000-$7FFF (fuera del .bin = 0), RAM $8000-$9FFF (espejo de $800).
    byte Rd(int a)
    {
        a &= 0xFFFF;
        if (a < 0x8000) return a < rom.Length ? rom[a] : (byte)0;
        if (a < 0xA000) return ram[a & 0x7FF];
        return 0xFF;
    }
    void Wr(int a, int v) { a &= 0xFFFF; if (a >= 0x8000 && a < 0xA000) ram[a & 0x7FF] = (byte)v; }
    int Rw(int a) => Rd(a) | Rd(a + 1) << 8;
    void Ww(int a, int v) { Wr(a, v & 0xFF); Wr(a + 1, (v >> 8) & 0xFF); }

    // $00FA arranque (y $0003 desde las ordenes $F8-$FF)
    public void Reset()
    {
        Silence();                                                   // $0160
        Array.Clear(ram);                                            // $017A $8000-$87FF = 0
        Wr(0x8001, 0xFF);                                            // $0103
    }

    // $0160: silencia los 4 canales de sn2 y sn1 (escritura directa)
    void Silence()
    {
        foreach (int chip in new[] { Sn2, Sn1 })
            foreach (byte v in new byte[] { 0x9F, 0xBF, 0xDF, 0xFF }) ChipWrite?.Invoke(chip, v);
    }

    // $016D: escritura directa de prueba (orden $F5)
    void TestTone()
    {
        foreach (int chip in new[] { Sn2, Sn1 })
            foreach (byte v in new byte[] { 0x80, 0x0F, 0xAF, 0x0B, 0xC0, 0x0A, 0x90, 0xB0, 0xD0 }) ChipWrite?.Invoke(chip, v);
    }

    // $0066 NMI: llega un codigo de la CPU principal (lectura de $E000)
    public void Nmi(byte code)
    {
        int b = code;
        if ((b & 0xF0) == 0xF0) { Special(b & 0x0F); return; }      // $0074-$007D
        if ((b & 0xC0) == 0xC0) Wr(0x8001, b & 0x3F);                // $008F musica
        else Wr(0x8000, b);                                          // $0089 efecto u orden
    }

    // $00B0 ordenes $F0-$FF (tabla $00B3)
    void Special(int n)
    {
        switch (n)
        {
            case 0: Wr(0x80F4, 0); break;                            // $00D3
            case 1: Wr(0x80F4, 1); break;                            // $00D9
            case 2: Wr(0x80F2, 1); break;                            // $00DF pausa
            case 3: Wr(0x80F2, 0); break;                            // $00E5
            case 4: Silence(); break;                                // $0160
            case 5: TestTone(); break;                               // $016D
            case 6: Wr(0x80F3, 1); break;                            // $00EB bloquea musica
            case 7: Wr(0x80F3, 0); break;                            // $00F1
            default: Reset(); break;                                 // $00F7 jp $0003
        }
    }

    // $0038 IRQ + bucle principal $0109: vuelca registros, avanza la fase y ejecuta su rutina.
    public void Irq()
    {
        Flush();                                                     // $0121
        int ph = (Rd(0x80EE) + 1) & 3;                               // $003E
        Wr(0x80EE, ph);
        if ((ph & 1) == 0) Commands();                               // tabla $0119: $01D0
        else Update();                                               //              $0330
    }

    // $0121
    void Flush()
    {
        if (Rd(0x80EA) == 0) return;
        Wr(0x80EA, 0);
        for (int i = 0; i < 11; i++) ChipWrite?.Invoke(Sn2, Rd(0x800B + i));
        for (int i = 0; i < 11; i++) ChipWrite?.Invoke(Sn1, Rd(0x8016 + i));
    }

    // $01D0
    void Commands()
    {
        if (Rd(0x8000) != 0xFF) { StartEffect(Rd(0x8000)); Wr(0x8000, 0xFF); }
        if (Rd(0x8001) != 0xFF) { StartMusic(Rd(0x8001)); Wr(0x8001, 0xFF); }
    }

    // $01EE efecto (o orden 0-7). Bit 6 = forzar, bit 7 = no reiniciar si ya suena el mismo.
    void StartEffect(int b)
    {
        int c = b & 0x3F;
        if (c < 0x08) { Control(c); return; }                        // $02CA
        if (Rd(0x8002) != 0 && (b & 0x40) == 0)                      // $01F7-$01FF
        {
            int cur = Rd(0x8004);
            if (cur == c) { if ((b & 0x80) != 0) return; }           // $020E
            else if (cur < c && cur < 0x13) return;                  // $0207-$020B
        }
        Wr(0x8002, 0xFF);                                            // $0211
        Wr(0x8004, c);
        int hl = Rw(0x0B00 + (((c - 8) * 2) & 0xFF));                // $021A $018C
        int n = Rd(hl++) == 0 ? 3 : 6;                               // $0222
        for (int k = 0; k < n; k++)                                  // $022B
        {
            int ix = 0x8027 + k * 0x20;
            for (int i = 0; i < 0x20; i++) Wr(ix + i, Rd(0x0770 + i));
            Wr(0x8021 + k, 1);
        }
        for (int k = 0; k < n; k++)                                  // $0248
        {
            int ix = 0x8027 + k * 0x20;
            int v = Rw(hl); hl += 2;
            int f = Rw(hl); hl += 2;
            Ww(ix + 0x1A, v); Ww(ix + 0x1C, v);
            Ww(ix + 0x12, f); Ww(ix + 0x14, f);
        }
    }

    // $0274 musica (canales desde el 5 hacia abajo)
    void StartMusic(int a)
    {
        if (Rd(0x80F3) != 0) return;
        Wr(0x8003, 0xFF);
        int hl = Rw(0x0B2A + ((a * 2) & 0xFF));
        int n = Rd(hl++) == 0 ? 3 : 6;
        for (int k = 0; k < n; k++)                                  // $028F lddr
        {
            int ix = 0x80C7 - k * 0x20;
            for (int i = 0; i < 0x20; i++) Wr(ix + i, Rd(0x0770 + i));
            Wr(0x8026 - k, 2);
        }
        for (int k = 0; k < n; k++)                                  // $02AC
        {
            Ww(0x80C7 - k * 0x20 + 7, Rw(hl)); hl += 2;
        }
        Wr(0x8005, 0x08); Wr(0x8006, 0x08);                          // $02C1
    }

    // $02CA ordenes 0-7 (tabla $02CD)
    void Control(int c)
    {
        switch (c)
        {
            case 0: for (int k = 0; k < 6; k++) Wr(0x8027 + k * 0x20, 0); break; // $02DE
            case 1: for (int k = 0; k < 3; k++) Wr(0x8027 + k * 0x20, 0); break; // $02EC
            case 2: for (int k = 3; k < 6; k++) Wr(0x8027 + k * 0x20, 0); break; // $02FA
            case 3: Wr(0x8005, (sbyte)Rd(0x8005) >> 1); break;      // $0308 sra
            case 4: Wr(0x8005, Rd(0x8005) << 1); break;              // $0311 sla
            case 5: Wr(0x8005, Rd(0x8006)); break;                   // $031A
        }
    }

    // $0330
    void Update()
    {
        if (Rd(0x80F2) != 0) return;
        Channels();                                                  // $0341
        BuildRegisters();                                            // $06F0
        Wr(0x80EA, 1);
    }

    // $0341
    void Channels()
    {
        for (int k = 0; k < 6; k++)
        {
            int iy = 0x8021 + k, ix = 0x8027 + k * 0x20;
            Wr(0x80E7, 6 - k);                                       // $034B / $0378
            int t = Rd(iy);
            if (t == 0) continue;
            if (t == 1)                                              // $03A0 efecto: solo envolventes
            {
                Envelopes(ix);
                if ((Rd(ix) & 0x18) == 0x18) Wr(ix, 0);
            }
            else { Sequence(ix); Envelopes(ix); }                    // $03B0 musica
            if ((Rd(ix) & 0x80) == 0) { Wr(iy, 0); Wr(ix + 4, 0); }  // $0360
        }
        int all = 0;                                                 // $037E
        for (int k = 0; k < 6; k++) all |= Rd(0x8021 + k);
        if ((all & 1) == 0) Wr(0x8002, 0);
        if ((all & 2) == 0) Wr(0x8003, 0);
    }

    // $03B7 lector de secuencia
    void Sequence(int ix)
    {
        Wr(ix + 9, Rd(ix + 9) - 1);
        if (Rd(ix + 9) != 0) return;
        Wr(ix + 9, Rd(0x8005));
        Wr(ix + 0x0A, Rd(ix + 0x0A) - 1);
        if (Rd(ix + 0x0A) != 0) return;
        Wr(0x80E9, 0);
        do
        {
            int hl = Rw(ix + 7);                                     // $03C9
            int b = Rd(hl);
            Ww(0x80EB, hl + 1);
            switch ((b >> 5) & 3)                                    // $0414 tabla $0417
            {
                case 0: Wr(ix + 0x0B, b); break;                     // $041F duracion
                case 1: Note(ix, b); break;                          // $0423
                case 2: Flow(ix, b & 7); break;                      // $04E0
                case 3: Param(ix, b & 7); break;                     // $0456
            }
            Ww(ix + 7, Rw(0x80EB));                                  // $03DD
        } while (Rd(0x80E9) == 0);
        Ww(ix + 0x10, 1); Ww(ix + 0x18, 1);                          // $03EC
        Ww(ix + 0x12, Rw(ix + 0x14));
        Ww(ix + 0x1A, Rw(ix + 0x1C));
    }

    // $0423 nota ($1F = silencio)
    void Note(int ix, int b)
    {
        Wr(ix + 0x0A, Rd(ix + 0x0B));
        Wr(0x80E9, 1);
        int n = b & 0x1F;
        if (n == 0x1F) { Wr(ix, Rd(ix) | 0x10); Wr(ix + 4, 0); return; } // $044D
        Ww(ix + 5, Rw(Rw(ix + 1) + n * 2));                          // $0435 $018C
        Wr(ix, Rd(ix) & 0xE7);
    }

    int NextByte() { int hl = Rw(0x80EB); Ww(0x80EB, hl + 1); return Rd(hl); }
    int NextWord() { int hl = Rw(0x80EB); Ww(0x80EB, hl + 2); return Rw(hl); }

    // $04E0 control de flujo (tabla $04E6)
    void Flow(int ix, int op)
    {
        switch (op)
        {
            case 0:                                                  // $04F6 inicio de bucle
                {
                    int a = NextByte();
                    Ww(ix + 0x0E, Rw(0x80EB));
                    Wr(ix, (Rd(ix) & 0xF8) | (a & 7));
                    break;
                }
            case 1:                                                  // $0511 fin de bucle
                {
                    int f = Rd(ix);
                    int a = ((f & 7) - 1) & 0xFF;
                    if (a == 0) return;
                    Wr(ix, (f & 0xF8) | a);
                    Ww(0x80EB, Rw(ix + 0x0E));
                    break;
                }
            case 2:                                                  // $052B llamada
                {
                    int target = NextWord();
                    Ww(ix + 0x0C, Rw(0x80EB));
                    Ww(0x80EB, target);
                    break;
                }
            case 3:                                                  // $053D retorno / fin
                {
                    int ret = Rw(ix + 0x0C);
                    if (ret == 0) { Wr(0x80E9, 1); Wr(ix, 0); return; }
                    Ww(ix + 0x0C, 0);
                    Ww(0x80EB, ret);
                    break;
                }
            case 4: Ww(0x80EB, NextWord()); break;                   // $055C salto
        }
    }

    // $0456 parametros (tabla $045C)
    void Param(int ix, int op)
    {
        switch (op)
        {
            case 0: Ww(ix + 1, 0x07A0 + ((NextByte() & 0x3F) * 2)); break;          // $046D transporte
            case 1: { int a = NextByte(); Wr(0x8005, a); Wr(0x8006, a); Wr(ix + 9, a); break; } // $0486 tempo
            case 2: Ww(ix + 3, NextWord()); break;                                   // $0498 volumen
            case 3: Ww(ix + 0x1C, Rw(0x1D18 + ((NextByte() * 2) & 0xFF))); break;   // $04A9 env. volumen
            case 4: Ww(ix + 0x14, Rw(0x1D08 + ((NextByte() * 2) & 0xFF))); break;   // $04BE env. tono
        }
    }

    // $0570
    void Envelopes(int ix) { ToneEnvelope(ix); VolumeEnvelope(ix); }

    // Direccion de los registros de ruido del chip del canal en curso ($80E7 >= 4: sn2).
    int NoiseAddr() => Rd(0x80E7) >= 4 ? 0x8007 : 0x8009;

    // $0577 envolvente de tono: pares (cuenta, paso) o ordenes $FFxx (tabla $05CD)
    void ToneEnvelope(int ix)
    {
        if ((Rd(ix) & 0x08) != 0) return;
        int cnt = (Rw(ix + 0x10) - 1) & 0xFFFF;
        if (cnt == 0)
        {
            int hl = Rw(ix + 0x12);
            while (true)
            {
                int e = Rd(hl), d = Rd(hl + 1); hl += 2;
                if (d != 0xFF)
                {
                    Ww(ix + 0x16, Rw(hl)); hl += 2;
                    Ww(ix + 0x12, hl);
                    cnt = d << 8 | e;
                    break;
                }
                switch (e & 7)
                {
                    case 0: Wr(ix, Rd(ix) | 0x08); return;           // $05DD fin
                    case 1: Ww(ix + 5, Rw(hl)); hl += 2; break;      // $05E5 fija tono
                    case 2: Ww(ix + 5, Rw(ix + 5) + Rw(hl)); hl += 2; break; // $05F1 suma
                    case 3: hl = Rw(hl); break;                      // $06E7 salto
                    case 4: Wr(ix, (Rd(ix) & 0x9F) | 0x40); break;   // $0606 sin ruido
                    case 5:                                          // $0612 ruido con volumen propio
                        {
                            int b = Rd(hl); hl += 2;
                            int de = NoiseAddr();
                            Wr(de, b & 7);
                            Wr(de + 1, (~(b >> 4) & 0x0F) | 0xF0);
                            Wr(ix, Rd(ix) | 0x60);
                            break;
                        }
                    case 6:                                          // $063C ruido con tono y volumen del canal 2
                        Wr(ix, (Rd(ix) & 0x9F) | 0x20);
                        Wr(NoiseAddr(), 3);
                        break;
                    case 7: break;                                   // $06ED
                }
            }
        }
        Ww(ix + 0x10, cnt);                                          // $05AC
        Ww(ix + 5, Rw(ix + 5) + Rw(ix + 0x16));
    }

    // $0658 envolvente de volumen (tabla $06AE)
    void VolumeEnvelope(int ix)
    {
        if ((Rd(ix) & 0x10) != 0) return;
        int cnt = (Rw(ix + 0x18) - 1) & 0xFFFF;
        if (cnt == 0)
        {
            int hl = Rw(ix + 0x1A);
            while (true)
            {
                int e = Rd(hl), d = Rd(hl + 1); hl += 2;
                if (d != 0xFF)
                {
                    Ww(ix + 0x1E, Rw(hl)); hl += 2;
                    Ww(ix + 0x1A, hl);
                    cnt = d << 8 | e;
                    break;
                }
                switch (e & 7)
                {
                    case 0: Wr(ix, Rd(ix) | 0x10); return;           // $06BE fin
                    case 1: Ww(ix + 3, Rw(hl)); hl += 2; break;      // $06C6 fija volumen
                    case 2: Ww(ix + 3, Rw(ix + 3) + Rw(hl)); hl += 2; break; // $06D2 suma
                    case 3: hl = Rw(hl); break;                      // $06E7 salto
                    default: break;                                  // $06ED
                }
            }
        }
        Ww(ix + 0x18, cnt);
        Ww(ix + 3, Rw(ix + 3) + Rw(ix + 0x1E));
    }

    // $06F0 / $070D: 11 bytes por chip (3 x tono/volumen, control y volumen de ruido)
    void BuildRegisters()
    {
        Build(0x8027, 0x8007, 0x800B);
        Build(0x8087, 0x8009, 0x8016);
    }

    void Build(int ix, int noise, int buf)
    {
        int c = 0x08, hl = buf;
        for (int ch = 0; ch < 3; ch++, ix += 0x20)
        {
            Wr(hl++, (c << 4) | (Rd(ix + 5) >> 4));                  // latch tono: $80/$A0/$C0 | 4 bits bajos
            Wr(hl++, Rd(ix + 6) & 0x3F);                             // 6 bits altos
            c++;
            Wr(hl++, (c << 4) | ((~Rd(ix + 4) >> 4) & 0x0F));        // $90/$B0/$D0 | atenuacion
            c++;
        }
        Wr(hl++, Rd(noise) | 0xE0);                                  // $073F
        int a = 0x0F;
        int f = Rd(ix - 0x20);                                       // banderas del tercer canal
        if ((f & 0x20) != 0)
        {
            if ((f & 0x40) != 0) a = Rd(noise + 1);                  // $0752
            else { a = Rd(hl - 2); Wr(hl - 2, a | 0x0F); }           // $0756 el ruido toma el volumen del tono 2
        }
        Wr(hl, a | 0xF0);
    }
}
