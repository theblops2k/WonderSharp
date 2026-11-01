namespace WonderBoy.Game;

// Presentacion de ronda de la tabla de juego $4F20 ($C07E=6): $1651, despacho por $C07F con la
// tabla $1657 (bytes 5F 16 96 16 C9 16 F9 16): 0 $165F, 1 $1696, 2 $16C9, 3 $16F9.
//   $165F  area/ronda ($C300=$C301>>2, $C310=$C301&3), $1431 (32 pasadas de $22A2), $14C9 (vidas
//          $14D0 + vitalidad $1809/$4E74), $172D (camara y tablas de nivel), $1711 (paleta 9 y
//          parches $582D), $1818 (punteros de objetos $C401-$C406), $187F ($C47C-$C47F, $C441),
//          $21BC (un paso de juego sin $274D), $1579 (telon), $C008 bit4=0.
//   $1696  telon $1585 durante $1C fotogramas, texto $0B, digitos de area y ronda, $C471=$80.
//   $16C9  el jugador ($C840) entra andando hasta quedar a menos de $40 px de $C442 y espera $0AA6.
//   $16F9  $09DB, $C3CC=$A211, $C3CA=$C400=0, $C07F=0 y $C032=0 (el planificador pasa a $18B7).
// Tiempos: $1431 tarda 4 fotogramas (10+10+10+2 pasadas, medido en ram_rand f222-f225; el resto de
// $165F y la primera llamada de $1696 caben en f225). En f222-f224 el volcado corta una pasada a
// medias ($C456/$C458/$C45E), el port no. Sonido $0440 ($CC/$CD) en TitleRom.Sound.
// Sin verificar (VRAM fuera de ram_rand): las celdas de $14D0, $4E74, $1585 y $09DB.
// La cola $2C39 de $2BD5 ($C479=1) esta en TitleRom.Round.End.cs (sin verificar).
public sealed partial class TitleRom
{
    // Pasadas de $22A2 por fotograma en $1431 (ram_rand: $C3DF = $16, $0C, $02 en f222-f224).
    const int LevelPassesPerFrame = 10;

    void RoundState() // $1651
    {
        switch (m[0xC07F])
        {
            case 0: RoundSub0(); break;                // $165F
            case 1:                                    // $1696
                if (!Curtain()) return;                // $1585 aborta el manejador hasta el ultimo paso
                Text(TextTable, 0x0B);                 // $0B7A($0B)
                Digit(m[0xC300] + 1, 0xEA5E);          // $14FA area
                Digit(m[0xC310] + 1, 0xEBDE);          // $14FA ronda
                Sound(m, m[0xC310] < 0x03 ? 0xCC : 0xCD);  // $16B2-$16BC $0440
                m[0xC471] = 0x80;
                m[0xC07F]++;
                break;
            case 2:                                    // $16C9
                ObjectRoutines.Animate(m, 0xC840);     // $1FC0
                Enemies.MoveX(m, 0xC840);              // $2725
                SetW(0xC852, ObjectRoutines.GroundY(m, 0xC840)); // $293A -> (ix+$12)
                WorldToScreen.Update(m);               // $239E
                if (((W(0xC442) - W(0xC850)) & 0xFF) >= 0x40) return; // $16DF-$16EC (solo L)
                m[0xC854] = 0;
                if (!WaitSeconds()) return;            // $0AA6
                m[0xC07F]++;
                break;
            case 3:                                    // $16F9
                FillRows(0xE986, 0); FillRows(0xE987, 0); // $09DB
                SetW(0xC3CC, 0xA211);
                m[0xC3CA] = 0; m[0xC400] = 0;
                m[0xC07F] = 0; m[0xC032] = 0;
                break;
            default: Unported = $"$1651/{m[0xC07F]}"; break;
        }
    }

    // $165F: el bucle de $1431 se reparte en 4 fotogramas; el resto corre en el ultimo.
    void RoundSub0()
    {
        int a = m[0xC301];
        m[0xC310] = (byte)(a & 3);
        m[0xC300] = (byte)(a >> 2);
        m[0xC313] = 0;
        LevelPassSetup();                              // $1431
        if (LevelPasses(LevelPassesPerFrame)) { RoundSub0End(); return; }
        void Next()
        {
            if (LevelPasses(LevelPassesPerFrame)) { RoundSub0End(); return; }
            pending.Enqueue(Next);
        }
        pending.Enqueue(Next);
    }

    // $1677-$1695: lo que sigue a $1431 en $165F.
    void RoundSub0End()
    {
        Lives();                                       // $14C9: $14D0
        VitalityInit();                                //        $1809
        LevelCamera();                                 // $172D
        LevelPatches();                                // $1711
        ObjectPointers();                              // $1818
        RoundParams();                                 // $187F
        GameStep();                                    // $21BC
        SetW(0xC3E1, 0xE93A); m[0xC3DF] = 0x1C;        // $1579
        m[0xC008] &= 0xEF;
        m[0xC07F]++;
    }

    // $14D0: iconos de vidas ($C302-1, max 6) en $E884, 2x2 celdas FC FD / FE FF (sin atributo).
    void Lives() => Lives(m);

