namespace WonderBoy.Game;

// Pantalla de titulo construida solo desde la ROM principal (sin volcados de RAM).
// Traduccion de la rutina de titulo $0F52 (subestados $0F66, $0FC3, $0FE1, $0FEE, $0FFB) y de lo
// que necesita del arranque y de la IRQ:
//   arranque  $002A borra $D000-$DFFF; $0942 borra RAM $C000-$CFFF, FG bajo=0 / alto=8 ($095C),
//             BG=0 ($097B); $0347 ($C0AC=1, $C0AD=$5776, cola de paleta $C0A9=$C099);
//             $0745 paleta inicial ($5975/$5995 sprites, $5B95/$5C15 tiles); $03AA texto 12
//             ($538F: cabecera PLAYER/HIGH en FG filas 0-1 y bordes).
//   IRQ $0038 $07E9 copia $C800 (17 x 32 B) a $D000 (16 B); $063E cola de paleta;
//             $07C8 ciclo de $DA02 (pares duracion,color de $5776); $05FB scroll a $EFFC/$EFBD.
//   bucle     $018E: $C010--, $01FB efecto ($C087 -> $C088 -> $0231: $5776 byte a byte en
//             $DC46,+8..+32), estado actual.
// Datos de ROM: $543B (grafico FG, $0C15), $50D9 (textos, $0B7A), $5E01 (sprites, $0C2E),
// $5429/$54A1 (guion del fondo, $0C5B/$0C87), $5D35 (guiones de paleta), $5776 (ciclos).
public sealed partial class TitleRom
{
    readonly byte[] m = new byte[0x10000];
    bool bootTitleSound; // sonido de $0F66 ya encolado en Start() (fotograma 28)
    int state = StateTitle; // indice en $4F12 ($C07E)
    int sub;           // $C07F
    bool subWait;      // sub 0 partido por la espera de vblank $0417
    bool resumed;      // continuacion tras $0417 ejecutada en este fotograma
    public static bool SameFrameRerun = true;
    // $0A24($1F) tarda ~134k ciclos (~2 fotogramas a 4 MHz): la IRQ intermedia pone $C00B=1 dos
    // veces y el bucle principal ($03CE) solo consume una, asi que se pierde una vuelta.
    public static int ClearLost = 1;
    int lostFrames;

    public TitleRom(byte[] mainRom)
    {
        Array.Copy(mainRom, m, Math.Min(mainRom.Length, 0xC000));
    }

    // Copias para el dibujo en arrays reservados una vez (rellenadas con Array.Copy en cada lectura).
    readonly byte[] palCopy = new byte[0x800], vramCopy = new byte[0x1000], sprCopy = new byte[0x800];
    public byte[] Palette { get { Array.Copy(m, 0xD800, palCopy, 0, 0x800); return palCopy; } }
    public byte[] Vram { get { Array.Copy(m, 0xE000, vramCopy, 0, 0x1000); return vramCopy; } }
    public byte[] SpriteRam { get { Array.Copy(m, 0xD000, sprCopy, 0, 0x800); return sprCopy; } }
    public byte this[int a] => m[a & 0xFFFF];
    public void Poke(int a, byte v) => m[a & 0xFFFF] = v; // escritura externa (--round-load: $C301)

    // Solo para el dibujo (WideView): copia de los 64 KB y "en nivel" = partida (no attract, $C005=0)
    // con $2193 ejecutado en los ultimos 8 fotogramas. No alteran la RAM ni la logica.
    public byte[] CopyMemory() => (byte[])m.Clone();
    // Igual, en un array de 64 KB reutilizado por quien dibuja (WideView): copia entera con Array.Copy.
    public void CopyMemory(byte[] dst) => Array.Copy(m, dst, m.Length);

