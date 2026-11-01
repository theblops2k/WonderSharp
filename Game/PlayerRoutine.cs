namespace WonderBoy.Game;

using static ObjectRoutines;

// Jugador $274D (IX=$C840), traduccion 1:1 sobre el espacio de la CPU (64 KB). $2766 salta por la
// tabla de ROM $2779 con (IX+$1F) - 1 & 7 ($0ABB): $2787 (1 muerte), $2889 (2 suelo), $2991 (3 sube),
// $2A15 (4 cae), $2AA7 (5 rebote), $2B2B (6), $2B3D (7). Despues $2B4F (hacha), $C466 = 0 y, si el
// estado queda en 0, $C400 = 1.
// Subrutinas: $252B/$2532/$2593/$2640 (velocidad X y animacion), $2631 (+$1FA3), $26FA/$2725,
// $297B/$2957 (pared), $28FB/$28DF (suelo, plataformas $C466/$C467), $287B (pozo), $2817, $2831,
// $293A, $43B2, $1FC0, $0440 (sonido), $4EF4 (vitalidad), $2D1A (cola de paleta).
// Entradas: $C084 (lo escribe $00A4 antes de $2193) y $C086. No toca VRAM ni sprite RAM.
// Estado 0 (indice 7) salta a la palabra en $2787 ($CBDD, RAM): no se ejecuta, Update la devuelve.
public static class PlayerRoutine
{
    public const int IX = 0xC840;
    public const int Table = 0x2779;

    // Direccion del primer destino no portado alcanzado en la ultima llamada (0 = ninguno)
    static int missing;

    // $274D. Devuelve 0 si todo el camino esta portado; si no, la direccion que falta.
    public static int Update(byte[] m)
    {
        missing = 0;
        int ix = IX;
        H2766(m, ix);
        if (missing != 0) return missing;
        H2B4F(m, ix);                                                // $2754
        m[0xC466] = 0x00;                                            // $2757
        if (m[ix + 0x1F] == 0) m[0xC400] = 0x01;                     // $275B
        return 0;
    }

    // $2766
    static void H2766(byte[] m, int ix)
    {
        if (m[0xC440] != 0) m[ix + 0x14] = 0x20;                     // $276C
        int h = Word(m, Table, (m[ix + 0x1F] - 1) & 0x07);           // $2770 $0ABB
        switch (h)
        {
            case 0x2787: H2787(m, ix); break;
            case 0x2889: H2889(m, ix); break;
            case 0x2991: H2991(m, ix); break;
            case 0x2A15: H2A15(m, ix); break;
            case 0x2AA7: H2AA7(m, ix); break;
            case 0x2B2B: H2B2B(m, ix, 0x0011); break;
            case 0x2B3D: H2B2B(m, ix, 0x0012); break;
            default: missing = h; break;
        }
    }

    // ---------------- estados ----------------

    // $2787: estado 1 (muerte)
    static void H2787(byte[] m, int ix)
    {
        if ((m[ix + 0x1F] & 0x80) == 0)
        {
            m[ix + 0x1F] = 0x81;                                     // $278D
            TitleRom.Sound(m, 0x11);
            TitleRom.Sound(m, 0x02);
            Ww(m, ix + 0x16, 0x5EB2);                                // $279B tabla de rebote
            m[ix + 0x14] = 0x00;
            Anim(m, ix, m[ix + 0x19] != 0 ? 0x0003 : 0x0002);        // $27A8
            m[ix + 0x19] = 0x01;
        }
        if ((m[ix + 0x1F] & 0x10) != 0) { H2831(m, ix); return; }    // $27BB
        if (--m[ix + 0x19] == 0)                                     // $27C1
        {
            int p = Rw(m, ix + 0x16);
            int e = m[p], d = m[(p + 1) & 0xFFFF];
            p = (p + 2) & 0xFFFF;
            if (m[p] != 0) Ww(m, ix + 0x16, p);                      // $27D1
            m[ix + 0x19] = (byte)e;
            m[ix + 0x1A] = (byte)d;
        }
        int y = (Rw(m, ix + 0x12) + (sbyte)m[ix + 0x1A]) & 0xFFFF;   // $27E1
        Ww(m, ix + 0x12, y);
        Animate(m, ix);                                              // $27F9
        int g = (GroundY(m, ix) + 0xFFC0) & 0xFFFF;                  // $27FC $293A
        if (g < y) return;                                           // $2807
        if (ScreenBounds.OnScreen(m, ix)) return;                      // $2808
        if (Pit(m, ix)) H2817(m, ix);                                // $280D
        else m[ix + 0x1F] = 0x00;
    }