    internal static void Lives(byte[] m)
    {
        int a = (m[0xC302] - 1) & 0x0F;
        if (a == 0) return;
        if (a >= 7) a = 6;
        for (int i = 0, ix = 0xE884; i < a; i++, ix += 4)
        {
            m[ix] = 0xFC; m[ix + 2] = 0xFD; m[ix + 0x40] = 0xFE; m[ix + 0x42] = 0xFF;
        }
    }

    // $1809: vitalidad inicial ($C469=$18FF, $C46C=$96) y barra $4E74.
    void VitalityInit()
    {
        SetW(0xC469, 0x18FF);
        m[0xC46C] = 0x96;
        VitalityBar();                                 // $4E74
    }

    // $4E74: barra de vitalidad de 16 celdas en $E89C (fila alta d, fila baja e en +$40); $C46B=$C46A.
    // Hasta 8 la primera mitad (F0F2 lleno de 2, F1F3 medio, F4F6 vacio) y la segunda vacia (F9FB);
    // desde 8 la primera llena y la segunda con F5F7 / F8FA / F9FB.
    void VitalityBar()
    {
        int a = m[0xC46A];
        m[0xC46B] = (byte)a;
        int ix = 0xE89C;
        void Put(int de) { m[ix] = (byte)(de >> 8); m[ix + 0x40] = (byte)de; ix += 2; }
        int Cell(int de, int half, int empty)
        {
            if (a >= 2) { a -= 2; return de; }
            if (a >= 1) { a--; return half; }
            return empty;
        }
        int de;
        if (a < 8)
        {
            de = 0xF0F2;
            for (int i = 0; i < 4; i++) { de = Cell(de, 0xF1F3, 0xF4F6); Put(de); }   // $4E99 ld de,$F4F6
            for (int i = 0; i < 12; i++) Put(0xF9FB);
            return;
        }
        for (int i = 0; i < 4; i++) Put(0xF0F2);
        a -= 8;
        de = 0xF5F7;
        for (int i = 0; i < 12; i++) { de = Cell(de, 0xF8FA, 0xF9FB); Put(de); }
    }

    // $172D: camara $C442=$C30B, tablas de la ronda ($A380 -> $C30D, $A300 -> $C44A, $A3C0 -> $C44C,
    // $A340 -> $C44E), altura $C444 = $C30D + 32*suma de pendientes de las $C443 primeras columnas,
    // $C002 = -L y $C446 = $30.
    void LevelCamera()
    {
        SetW(0xC442, W(0xC30B));
        int r = m[0xC301];
        SetW(0xC30D, Table(0xA380, r));
        SetW(0xC44A, Table(0xA300, r));
        SetW(0xC44C, Table(0xA3C0, r));
        int de = Table(0xA340, r);
        SetW(0xC44E, de);
        int l = 0, h = 0, b = m[0xC443];
        do                                             // djnz: B=0 da 256 vueltas
        {
            int a = m[de & 0xFFFF] & 3;
            de++;
            if (a < 3) l = (a == 1 ? l + 1 : l - 1) & 0xFF;
            b = (b - 1) & 0xFF;
        } while (b != 0);
        if ((l & 0x80) != 0) h = 0xFF;                 // dec h
        int hl = ((((h << 8) | l) << 5) + W(0xC30D)) & 0xFFFF;
        SetW(0xC444, hl);
        m[0xC002] = (byte)-(hl & 0xFF);
        m[0xC446] = 0x30;
    }

    // $1711: encola la paleta 9 y aplica los parches (direccion, valor) de $582D[$C301] hasta $FF.
    void LevelPatches()
    {
        QueuePalette(0x09);                            // $0736
        int hl = Table(0x582D, m[0xC301]);
        while (m[hl & 0xFFFF] != 0xFF)
        {
            int de = m[hl & 0xFFFF] | m[(hl + 1) & 0xFFFF] << 8;
            m[de & 0xFFFF] = m[(hl + 2) & 0xFFFF];
            hl += 3;
        }
    }

    // $1818: 3 punteros de lista de objetos ($A420 + 6*$C301) en $C401/$C403/$C405, avanzados con
    // $184E hasta la primera entrada con X >= $C30B. Usa $C3DF como contador (queda a 0).
    void ObjectPointers()
    {
        int hl = 0xA420 + 6 * m[0xC301];
        m[0xC3DF] = 3;
        int ix = 0xC401, bc = W(0xC30B);
        do
        {
            SetW(ix, W(hl)); hl += 2;
            SkipObjects(ix, bc);                       // $184E
            ix += 2;
        } while (--m[0xC3DF] != 0);
    }

    // $184E: salta las entradas (2, 3 o 4 bytes segun tipo < $38, < $3C o mayor) con X < bc.
    void SkipObjects(int ix, int bc)
    {
        while (true)
        {
            int p = W(ix);
            int e = m[p & 0xFFFF], a = e & 0x3F;
            if (a == 0) return;                        // fin de lista
            int d = m[(p + 1) & 0xFFFF];
            p += 2;
            int x = ((d << 8) | (e & 0xC0)) >> 2;
            if (x >= bc) return;                       // sbc hl,bc; ret nc
            if (a >= 0x38) { p++; if (a >= 0x3C) p++; }
            SetW(ix, p);
        }
    }