    // Solo para el dibujo (WideView): ranuras $C800-$CA1F y camara $C442-$C445 tal como estaban al
    // copiar la sprite RAM ($07E9, al inicio de Step). La sprite RAM va un paso por detras de las
    // ranuras; con esta copia los sprites de la franja ancha usan los mismos datos que el dibujo
    // original (sin ella, al cruzar X = 12 se veia un fotograma recortado en el borde ORIGINAL).
    readonly byte[] spriteSnap = new byte[0x220 + 4];
    bool spriteSnapOk;
    public void ApplySpriteSnapshot(byte[] dst)
    {
        if (!spriteSnapOk) return;
        Array.Copy(spriteSnap, 0, dst, 0xC800, 0x220);
        Array.Copy(spriteSnap, 0x220, dst, 0xC442, 4);
    }
    public bool InLevel => gameMode && m[0xC005] == 0 && stepCount - tickStep <= 8;
    int stepCount, tickStep = -1000;
    // Solo para el dibujo: true desde la primera pasada de $22A2 en la carga de ronda ($1431, LevelPasses)
    // hasta que se borra el fondo ($0A24 con bits $0C) o termina la ronda ($1908/2). No toca la RAM.
    public bool LevelTilesDrawn => gameMode && m[0xC005] == 0 && levelTiles;
    bool levelTiles;
    // Solo para el dibujo: true desde $1908/2 ($0A24($10), fin de ronda en la meta) hasta el borrado del
    // fondo $19B3 ($0A24($0C)): el telon de 24 filas de $199A ($19A0-$19AC) tapa el FG y el BG sigue
    // siendo el del nivel, asi que en WIDESCREEN se dibuja con WideView. No toca la RAM.
    public bool RoundEndCurtain => gameMode && m[0xC005] == 0 && roundCurtain;
    bool roundCurtain;
    // Solo para el dibujo: true mientras $1431 reparte sus 32 pasadas en varios fotogramas (la VRAM
    // del nivel aun esta a medias: las columnas sin escribir son restos). No toca la RAM.
    public bool LevelLoading { get; private set; }

    // Construye el estado en el fotograma 'frame' (numeracion de MAME). titleStart = fotograma
    // del subestado 0 del titulo (f29, attract_flow.md); irqStart = primer fotograma con la IRQ
    // de video activa ($C009=1), solo afecta a la fase del ciclo de $DA02.
    // Fase del ciclo $DA02: depende de cuanto tarda el arranque ($0000-$0140) en poner $C009=1,
    // no deducible de la ROM sin contar ciclos. Los volcados f300/f500/f700 acotan 5..8
    // (fuera de ese rango solo cambia $DA02); se usa 6.
    public const int DefaultIrqStart = 6;

    // Estados de attract ($4F12) que se encadenan tras el titulo: 2 = titulo $0F52,
    // 3 = records $101A, 4 = licencia $10D7. Despues de la licencia se queda en el ultimo subestado.
    public const int StateTitle = 2, StateRecords = 3, StateLicense = 4;
    public int State => state;

    // CorrectLicensed=T (Config.cfg): en la copia de la ROM, el texto del guion $5576 ("LISENCED  BY",
    // en $557A, licencia $1145) pasa a "LICENSED  BY" ($557C 'S'->'C', $557F 'C'->'S'). Solo si es el original.
    public void CorrectLicensed()
    {
        const int at = 0x557A;
        if (System.Text.Encoding.ASCII.GetString(m, at, 8) != "LISENCED") return;
        m[at + 2] = (byte)'C';
        m[at + 5] = (byte)'S';
    }

    // Menu de opciones (CorrectLicensed): pone "LICENSED" (true) o el "LISENCED" original (false) en la copia
    // de la ROM. Si la licencia ya esta en pantalla (guion $5576: celdas de 2 bytes desde $E414) se cambia
    // tambien alli; si no, se vera la proxima vez que se escriba el texto.
    public void SetLicensed(bool correct)
    {
        const int at = 0x557A, vram = 0xE414;
        string from = correct ? "LISENCED" : "LICENSED", to = correct ? "LICENSED" : "LISENCED";
        if (System.Text.Encoding.ASCII.GetString(m, at, 8) != from) return;
        m[at + 2] = (byte)to[2];
        m[at + 5] = (byte)to[5];
        for (int i = 0; i < 8; i++)
            if (m[vram + 2 * i] != (byte)from[i]) return;
        m[vram + 2 * 2] = (byte)to[2];
        m[vram + 2 * 5] = (byte)to[5];
    }