    // $2817: caida al pozo
    static void H2817(byte[] m, int ix)
    {
        Anim(m, ix, 0x021D);
        Animate(m, ix);
        m[ix + 0x16] = 0x00;
        m[ix + 0x17] = 0x70;
        m[ix + 0x1F] = 0x91;
        ObjectSpawn.Enqueue(m, ix, 0x38);                            // $282E jp $2D1A
    }

    // $2831: estado 1 con bit4 (sube y cae fuera de pantalla)
    static void H2831(byte[] m, int ix)
    {
        Animate(m, ix);
        Enemies.MoveY(m, ix);                                        // $2834 $43B2
        if ((m[ix + 0x1F] & 0x20) == 0)                              // $2837
        {
            if (!ScreenBounds.OnScreen(m, ix)) return;
            m[ix + 0x1F] |= 0x20;
            return;
        }
        if ((m[ix + 0x1F] & 0x40) == 0)                              // $2847
        {
            int v = (m[ix + 0x17] - 3) & 0xFF;
            m[ix + 0x17] = (byte)v;
            if ((v & 0x80) == 0) return;                             // $2855 ret p
            Anim(m, ix, 0x021E);
            m[ix + 0x1F] |= 0x40;
            return;
        }
        if (!ScreenBounds.OnScreen(m, ix)) { m[ix + 0x1F] = 0x00; return; } // $2861
        int a = (m[ix + 0x17] - 3) & 0xFF;                           // $286C
        if (a < 0xB1) a = 0xB0;
        m[ix + 0x17] = (byte)a;
    }

    // $2889: estado 2 (suelo)
    static void H2889(byte[] m, int ix)
    {
        m[ix + 0x1F] |= 0x80;
        Animate(m, ix);                                              // $2893
        Move(m, ix);
        if ((m[ix + 0x1E] & 0x04) != 0)                              // $289F golpeado
        {
            m[ix + 0x1E] &= 0xFB;
            m[ix + 0x1F] = 0x01;
            return;
        }
        if ((m[ix + 0x1E] & 0x08) != 0)                              // $28AE rebote
        {
            m[ix + 0x1E] &= 0xF7;
            m[ix + 0x1F] = 0x05;
            return;
        }
        if ((m[0xC084] & 0x20) != 0)                                 // $28BD salto pendiente
        {
            m[0xC084] &= 0xDF;
            m[ix + 0x1F] = 0x03;
            return;
        }
        int de = Floor(m, ix);                                       // $28CB
        if (Far(m, ix, de)) { m[ix + 0x1F] = 0x04; return; }        // $28CE
        Ww(m, ix + 0x12, de);                                        // $28D8
    }

    // $2991: estado 3 (sube)
    static void H2991(byte[] m, int ix)
    {
        if ((m[ix + 0x1F] & 0x80) == 0)
        {
            m[ix + 0x1F] |= 0x80;                                    // $2997
            TitleRom.Sound(m, 0x16);
            int hl = m[0xC408] != 0 ? 0x000F : ((m[ix + 0x1E] & 0x10) != 0 ? 0x000B : 0x000A);
            if ((m[ix + 0x1E] & 0x80) == 0) Anim(m, ix, hl);         // $29B2
            int mask = m[0xC408] != 0 ? 0x42 : 0xC2;                 // $29B9-$29DE
            m[ix + 0x17] = (byte)((m[0xC086] & mask) != 0 ? 0x55 : 0x40);
            m[ix + 0x16] = 0x00;
        }
        Animate(m, ix);                                              // $29E7
        Move(m, ix);
        if ((m[ix + 0x1E] & 0x04) != 0) { m[ix + 0x1F] = 0x01; return; } // $29F3
        Enemies.MoveY(m, ix);                                        // $29FE
        if (m[ix + 0x17] < 0x04)                                     // $2A04 sub $04, jr c
        {
            m[ix + 0x17] = 0x00;
            m[ix + 0x1F] = 0x84;
            return;
        }
        m[ix + 0x17] -= 0x04;
    }

