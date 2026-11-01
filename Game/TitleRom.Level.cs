namespace WonderBoy.Game;

// Inicio de nivel de la tabla $4F20: $1388 (limpieza, paleta, marcador y vidas).
//   $0A73(0)   rellena con 0 la region de la entrada 0 de $5743 (direccion, numero de bytes).
//   $0A24($10) borra $C800-$CBFF y pone $C0BE=1.
//   $074C      paleta: 32 colores desde $5995 indexados por $5975 -> $D800.
//   $0736(8)   encola paleta.
//   marcador   $C016 -= $C083+1 en BCD (solo si $C02F=0; $C3C4 = $C083+1).
//   $13BE      copia 3 bytes empaquetados de $C102 a $C3C0.
public sealed partial class TitleRom
{
    void LevelStartState() // $1388
    {
        FillRegion(0);                                 // $0A73(0)
        Clear(0x10);                                   // $0A24($10)
        PaletteLoad();                                 // $074C
        QueuePalette(0x08);                            // $0736($08)

        int b = m[0xC083] + 1;                         // $1399
        m[0xC3C4] = (byte)b;
        if (m[0xC02F] == 0)
        {
            int r = (Bcd(m[0xC016]) - b + 100) % 100;  // SUB + DAA
            m[0xC016] = (byte)(((r / 10) << 4) | (r % 10));
        }
        m[0xC01A] = 0; m[0xC01D] = 0;
        PackC102();                                    // $13BE
        m[0xC032] = 0;                                 // $13B9
    }

    // $13D9: despacho por subestado $C07F (tabla $13DF: 0 $13E7, 1 $146D, 2 $147A, 3 $14B2).
    void LevelSubState()
    {
        switch (m[0xC07F])
        {
            case 0: LevelSub0(); break;                // $13E7
            case 1:                                    // $146D
                FillRegion(2);                         // $0A73(2)
                StripeInit();                          // $1546
                m[0xC07F]++;
                break;
            case 2:                                    // $147A
                if (!Stripe()) return;                 // $1556 aborta el manejador hasta C3DF=0
                Clear(0x10);                           // $0A24($10)
                m[0xC008] |= 0x10;
                if (m[0xC026] == 0) m[0xC003] = (byte)(m[0xC3C3] ^ 1);
                SetW(0xC3C7, m[0xC3C3] == 0 ? 0xE844 : 0xE86E);
                PlayerPlace();                         // $179B
                m[0xC3DF] = 6;
                m[0xC07F]++;
                break;
            case 3:                                    // $14B2: cuenta atras de 6 fotogramas
                if (--m[0xC3DF] != 0) return;
                m[0xC013] = 2;
                m[0xC032] = 0; m[0xC07F] = 0;
                m[0xC07E]++;
                break;
            default: Unported = $"$13D9/{m[0xC07F]}"; break;
        }
    }

    // $13E7: fondo de la tabla, texto del marcador y copia de datos del jugador ($C300).
    void LevelSub0()
    {
        FillRegion(1);                                 // $0A73(1)
        int src = 0xC340, b = 6;
        if (m[0xC3C3] != 0) { src = 0xC380; b = 7; }
        for (int i = 0; i < 0x40; i++) m[0xC300 + i] = m[src + i];   // ldir
        Text(TextTable, b);                            // $0B7A
        if (m[0xC30A] == 0)
        {
            m[0xC30A] = 1;                             // (ix+$0A)
            m[0xC302] = m[0xC027];                     // (ix+$02)
            m[0xC300] = 0;                             // (ix+$00)
            m[0xC307] = m[0xC028];                     // (ix+$07)
            SetW(0xC30B, 0x0100);
        }
        m[0xC07F]++;                                   // $142C
    }

    // $1546: puntero del relleno $C3E1=$E880, contador $C3DF=$20 y $C471=0.
    void StripeInit()
    {
        SetW(0xC3E1, 0xE880);
        m[0xC3DF] = 0x20;
        m[0xC471] = 0;
    }

    // $1556: escribe 26 pares en el puntero $C3E1 (paso $40), avanza el puntero 2 y cuenta C3DF.
    // true = C3DF llego a 0 (el manejador continua); false = el manejador se aborta.
    bool Stripe()
    {
        int p = W(0xC3E1);
        for (int i = 0; i < 0x1A; i++) { m[p & 0xFFFF] = 1; m[(p + 1) & 0xFFFF] = 8; p += 0x40; }
        SetW(0xC3E1, W(0xC3E1) + 2);
        m[0xC3DF]--;
        return m[0xC3DF] == 0;
    }

    // $179B: coloca el objeto jugador ($C840) en X = $C30B-$40 y lanza su script.
    void PlayerPlace() => PlayerPlaceAt(0x40);

    // $179B/$17D2: mismo codigo salvo el desplazamiento X (de = $FFC0 / $FF40); script por $1FA3.
    void PlayerPlaceAt(int dx)
    {
        SetW(0xC473, 0xFFFF);
        m[0xC407] &= 0xFE;
        SetW(0xC850, (W(0xC30B) - dx) & 0xFFFF);
        m[0xC858] = 0x30; m[0xC854] = 0x10; m[0xC85F] = 0x02;
        ObjectRoutines.SetScript(m, 0xC840, m[0xC408] == 0 ? 0 : 0x0D);
    }

    static int Bcd(int v) => (v >> 4) * 10 + (v & 0x0F);

    // $0A73: entrada A de $5743 (4 bytes: direccion, numero de bytes), relleno con 0.
    void FillRegion(int a)
    {
        int e = 0x5743 + 4 * a;
        int addr = W(e), n = W(e + 2);
        for (int i = 0; i < n; i++) m[(addr + i) & 0xFFFF] = 0;
    }

    // $074C: 32 entradas de 16 bytes en $D800, indice de $5975 y datos de $5995 (16 bytes por paleta).
    void PaletteLoad()
    {
        for (int i = 0; i < 0x20; i++)
        {
            int src = 0x5995 + 0x10 * m[0x5975 + i];
            for (int j = 0; j < 0x10; j++) m[0xD800 + 0x10 * i + j] = m[src + j];
        }
    }

    // $13BE: 3 bytes en $C3C0 con nibbles bajos de pares de $C102.
    void PackC102()
    {
        int hl = 0xC102, de = 0xC3C0;
        for (int i = 0; i < 3; i++)
        {
            int hi = (m[hl++] & 0x0F) << 4;
            int lo = m[hl++] & 0x0F;
            m[de++] = (byte)(hi | lo);
        }
    }
}