    public static TitleRom Build(byte[] mainRom, int frame, int titleStart = 29, int irqStart = DefaultIrqStart)
    {
        var t = Start(mainRom, titleStart, irqStart);
        for (int f = titleStart; f <= frame; f++) t.Step();
        return t;
    }

    // Arranque y calentamiento hasta titleStart; a partir de ahi se avanza con Step().
    public static TitleRom Start(byte[] mainRom, int titleStart = 29, int irqStart = DefaultIrqStart)
    {
        var t = new TitleRom(mainRom);
        t.Boot();
        t.m[0xC032] = 1;   // titulo ya cargado por el planificador ($01E4)
        for (int f = irqStart; f < titleStart; f++) { t.Irq(); t.m[0xC010]--; }
        // $0F66 del titulo corre en el bucle de f28 (ram_rand: $C0DB=1 en f28, 0 en f29): su sonido 0
        // va antes de la IRQ de f29; el resto de $0F66 lo hace el primer Step().
        Sound(t.m, 0x00); t.bootTitleSound = true;
        return t;
    }

    // Un fotograma: IRQ y, si no se pierde por el borrado ($0A24), bucle principal.
    public void Step()
    {
        stepCount++;
        Irq();
        if (lostFrames > 0) { lostFrames--; return; }
        if (gameMode) GameFrame(); // fin de partida (TitleRom.GameOver.cs)
        else MainFrame();
    }

    // ---- utilidades -------------------------------------------------------------------------
    int W(int a) => m[a & 0xFFFF] | m[(a + 1) & 0xFFFF] << 8;
    void SetW(int a, int v) { m[a & 0xFFFF] = (byte)v; m[(a + 1) & 0xFFFF] = (byte)(v >> 8); }
    int Table(int baseAddr, int idx) => W(baseAddr + 2 * idx); // $0A9A

    // $0A16: escribe 'a' en bytes alternos, n celdas.
    void FillAlt(int addr, int n, byte a) { for (int i = 0; i < n; i++) m[(addr + 2 * i) & 0xFFFF] = a; }

    // $0A24: borrados segun bits de c (bit7 => valor b; aqui siempre 0).
    void Clear(int c)
    {
        if ((c & 0x01) != 0) ClearRect(0xE884, 0);  // $09AF FG bajo, 26x28
        if ((c & 0x02) != 0) ClearRect(0xE885, 0);  // $09C5 FG alto
        if ((c & 0x04) != 0) FillAlt(0xE000, 0x400, 0); // $0982 BG bajo
        if ((c & 0x08) != 0) FillAlt(0xE001, 0x400, 0); // $098C BG alto
        if ((c & 0x10) != 0) { Array.Clear(m, 0xC800, 0x400); m[0xC0BE] = 1; } // $0996
        if ((c & 0x20) != 0) FillAlt(0xE800, 0x400, 0); // $0963
        if ((c & 0x40) != 0) FillAlt(0xE801, 0x400, 8); // $096D (a|8)
        if (c == 0x1F) lostFrames = ClearLost;
        if ((c & 0x0C) != 0) levelTiles = roundCurtain = false;   // fondo borrado: ya no hay tiles del nivel
    }

    void ClearRect(int addr, byte a)
    {
        for (int r = 0; r < 0x1A; r++, addr += 0x40)
            for (int c = 0; c < 0x1C; c++) m[addr + 2 * c] = a;
    }

    // ---- arranque ---------------------------------------------------------------------------
    void Boot()
    {
        Array.Clear(m, 0xD000, 0x1000);              // $002A
        Array.Clear(m, 0xC000, 0x1000);              // $0942
        FillAlt(0xE800, 0x400, 0); FillAlt(0xE801, 0x400, 8); // $095C
        FillAlt(0xE000, 0x400, 0); FillAlt(0xE001, 0x400, 0); // $097B
        Array.Clear(m, 0xC800, 0x400); m[0xC0BE] = 1;         // $0996
        DipDecode();                                  // $02D5
        m[0xC0AC] = 1; SetW(0xC0AD, 0x5776); SetW(0xC0A9, 0xC099); // $0347
        DipSettings();                                // $0358
        m[0xC014] = 0x3C;
        PaletteInit();                                // $0745
        Text(0x50D9, 12);                             // $03AA
        Array.Copy(m, 0x4F77, m, 0xC100, 0x140);      // $03AF: tabla de records por defecto
        SetW(0xC035, 0xC037);                         // $03BA: cola de puntos vacia
        Array.Fill(m, (byte)0xFF, 0xC037, 0x40);      // $03C0 $0A0F (bc=$3F: $40 bytes)
        m[0xC07D] = 0x10;                             // $03C8
    }