    // $2A15: estado 4 (cae)
    static void H2A15(byte[] m, int ix)
    {
        if ((m[ix + 0x1F] & 0x80) == 0)
        {
            m[ix + 0x1F] |= 0x80;                                    // $2A1B
            m[ix + 0x16] = 0x00;
            m[ix + 0x17] = 0x00;
        }
        Animate(m, ix);                                              // $2A27
        Move(m, ix);
        if ((m[ix + 0x1E] & 0x04) != 0) { m[ix + 0x1F] = 0x01; return; } // $2A33
        Enemies.MoveY(m, ix);                                        // $2A3E
        int a = (m[ix + 0x17] - 4) & 0xFF;                           // $2A41
        if ((a & 0x80) == 0 || a >= 0xA1) m[ix + 0x17] = (byte)a;    // tope de caida $A1..
        int y = Rw(m, ix + 0x12);
        int top = (Rw(m, 0xC444) - 0x100) & 0xFFFF;                  // $2A51
        if (top >= y)                                                // $2A5E fuera por abajo
        {
            if (Pit(m, ix)) H2817(m, ix);                            // $2A60
            else m[ix + 0x1F] = 0x00;
            return;
        }
        int de = Floor(m, ix);                                       // $2A6B
        if (y >= de) return;                                         // $2A77
        int diff = (y - de) & 0xFFFF;
        if ((diff >> 8) != 0xFF) return;                             // $2A78
        if ((diff & 0xFF) < 0xF0) return;                            // $2A7A
        Ww(m, ix + 0x12, de);                                        // $2A7E aterriza
        m[ix + 0x1E] &= 0xF7;
        int c = (((sbyte)m[ix + 0x17] >> 4) - 2) & 0xFF;             // $2A88
        if ((diff & 0xFF) >= c) { m[ix + 0x1F] = 0x02; return; }    // $2A95
        if (m[0xC466] != 0) return;                                  // $2A9D
        m[ix + 0x1F] = 0x05;
    }

    // $2AA7: estado 5 (rebote)
    static void H2AA7(byte[] m, int ix)
    {
        if ((m[ix + 0x1F] & 0x80) == 0)
        {
            m[ix + 0x1F] |= 0x80;                                    // $2AAD
            TitleRom.Sound(m, 0x0B);
            m[0xC408] = 0x00;
            m[0xC463] = 0x00;
            Enemies.Vitality(m, 0xFC00);                             // $2AC0 $4EF4
            Ww(m, ix + 0x16, 0x5EA3);
            m[ix + 0x14] = (byte)(m[ix + 0x18] - 0x14);
            m[ix + 0x1E] &= 0x7F;
            Anim(m, ix, (m[ix + 0x1E] & 0x10) != 0 ? 0x0005 : 0x0004);
        }
        Animate(m, ix);                                              // $2AE6
        Wall(m, ix);                                                 // $2AE9 $297B
        Enemies.MoveX(m, ix);                                        // $2AEC $2725
        if ((m[ix + 0x1E] & 0x04) != 0) { m[ix + 0x1F] = 0x01; return; } // $2AEF
        int p = Rw(m, ix + 0x16);                                    // $2AFA
        int a = m[p];
        if (a == 0xFF)
        {
            a = m[(p - 1) & 0xFFFF];                                 // $2B05
            m[ix + 0x16] = 0x00;
            m[ix + 0x17] = (byte)a;
            m[ix + 0x1F] = 0x84;
            return;
        }
        Ww(m, ix + 0x16, p + 1);                                     // $2B13
        Ww(m, ix + 0x12, Rw(m, ix + 0x12) + a);
    }

    // $2B2B / $2B3D: estados 6 y 7, solo fijan la animacion la primera vez
    static void H2B2B(byte[] m, int ix, int hl)
    {
        if ((m[ix + 0x1F] & 0x80) != 0) return;
        m[ix + 0x1F] |= 0x80;
        Anim(m, ix, hl);
        Animate(m, ix);                                              // jp $1FC0
    }