    // $187F: parametros del area ($5EFF + 4*$C300): $C47C, $C47D, $C47E (con $C02C: c/2+1), $C47F;
    // $C441 = $C02C salvo en attract ($C005 != 0).
    void RoundParams()
    {
        int hl = 0x5EFF + ((m[0xC300] * 4) & 0xFF);
        m[0xC47C] = m[hl]; m[0xC47D] = m[hl + 1];
        int c = m[hl + 2], b = m[hl + 3];
        m[0xC441] = m[0xC02C];
        if (m[0xC02C] != 0) c = (c >> 1) + 1;
        m[0xC47E] = (byte)c; m[0xC47F] = (byte)b;
        if (m[0xC005] != 0) m[0xC441] = 0;
    }

    // $21BC: paso de juego sin el jugador ($274D): $4CD2, $2BD5, $24EB, $21CF, $2297, $239E.
    void GameStep()
    {
        Weapon.Update(m);                              // $4CD2
        EnemySlots();                                 // $2BD5
        if (Unported != null) return;
        SlotUpdate.Update(m);                          // $24EB
        Camera.Update(m);                              // $21CF
        TilemapFill.Update(m);                         // $2297
        WorldToScreen.Update(m);                       // $239E
    }

    // $2BD5: 12 ranuras de enemigo desde $C8A0 ($C3DF = $0C, $C46F = 0). IX+$18 bit7 o (0 y $C479 = 0)
    // -> $2CA5 (ObjectSpawn.Spawn); tipo activo -> $2D9B (Enemies.Dispatch; con $C477 solo la ranura
    // $C478) e IX+$18 = 0 si IX+$1F queda a 0. Con $C479 = 1 y todas las ranuras libres sigue $2C39.
    void EnemySlots()
    {
        int ix = 0xC8A0;
        m[0xC3DF] = 0x0C;
        m[0xC46F] = 0;
        do
        {
            int t = m[ix + 0x18];
            if ((t & 0x80) != 0 || (t == 0 && m[0xC479] == 0)) ObjectSpawn.Spawn(m, ix); // $2CA5
            else if (t != 0 && (m[0xC477] == 0 || m[0xC478] == m[0xC3DF]))
            {
                int miss = Enemies.Dispatch(m, ix);    // $2D9B
                if (miss != 0) { Unported = $"${miss:X4}"; return; }
                if (m[ix + 0x1F] == 0) m[ix + 0x18] = 0;
            }
            ix += 0x20;
        } while (--m[0xC3DF] != 0);
        if (m[0xC479] != 1) return;                    // $2C26
        for (int i = 0; i < 12; i++) if (m[0xC8BF + 0x20 * i] != 0) return;
        RoundGoalSpawn();                              // $2C39 (TitleRom.Round.End.cs, sin verificar)
    }

    // $1585: telon de la presentacion. Columna de $17 celdas en $C3E1 (paso $40): marco superior E4 en
    // el primer paso, en los pasos $0A..$01 las dos letras de $56D9 (3 bytes: color, tile, tile) para
    // c = $22, $1C-$1E (+3 sin $C407 bit1) o $56A9[6*$C300 + paso-3]; marco E1/E3 en el ultimo y E6
    // al pie del primero. $C3E1 -= 2 y $C3DF--. true = ultimo paso (el manejador sigue);
    // false = inc sp/inc sp/ret, el manejador se aborta.
    bool Curtain()
    {
        int hl = W(0xC3E1), b = 0x17;
        void Put(int t, int c) { m[hl & 0xFFFF] = (byte)t; m[(hl + 1) & 0xFFFF] = (byte)c; hl += 0x40; }
        int a = m[0xC3DF];
        if (a == 0x1C) { Put(0xE4, 0x08); b--; }
        else if (--a < 0x0A)
        {
            int c;
            if (a == 9) c = 0x22;
            else if (a < 3)
            {
                c = a + 0x1C;
                if ((m[0xC407] & 0x02) == 0) c += 3;
            }
            else c = m[0x56A9 + ((m[0xC300] * 6) & 0xFF) + (a - 3)];
            int p = 0x56D9 + ((c * 3) & 0xFF);
            int attr = m[p];
            Put(m[p + 1], attr);
            Put(m[p + 2], attr);
            b -= 2;
            if (m[0xC3DF] == 1) { Put(0xE1, 0x08); b--; }
        }
        for (int i = 0; i < b; i++) Put(0, 0);         // $1600
        int n = m[0xC3DF];
        m[0xC3DF] = (byte)(n - 1);
        if (n - 1 == 0) Put(0xE3, 0x08);
        else if (n - 1 == 0x1B) Put(0xE6, 0x08);
        else Put(0, 0);
        SetW(0xC3E1, W(0xC3E1) - 2);
        return n - 1 == 0;
    }
}