    // DIP switches ya invertidos como los deja $02DD/$02EB ($C011 = ~IN $0C, $C012 = ~IN $0D).
    // Valores de ram_rand f30 ($C011=$00, $C012=$13); sin verificar con otros ajustes.
    public static byte Dsw1 = 0x00, Dsw2 = 0x13;

    // $02D5: campos de bits (anchos $4F38) de $C011/$C012 a los destinos de $4F41
    // ($C018 $C01B $C026 $C006 $C027 $C028 $C02D $C02C $C02E).
    void DipDecode()
    {
        m[0xC011] = Dsw1; m[0xC012] = Dsw2;
        m[0xC3DF] = 0;
        int ix = 0xC300;                              // $02D9: bufer de campos en $C300..
        foreach (int v in new[] { (int)Dsw1, Dsw2 })   // $0313
        {
            int e = v, c = 0;
            do
            {
                int b = m[0x4F38 + m[0xC3DF]];
                m[ix++] = (byte)(e & ((1 << b) - 1));  // $0339 mascara de b bits
                e >>= b;
                m[0xC3DF]++;
                c += b;
            } while (c < 8);
        }
        for (int i = 0; i < m[0xC3DF]; i++) m[W(0x4F41 + 2 * i)] = m[0xC300 + i]; // $02FB
    }

    // $0358-$0387: umbral de vida extra ($C028 = $30, $C029 = $70 o $90 si $C028 != 0), $C02D ^= 1,
    // vidas $C027 = $4F53[$C027] (0 = juego libre: $C02F = 1, $C027 = 1).
    void DipSettings()
    {
        int b = m[0xC028] != 0 ? 0x90 : 0x70;
        m[0xC028] = 0x30;
        m[0xC029] = (byte)b;
        m[0xC02D] ^= 0x01;
        int a = m[0x4F53 + m[0xC027]];
        m[0xC027] = (byte)a;
        if (a == 0) { m[0xC02F] = 1; m[0xC027] = 1; }
    }

    void PaletteInit()
    {
        for (int i = 0; i < 0x20; i++)               // $074C
            Array.Copy(m, 0x5995 + 16 * m[0x5975 + i], m, 0xD800 + 16 * i, 16);
        for (int i = 0; i < 0x80; i++)               // $077B
            Array.Copy(m, 0x5C15 + 8 * m[0x5B95 + i], m, 0xDA00 + 8 * i, 8);
    }

    // ---- IRQ --------------------------------------------------------------------------------
    void Irq()
    {
        SpriteCopy();     // $07E9
        PaletteQueue();   // $063E
        ObjectSpawn.Drain(m); // $07A6 cola de paletas de objeto $C0C7 (vacia fuera de partida)
        CycleDa02();      // $07C8
        m[0xC00B] = 1;
        int hl = (-W(0xC000) + 0x01E5) & 0xFFFF;    // $05FB (sin flip)
        SetW(0xEFFC, hl);
        m[0xEFBD] = m[0xC002];
        SoundDrain(m);    // $006E $0428 (el port no modela $C009 = 0; solo f1-f3 en ram_rand)
        if (playMode) PlayIrq(); // mando y monedero (TitleRom.Play.cs)
    }

    void SpriteCopy()
    {
        if (m[0xC0BE] == 0 && m[0xC0BD] == 0) return;
        m[0xC0BE] = 0;
        Array.Copy(m, 0xC800, spriteSnap, 0, 0x220);   // solo dibujo ancho (ApplySpriteSnapshot)
        Array.Copy(m, 0xC442, spriteSnap, 0x220, 4);
        spriteSnapOk = true;
        int src = 0xC800, dst = 0xD000;               // $C008 bit7 = 0: sin desplazamiento
        for (int n = 0; n < 0x11; n++, src += 0x20, dst += 0x10)
        {
            m[dst] = m[src]; m[dst + 1] = m[src + 1];
            SetW(dst + 2, W(src + 2));
            m[dst + 4] = m[src + 4]; m[dst + 5] = m[src + 5]; m[dst + 6] = m[src + 6];
            m[dst + 7] = (byte)((m[src + 7] & 0x7F) | (m[src + 9] & 0x80));
        }
    }