    // $2B4F: hacha (boton $C084 bit4, $C407 bit1, estados 2-4, $C462 = 0, cooldown IX+$1A)
    static void H2B4F(byte[] m, int ix)
    {
        int st = m[ix + 0x1F] & 0x0F;
        if ((m[0xC084] & 0x10) != 0 && (m[0xC407] & 0x02) != 0 && st >= 0x02 && st < 0x05
            && m[0xC462] == 0 && m[ix + 0x1A] == 0)
        {
            m[ix + 0x1E] |= 0x80;                                    // $2B76
            m[ix + 0x1A] = 0x08;
            int hl = m[0xC408] != 0 ? 0x0010 : ((m[ix + 0x1E] & 0x10) != 0 ? 0x0007 : 0x0006);
            Ww(m, 0xC473, hl);                                       // $2B90
            SetScript(m, ix, hl);                                    // $1FA3
            Animate(m, ix);
            m[0xC463] = 0x01;
            return;
        }
        if (m[ix + 0x1A] != 0) m[ix + 0x1A]--;                       // $2B9F
        if ((m[ix + 0x1E] & 0x80) == 0) return;
        if ((m[ix + 0x1E] & 0x01) == 0) return;                      // animacion terminada
        m[ix + 0x1E] &= 0x7F;
        if ((m[ix + 0x1F] & 0x0F) == 0x02) return;
        // $2BC8: la ROM hace bit 4,(ix+$00) e inc a (no inc l): HL queda en $000A siempre
        Anim(m, ix, m[0xC408] != 0 ? 0x000F : 0x000A);
    }

    // ---------------- subrutinas ----------------

    // $2631: cambia el script de animacion ($C473 = hl, $1FA3) si es distinto del actual
    static void Anim(byte[] m, int ix, int hl)
    {
        if (Rw(m, 0xC473) == hl) return;
        Ww(m, 0xC473, hl);
        SetScript(m, ix, hl);
    }

    // $287B: Z si el nibble alto de ($C44A)[X hi] es $40 (pozo)
    static bool Pit(byte[] m, int ix) =>
        (m[(Rw(m, 0xC44A) + m[ix + 0x11]) & 0xFFFF] & 0xF0) == 0x40;

    // Secuencia comun de los estados 2/3/4: $252B, $297B, $26FA
    static void Move(byte[] m, int ix)
    {
        H252B(m, ix);
        Wall(m, ix);
        H26FA(m, ix);
    }

    // $252B
    static void H252B(byte[] m, int ix)
    {
        if (m[0xC440] != 0) { H2640(m, ix); return; }
        if (m[0xC408] != 0) H2532(m, ix);
        else H2593(m, ix);
    }

    // $2640: fin de nivel, sin entradas
    static void H2640(byte[] m, int ix)
    {
        m[0xC084] = 0x00;
        if ((m[ix + 0x1E] & 0x80) != 0) return;
        if ((m[ix + 0x1F] & 0x0F) != 0x02) return;
        Anim(m, ix, m[0xC408] != 0 ? 0x000D : 0x0000);
    }

    // $2532 (con monopatin, $C408 != 0): velocidad fija sin aceleracion
    static void H2532(byte[] m, int ix)
    {
        int hl = 0x000D;
        int a = m[0xC084] & 0x0C;
        if (a == 0) m[ix + 0x14] = m[ix + 0x18];                     // $2543
        else
        {
            int c = (m[0xC086] & 0x02) != 0 ? 0x08 : 0x00;           // $254C
            if ((a & 0x04) != 0)
            {
                m[ix + 0x1E] &= 0xEF;                                // $255B
                m[ix + 0x14] = (byte)(m[ix + 0x18] + 0x18 + c);
                hl = 0x000C;
            }
            else
            {
                m[ix + 0x1E] |= 0x10;                                // $256C
                m[ix + 0x14] = (byte)(m[ix + 0x18] - 0x18 - c);
                hl = 0x000E;
            }
        }
        if ((m[ix + 0x1E] & 0x80) != 0) return;                      // $257B
        int st = m[ix + 0x1F] & 0x0F;
        if (st == 0x02) { Anim(m, ix, hl); return; }
        if (st < 0x02 || st >= 0x05) return;
        Anim(m, ix, 0x000F);                                         // $258E
    }