    void QueuePalette(byte cmd) // $0736
    {
        int p = W(0xC0A9); m[p] = cmd; SetW(0xC0A9, p + 1); m[0xC0AB]++;
    }

    void PaletteQueue() // $063E: se procesa desde el final (LIFO)
    {
        if (m[0xC0AB] == 0) return;
        int hl = W(0xC0A9);
        do { hl--; PaletteCmd(m[hl]); } while (--m[0xC0AB] != 0);
        SetW(0xC0A9, hl);
    }

    void PaletteCmd(byte b) // $065C
    {
        if ((b & 0xC0) == 0)
        {
            int hl = Table(0x5D35, b);
            while (m[hl] != 0xFF)
            {
                byte type = m[hl];
                int de = W(hl + 1), bc = W(hl + 3);
                hl += 5;
                switch (type)
                {
                    case 0: for (int i = 0; i < bc; i++) m[de + i] = m[hl++]; break;
                    case 1: { byte a = m[de]; Array.Copy(m, de + 1, m, de, bc - 1); m[de + bc - 1] = a; break; }
                    case 2: { byte a = m[de + bc - 1]; Array.Copy(m, de, m, de + 1, bc - 1); m[de] = a; break; }
                    case 3: Array.Copy(m, bc, m, de, 16); break;
                    default: Array.Copy(m, bc, m, de, 8); break;
                }
            }
        }
        else if ((b & 0x80) == 0)
        {
            int n = b & 0x1F;
            Array.Copy(m, 0x5995 + 16 * m[0x5975 + n], m, 0xD800 + 16 * n, 16);
        }
        else
        {
            int n = b & 0x7F;
            Array.Copy(m, 0x5C15 + 8 * m[0x5B95 + n], m, 0xDA00 + 8 * n, 8);
        }
    }

    void CycleDa02() // $07C8
    {
        if (--m[0xC0AC] != 0) return;
        int de = W(0xC0AD);
        if (m[de] == 0) de = 0x5776;
        m[0xC0AC] = m[de++];
        byte a = m[de++];
        SetW(0xC0AD, de);
        m[0xDA02] = a; m[0xDA03] = 0;
    }

    // ---- bucle principal --------------------------------------------------------------------
    void MainFrame()
    {
        // Vuelta a attract: la continuacion tras $0417 sigue dentro de $0F66 sin pasar por $018E,
        // asi que no decrementa; el $C010-- de ese fotograma es el de la re-ejecucion (pc_trace f5364).
        if (resumeNoDec) resumeNoDec = false;
        else m[0xC010]--;
        Effect();          // $01FB
        if (playMode && PlayCredit()) return; // $019B: creditos -> tabla $4F20 (TitleRom.Play.cs)
        // $01B5: con $C032=0 carga el siguiente estado de $4F12 y lo ejecuta en el mismo fotograma
        if (m[0xC032] == 0 && state < StateLicense)
        {
            m[0xC032] = 1; state++;
            m[0xC07E] = (byte)state; SetW(0xC081, Table(0x4F12, state)); // $01E9-$01F3
        }
        switch (state)     // $01F7
        {
            case StateTitle: TitleState(); break;     // $0F52
            case StateRecords: RecordsState(); break; // $101A
            default: LicenseState(); break;           // $10D7
        }
        // Tras la espera $0417 la IRQ ya dejo $C00B=1, asi que $03CE no espera y la siguiente
        // vuelta del bucle se ejecuta en el mismo fotograma (sin IRQ entre medias).
        if (SameFrameRerun && resumed) { resumed = false; MainFrame(); }
    }

    void Effect()
    {
        if (m[0xC087] != 0)
        {   // $0224
            m[0xC088] = m[0xC087];
            Array.Clear(m, 0xC089, 0x10);             // $0A73(3): tabla $5743
            m[0xC087] = 0;
        }
        // $0231 (titulo, paleta BG $DC46) y $0264 (licencia, paleta FG $DA46); el resto no se usa aqui
        int dst = m[0xC088] switch { 1 => 0xDC46, 2 => 0xDA46, _ => 0 };
        if (dst == 0) return;
        if (m[0xC08B] == 0) { m[0xC08B] = 1; SetW(0xC089, 0x5776); }
        int hl = W(0xC089);
        if (m[hl] == 0) hl = 0x5776;
        byte a = m[hl++];
        SetW(0xC089, hl);
        for (int i = 0; i < 5; i++) m[dst + 8 * i] = a;
    }

    // $0AA6: true = seguir (temporizador $C013 en segundos agotado).
    bool WaitSeconds()
    {
        if (m[0xC013] == 0) return true;
        if (--m[0xC014] != 0) return false;
        m[0xC014] = 0x3C;
        return --m[0xC013] == 0;
    }

    void TitleState()
    {
        switch (sub)
        {
            case 0:
                if (!subWait)
                {   // $0F66 hasta la espera $0417
                    if (bootTitleSound) bootTitleSound = false; // ya encolado en Start()
                    else Sound(m, 0x00);                 // $0F66 $0440
                    Clear(0x1F);
                    SetW(0xC000, 0); m[0xC002] = 0;
                    DrawPacked(0x543B, 0xEA8E, 0x09);  // $0C15
                    Text(0x50D9, 0x0A);                 // $0B7A
                    SpriteSet(0);                        // $0C2E
                    subWait = true;
                    return;
                }
                subWait = false; resumed = true;
                Sound(m, 0x00);                          // $0F92 $0440
                for (byte c = 0xC8; c <= 0xCC; c++) QueuePalette(c);
                m[0xDC00] = m[0xDC30] = 0xB6;
                m[0xDC08] = m[0xDC10] = 0;
                sub++;
                break;
            case 1: // $0FC3
                if (!WaitSeconds()) return;
                ScriptStart(1, 0x08, 0x0C);             // $0C5B
                m[0xC0BD] = 0;
                m[0xC087] = 1;
                m[0xC002] = 0x24;
                sub++;
                break;
            case 2: // $0FE1
                if (!ScriptStep()) return;
                m[0xC013] = 4;
                sub++;
                break;
            case 3: // $0FEE
                if (!WaitSeconds()) return;
                m[0xC013] = 8;
                sub++;
                break;
            case 4: // $0FFB
                if ((m[0xC010] & 1) == 0) QueuePalette(1);
                if (!WaitSeconds()) return;
                sub++;
                break;
            default: // $100F
                m[0xC088] = 0;
                sub = 0; m[0xC032] = 0;
                break;
        }
        m[0xC07F] = (byte)sub;   // $0FBE/$0FDC/$0FE9/$0FF6/$100A inc ($C07F); $1013 ld ($C07F),0
    }

    // $101A: records "THE BEST PLAYERS" (subestados $1028, $105E, $10AE, $10CC).
    void RecordsState()
    {
        switch (sub)
        {
            case 0: // $1028
                SetW(0xC000, 0); m[0xC002] = 0;
                Clear(0x1F);
                m[0xDC00] = m[0xDC30] = 0;
                QueuePalette(5);
                Text(0x50D9, 8);
                SetW(0xC3E1, 0xE18E);
                m[0xC3DF] = 0x14; m[0xC3E0] = 0;
                sub++;
                break;
            case 1: // $105E
            {
                if ((m[0xC010] & 1) != 0) return;
                m[0xC002]--;
                if ((++m[0xC3E0] & 0x0F) != 0) return;
                int de = W(0xC3E1);
                RecordEntry(m[0xC3DF], de);                      // $0F18
                FillAlt((de + 0x380) & 0xF7FF, 0x14, 0);          // $0A16, res 3,h
                int hl = (de - 0x80) & 0xFFFF;
                SetW(0xC3E1, (hl & 0x00FF) | (((hl >> 8) & 0x07) | 0xE0) << 8);
                if (--m[0xC3DF] != 0) return;
                sub++;
                break;
            }
            case 2: // $10AE
                if ((m[0xC010] & 1) != 0) return;
                m[0xC002]--;
                if (++m[0xC3DF] < 8) return;
                m[0xC013] = 0x0A;
                sub++;
                break;
            default: // $10CC
                if (!WaitSeconds()) return;
                sub = 0; m[0xC032] = 0;
                break;
        }
    }