    // $2593 (sin monopatin): velocidad objetivo y aceleracion
    static void H2593(byte[] m, int ix)
    {
        int a = m[0xC084] & 0x0C, c, hl;
        if (a == 0)
        {
            c = 0x00;                                                // $259A
            hl = (m[ix + 0x1E] & 0x10) != 0 ? 0x0009 : 0x0008;
        }
        else
        {
            c = m[ix + 0x18];                                        // $25A8
            if ((m[0xC086] & 0x02) != 0)
            {
                c = (c + 0x18) & 0xFF;                               // $25B3 corre
                if (--m[ix + 0x0C] == 0) m[ix + 0x0C] = 0x01;        // anima mas rapido
            }
            m[ix + 0x1E] &= 0xEF;                                    // $25C0
            hl = 0x0000;
            if ((a & 0x04) == 0)
            {
                m[ix + 0x1E] |= 0x10;                                // $25CB izquierda
                c = (-c) & 0xFF;
                hl = 0x0001;
            }
        }
        if ((m[ix + 0x1E] & 0x80) == 0)                              // $25D4
        {
            int st = m[ix + 0x1F] & 0x0F;
            if (st == 0x02) Anim(m, ix, hl);
            else if (st < 0x05) Anim(m, ix, (m[ix + 0x1E] & 0x10) != 0 ? 0x000B : 0x000A); // $25E7
        }
        int step = 0x01;                                             // $25F5
        if ((m[ix + 0x1F] & 0x0F) == 0x02)
        {
            step = 0x03;
            if ((m[(Rw(m, 0xC44A) + m[ix + 0x11]) & 0xFFFF] & 0xF0) == 0x50) step = 0x01; // resbala
        }
        int cc = (c + 0x80) & 0xFF;                                  // $2613
        int v = (m[ix + 0x14] + 0x80) & 0xFF;
        if (v == cc) return;
        if (v < cc)
        {
            v = (v + step) & 0xFF;                                   // $2626
            if (v >= cc) v = cc;
        }
        else
        {
            v = (v - step) & 0xFF;                                   // $2620
            if (v < cc) v = cc;
        }
        m[ix + 0x14] = (byte)(v - 0x80);                             // $262B
    }

    // $26FA: con velocidad negativa no pasa de $C0 px detras de la camara; si no, $2725
    // (WIDESCREEN: tope en ScreenBounds.Left, el mismo margen del HUD; ORIGINAL / NO = la ROM).
    internal static void H26FA(byte[] m, int ix)
    {
        if ((m[ix + 0x14] & 0x80) != 0 && ScreenBounds.PlayerBackLimit(m, ix))
        {
            m[ix + 0x14] = 0x00;
            return;
        }
        bool back = (m[ix + 0x14] & 0x80) != 0;
        Enemies.MoveX(m, ix);
        if (back) ScreenBounds.ClampPlayerLeft(m, ix);
    }

    // $297B: pared delante ($2957: suelo en X +/- $14); si Y + 8 < suelo, IX+$14 = 0
    static void Wall(byte[] m, int ix)
    {
        int de = Enemies.Ahead(m, ix, 0x14);
        if (((Rw(m, ix + 0x12) + 8) & 0xFFFF) < de) m[ix + 0x14] = 0x00;
    }

    // $28FB: suelo = plataforma ($C466/$C467) o max($2660(X-10), $2660(X+10)) + IX+$08/2
    static int Floor(byte[] m, int ix)
    {
        if (m[0xC466] != 0) return Rw(m, 0xC467);
        int x = Rw(m, ix + 0x10);
        int h1 = Enemies.Terrain2660(m, (x - 0x0A) & 0xFFFF);
        Ww(m, 0xC3E3, h1);                                           // $2915
        int h2 = Enemies.Terrain2660(m, (x + 0x0A) & 0xFFFF);
        int de = h1 >= h2 ? h1 : h2;                                 // $2926
        return (de + (m[ix + 0x08] >> 1)) & 0xFFFF;
    }

    // $28DF: carry si |Y - de| >= 10
    static bool Far(byte[] m, int ix, int de)
    {
        int d = (Rw(m, ix + 0x12) - de) & 0xFFFF;
        if ((d & 0x8000) != 0) d = (-d) & 0xFFFF;
        return d >= 0x0A;
    }

    // $00A4: entradas filtradas. a = $C086; actualiza $C085 (bit0 salto, bit1 boton) y $C084.
    public static void ReadInputs(byte[] m)
    {
        int b = m[0xC086];
        int c = (b & 0xF0) >> 4 | (m[0xC084] & 0x20);
        if ((m[0xC085] & 0x01) != 0)
        {
            if ((b & 0x04) == 0) { m[0xC085] &= 0xFE; c &= 0xDF; }   // $00BE
        }
        else if ((b & 0x04) != 0) { m[0xC085] |= 0x01; c |= 0x20; } // $00C8
        if ((b & 0x02) == 0) m[0xC085] &= 0xFD;                      // $00D0
        else if ((m[0xC085] & 0x02) == 0) { m[0xC085] |= 0x02; c |= 0x10; } // $00D8
        m[0xC084] = (byte)c;
    }
}