    // $0F18: entrada n de $C100 (2 car. puesto, hueco, 6 digitos, "0", 2 huecos, 8 car. nombre),
    // solo bytes de tile (cada celda +2).
    void RecordEntry(int n, int de)
    {
        int hl = 0xC100 + 16 * (n - 1);
        for (int i = 0; i < 2; i++, de += 2) m[de] = m[hl++];
        de += 2;
        for (int i = 0; i < 6; i++, de += 2) m[de] = m[hl++];
        m[de] = 0x30;
        de += 6;
        for (int i = 0; i < 8; i++, de += 2) m[de] = m[hl++];
    }

    // $10D7: licencia / INSERT COIN (subestados $10E7, $1145, $1161, $116E, $118A).
    void LicenseState()
    {
        switch (sub)
        {
            case 0:
                if (!subWait)
                {   // $10E7 hasta la espera $0417
                    SetW(0xC000, 0);
                    Clear(0x1F);
                    for (byte c = 0x88; c <= 0x8C; c++) QueuePalette(c);
                    m[0xDC00] = 0;
                    subWait = true;
                    return;
                }
                subWait = false; resumed = true;
                QueuePalette(6);
                CopyScriptToFg();                                 // $1125
                Text(0x5429, 2);                                  // $0B75
                sub++;
                break;
            case 1: // $1145
                m[0xC002] = 0x98;
                ScriptStart(3, 0x04, 0x0E);
                QueuePalette(0xC1); QueuePalette(0xC2);
                sub++;
                break;
            case 2: // $1161
                if (!ScriptStep()) return;
                m[0xC013] = 2;
                sub++;
                break;
            case 3: // $116E
                if (!WaitSeconds()) return;
                Text(0x5429, 4);
                m[0xC087] = 2;
                m[0xC013] = 0x0A;
                QueuePalette(3);
                sub++;
                break;
            case 4: // $118A
                if ((m[0xC010] & 3) == 0) QueuePalette(2);
                if (!WaitSeconds()) return;
                QueuePalette(0x82);
                m[0xC088] = 0;
                sub++;   // aqui seguiria la demo ($11B4); no se modela
                break;
            default:
                break;
        }
    }

    // $1125: copia los bloques de $5429[1] (cnt, attr, dir LE, tiles) a la pagina FG (dir | $0800).
    void CopyScriptToFg()
    {
        int hl = Table(0x5429, 1);
        while (m[hl] != 0)
        {
            int b = m[hl], c = m[hl + 1], de = W(hl + 2) | 0x0800;
            hl += 4;
            for (int i = 0; i < b; i++) { m[de++] = m[hl++]; m[de++] = (byte)c; }
        }
    }

    // $0C15: byte!=0 => tile (con atributo c); 00 n => saltar n celdas; 00 00 => fin.
    void DrawPacked(int de, int hl, byte c)
    {
        while (true)
        {
            byte a = m[de++];
            if (a != 0) { m[hl++] = a; m[hl++] = c; continue; }
            a = m[de++];
            if (a == 0) return;
            hl += 2 * a;
        }
    }

    // Manejadores de bloque de la tabla $0C0B. Devuelve el puntero tras el bloque.
    int Block(int mode, int hl)
    {
        switch (mode & 7)
        {
            case 0: { int b = m[hl], c = m[hl + 1], de = W(hl + 2); hl += 4;          // $0B55
                      for (int i = 0; i < b; i++) { m[de++] = m[hl++]; m[de++] = (byte)c; } return hl; }
            case 1: { int b = m[hl], de = W(hl + 1); hl += 3;                         // $0B67
                      for (int i = 0; i < b; i++) { m[de] = m[hl++]; de += 2; } return hl; }
            case 2: { int b = m[hl], c = m[hl + 1], de = W(hl + 2); hl += 4;          // $0B13
                      for (int i = 0; i < b; i++) { m[de] = m[hl++]; m[de + 1] = (byte)c; de += 0x40; } return hl; }
            case 3: { int b = m[hl], a = m[hl + 1], c = m[hl + 2], de = W(hl + 3);   // $0B42
                      for (int i = 0; i < b; i++) { m[de++] = (byte)a; m[de++] = (byte)c; } return hl + 5; }
            case 4: { int b = m[hl], a = m[hl + 1], c = m[hl + 2], de = W(hl + 3);   // $0B2B
                      for (int i = 0; i < b; i++) { m[de] = (byte)a; m[de + 1] = (byte)c; de += 0x40; } return hl + 5; }
            default: throw new NotSupportedException($"modo de bloque {mode} en ${hl:X4}");
        }
    }

    // $0B7A/$0B75: interprete de textos. 00 0 = fin/retorno, 00 1 w = subrutina,
    // 00 2 w = salto, 00 3 n = modo de bloque.
    void Text(int table, int idx)
    {
        int mode = 0, ret = 0;
        int hl = Table(table, idx);
        while (true)
        {
            if (m[hl] != 0) { hl = Block(mode, hl); continue; }
            byte cmd = m[hl + 1]; int arg = hl + 2;
            switch (cmd)
            {
                case 0: if (ret == 0) return; hl = ret; ret = 0; break;
                case 1: ret = arg + 2; hl = W(arg); break;
                case 2: hl = W(arg); break;
                case 3: mode = m[arg]; hl = arg + 1; break;
                default: throw new NotSupportedException($"orden de texto {cmd} en ${hl:X4}");
            }
        }
    }

    // $0C2E: sprites/paleta de la tabla $5E01.
    void SpriteSet(int idx)
    {
        Clear(0x10);
        m[0xC0BD] = 1;
        int hl = Table(0x5E01, idx);
        while (m[hl] != 0xFF)
        {
            byte b = m[hl];
            if ((b & 0xF0) != 0xF0) QueuePalette(b);
            int de = W(hl + 1);
            Array.Copy(m, hl + 3, m, de, 8);
            hl += 11;
        }
    }

    // $0C5B: guion progresivo $5429[a]; ix=$C0B5: [0,1] puntero, [2] velocidad, [3] acumulador,
    // [4] bloques, [5] modo, [6] pixeles, [7] contador de bloques.
    void ScriptStart(int idx, byte d, byte e)
    {
        Array.Clear(m, 0xC0B5, 8);
        m[0xC0B7] = d; m[0xC0B9] = e;
        int hl = Table(0x5429, idx);
        if (m[hl] == 0) { m[0xC0BA] = m[hl + 2]; hl += 3; }
        SetW(0xC0B5, hl);
    }

    // $0C87: true cuando termina (ret normal), false = sigue (inc sp/inc sp/ret).
    bool ScriptStep()
    {
        const int ix = 0xC0B5;
        byte acc = (byte)(m[ix + 2] + m[ix + 3]);
        m[ix + 3] = acc;
        int d = 0; byte a = acc;
        if ((a & 0x80) != 0) { a = (byte)-a; d = 0xFF; }
        if ((a & 0xF0) == 0) return false;
        int e = (sbyte)(m[ix + 3] & 0xF0) >> 4;
        if (m[ix + 5] == 0) m[0xC002] = (byte)(m[0xC002] + e);
        else SetW(0xC000, W(0xC000) + (d << 8 | (e & 0xFF)));
        m[ix + 3] &= 0x0F;
        a = (byte)(e < 0 ? -e : e);
        a = (byte)(a + m[ix + 6]);
        byte b = a;
        if ((a & 0xF8) != 0)
        {
            m[ix + 7] = (byte)(m[ix + 7] + (a >> 3));
            if (m[ix + 7] >= m[ix + 4]) return true;
            int hl = W(ix);
            if (m[hl] != 0) hl = Block(m[ix + 5], hl);  // $0CFD
            SetW(ix, hl);
        }
        m[ix + 6] = (byte)(b & 7);
        return false;
    }
}
