namespace WonderBoy.Game;

using static ObjectRoutines;

// Manejadores de enemigo portados a partir de --walk-all (WalkAll.cs), traduccion 1:1 de la ROM
// (docs/analysis/wboy2u_maincpu.asm). SIN VERIFICAR contra volcados de MAME (ram_rand no los alcanza):
//   $3831 (tipo $0F), $410A/$4167 de $40B1 ($07 $08 $22; el despacho esta en Enemies.cs con $40C4),
//   $478A ($20 $21, con $480D, $4790, $4797, $4805), $457B ($1B), $4BE1 ($26 $27),
//   $2E24 ($2C, rey de fin de area), $2FC4 ($2D, cabeza), $30FC ($2E, proyectil), $31E7 (piezas,
//   con $30E1/$3247), $3267 ($28), $32B1 ($29), $45C3 ($1F), $4620 ($1E), $39D7 ($16).
// $2E24 y $30FC usan 'ld a,r' (registro de refresco del Z80, no emulado): Refresh() es una APROXIMACION.
public static partial class Enemies
{
    // $28DF sobre IX: carry si |Y - de| >= $0A
    static bool FarY(byte[] m, int ix, int de)
    {
        int d = (Rw(m, ix + 0x12) - de) & 0xFFFF;
        if ((d & 0x8000) != 0) d = (-d) & 0xFFFF;
        return d >= 0x0A;
    }

    // 'ld a,r': el registro R del Z80 depende de las instrucciones ejecutadas y no se emula.
    // APROXIMACION determinista: contador de fotogramas $C010 y X del objeto.
    static int Refresh(byte[] m, int ix) => (m[0xC010] + m[ix + 0x10] * 3 + m[ix + 0x1B]) & 0x7F;

    // ---------------- $3831 (tipo $0F): estados $3840 $46E1 $3862 $4B8E ----------------
    static void H3831(byte[] m, int ix)
    {
        int s = Jump(m, 0x3838, (m[ix + 0x1F] - 1) & 0xFF);
        switch (s)
        {
            case 0x3840:
            {
                SetScript(m, ix, 0x022A);                                // $3840 $1FA3
                Animate(m, ix);                                          // $3846 $1FC0
                int de = GroundY(m, ix);                                 // $3849 $293A
                int hl = (m[ix + 0x10] & 0x10) != 0 ? 0x18 : 0x00;       // $384C-$3855
                Ww(m, ix + 0x12, (hl + de) & 0xFFFF);                    // $3857
                m[ix + 0x1F]++;                                          // $385E
                return;
            }
            case 0x46E1: H34F7(m, ix); return;                           // $46E1 = $34F7
            case 0x3862:
                if (OffScreen(m, ix)) return;                            // $3862 $2D7F
                Animate(m, ix);                                          // $3866 $1FC0
                if (Weapon(m, ix, 0x0C0E, out int iy))                   // $3869 $20A9
                {
                    m[ix + 0x1F]++;                                      // $3871
                    Killed(m, ix, iy, 0x00, 0x022B);                     // $3874 jp $4BB4
                    return;
                }
                if (!Touch(m, ix, 0x0606)) return;                       // $387C $205A
                if (PlayerHit(m, ix)) return;                            // $3883 $3359 $334A
                m[0xC85E] |= 0x04;                                       // $3889
                m[0xC859] = 0x01;                                        // $388E
                return;
            case 0x4B8E: H4B8E(m, ix); return;
            default: Missing(s); return;
        }
    }

    // ---------------- $40B1: estados 3 y 4 ----------------

    // $410A: $41DA y salto hacia el jugador cuando esta cerca (tipo impar: $80 delante o golpeado).
    static void H410A(byte[] m, int ix)
    {
        if (H41DA(m, ix)) return;                                        // $410A
        int hl = Rw(m, 0xC850);                                          // $410D
        int x = Rw(m, ix + 0x10);
        int script;
        if ((m[ix + 0x18] & 0x01) != 0)                                  // $4110
        {
            if ((m[ix + 0x1E] & 0x04) == 0 && ((hl + 0x0080) & 0xFFFF) < x) return; // $4116-$4129
            m[ix + 0x1E] &= 0xFB;                                        // $412A
            m[ix + 0x14] = 0xD0;
            m[ix + 0x17] = 0x70;
            script = 0x020D;
        }
        else
        {
            if (((hl + 0xFFB0) & 0xFFFF) < x) return;                    // $413B-$4148
            m[ix + 0x14] = 0x78;                                         // $4149
            m[ix + 0x17] = 0x78;
            script = 0x0215;
        }
        SetScript(m, ix, script);                                        // $4154 $1FA3
        TitleRom.Sound(m, 0x15);                                         // $4157 $0440
        m[ix + 0x16] = 0x00;                                             // $415C
        m[ix + 0x1F]++;                                                  // $4160
    }

    // $4167: en el aire hasta tocar el suelo; despues vuelve al estado 3 o sigue al 5.
    static void H4167(byte[] m, int ix)
    {
        MoveY(m, ix);                                                    // $4167 $43B2
        int a = (m[ix + 0x17] - 0x08) & 0xFF;                            // $416A
        if ((a & 0x80) != 0 && a < 0x90) a = 0x90;                       // $416F-$4176
        m[ix + 0x17] = (byte)a;
        MoveX(m, ix);                                                    // $417B $2725
        if (H41DA(m, ix)) return;                                        // $417E
        int de = GroundAt(m, ix, Rw(m, ix + 0x10));                      // $4181 $294F
        if (Rw(m, ix + 0x12) >= de) { OffScreen(m, ix); return; }        // $4184-$418D jp nc,$2D7F
        Ww(m, ix + 0x12, de);                                            // $4190
        int hl;
        if ((m[ix + 0x18] & 0x01) == 0)                                  // $4196
        {
            hl = 0x0214;                                                 // $419C
            if (--m[ix + 0x1B] != 0) m[ix + 0x1F] = 0x03;                // $419F $41AE
            else if (m[ix + 0x18] == 0x22) { H36E7(m, ix); return; }     // $41A4 jp z,$36E7
            else m[ix + 0x1F]++;                                         // $41CB
        }
        else
        {
            hl = 0x020C;                                                 // $41C0
            if (Rw(m, 0xC850) >= Rw(m, ix + 0x10)) m[ix + 0x1F]++;       // $41B4-$41C3 $41CB
            else m[ix + 0x1F] = 0x03;                                    // $41C5
        }
        SetScript(m, ix, hl);                                            // $41CE $1FA3
        Animate(m, ix);                                                  // jp $1FC0
    }

    // ---------------- $478A (tipos $20 $21) ----------------

    // $478A: $480D (estado) y $4797 (choques).
    static void H478A(byte[] m, int ix)
    {
        H480D(m, ix);
        if (missing != 0) return;
        if (!ScreenBounds.OnScreen(m, ix)) return;                       // $4797 bit1 (o franja ancha)
        if ((m[ix + 0x1F] & 0x0F) < 0x03) return;                        // $479C
        if (Touch(m, ix, 0x0810))                                        // $47A4 $205A
        {
            if (PlayerHit(m, ix)) return;                                // $47AC $3359 $334A
            m[0xC85E] |= 0x04;                                           // $47B2
            return;
        }
        if (!Weapon(m, ix, 0x0810, out _)) return;                       // $47B8 $20A9
        m[ix + 0x19]++;                                                  // $47BC
        if (m[ix + 0x19] >= 0x02)                                        // $47C2
        {
            TitleRom.Sound(m, 0x1B);                                     // $47C6 $0440
            m[ix + 0x1F] = 0x02;                                         // $47CB
            if (Rw(m, 0xC850) >= Rw(m, ix + 0x10)) m[ix + 0x1E] &= 0x7F; // $47CF-$47DD
            else m[ix + 0x1E] |= 0x80;                                   // $47E2
            return;
        }
        ObjectSpawn.Enqueue(m, ix, (m[ix + 0x18] & 0x01) != 0 ? 0x41 : 0x40); // $47E7-$47F0 $2D1A
        Score(m, 0x01);                                                  // $47F3 $0E48
        if ((m[ix + 0x1F] & 0x0F) >= 0x04) return;                       // $47F8
        m[ix + 0x1F] = 0x06;                                             // $4800
    }

    // $4790: script $05xx
    static void H4790(byte[] m, int ix, int a) => SetScript(m, ix, 0x0500 | a);

    // $4805: anima si IX+$1E bit5 (o franja ancha)
    static void H4805(byte[] m, int ix)
    {
        if (ScreenBounds.Visible(m, ix)) Animate(m, ix);
    }

    // $480D: tabla $4816 con (IX+$1F - 1) & 7: $4824 $486C $48D6 $4935 $4982 $49F6 $4A0D.
    // IX+$1F bit7 = estado ya iniciado.
    static void H480D(byte[] m, int ix)
    {
        int s = Jump(m, 0x4816, (m[ix + 0x1F] - 1) & 0x07);
        bool init = (m[ix + 0x1F] & 0x80) == 0;
        switch (s)
        {
            case 0x4824:                                                 // estado 1: espera
                if (init)
                {
                    m[ix + 0x1F] |= 0x80;                                // $482A
                    H4790(m, ix, 0x03);
                    Animate(m, ix);                                      // $4833 $1FC0
                    Ww(m, ix + 0x12, (GroundAt(m, ix, Rw(m, ix + 0x10)) + 0x0003) & 0xFFFF); // $4836 $294F
                    return;
                }
                // $4844-$4851: sale cuando su X queda detras del borde izquierdo de la camara. NewLogicWide=T en
                // WIDESCREEN: el borde es ScreenBounds.Left (franja ancha). F u ORIGINAL / NO: la ROM tal cual.
                if (ScreenBounds.NewLogicWide && ScreenBounds.Wide)
                {
                    if (!ScreenBounds.BehindLeft(m, Rw(m, ix + 0x10))) return;
                }
                else if (((Rw(m, 0xC442) - 0x0100) & 0xFFFF) < Rw(m, ix + 0x10)) return; // $4844-$4851
                m[ix + 0x1F] = 0x03;                                     // $4852
                m[ix + 0x19] = 0x00;
                H4790(m, ix, 0x00);                                      // $485A
                Animate(m, ix);                                          // $485F $1FC0
                Ww(m, ix + 0x12, GroundAt(m, ix, Rw(m, ix + 0x10)));     // $4862 $294F
                return;
            case 0x486C:                                                 // estado 2: derribado
            {
                if (init)
                {
                    m[ix + 0x1F] |= 0x80;                                // $4872
                    H4790(m, ix, 0x02);
                    Ww(m, ix + 0x16, 0x5EC7);                            // $487B tabla de caida
                    m[ix + 0x14] = (byte)(m[ix + 0x14] >> 1);            // $4884 srl
                    Score(m, (m[ix + 0x18] & 0x01) + 0x07);              // $4888-$488F $0E48
                }
                if (!ScreenBounds.OnScreen(m, ix))                       // $4892 bit1 (o franja ancha)
                {
                    if ((m[ix + 0x1E] & 0x80) == 0) m[ix + 0x1F] = 0x00; // $48C6
                    else m[ix + 0x18] = 0x96;                            // $48D1
                    return;
                }
                int hl = Rw(m, ix + 0x16);                               // $4898
                int e = m[hl];
                hl = (hl + 1) & 0xFFFF;
                if (m[hl] != 0x80) Ww(m, ix + 0x16, hl);                 // $48A0-$48A8
                Ww(m, ix + 0x12, (Rw(m, ix + 0x12) + (sbyte)e) & 0xFFFF); // $48AB-$48BC
                MoveX(m, ix);                                            // $48BF $2725
                H4805(m, ix);                                            // $48C2
                return;
            }
            case 0x48D6:                                                 // estado 3: avanza
            {
                if (init)
                {
                    m[ix + 0x1F] |= 0x80;                                // $48DC
                    H4790(m, ix, 0x00);
                }
                if ((m[0xC85F] & 0x0F) == 0x01) { m[ix + 0x1F] = 0x07; return; } // $48E5-$48EE
                m[ix + 0x14] = 0x54;                                     // $48F3
                MoveX(m, ix);                                            // $48F7 $2725
                if ((m[ix + 0x18] & 0x01) != 0 && (m[0xC85E] & 0x80) != 0) // $48FA-$4905
                {
                    m[ix + 0x1F] = 0x04;                                 // $4907
                    return;
                }
                int de = GroundAt(m, ix, Rw(m, ix + 0x10));              // $490C $294F
                if (FarY(m, ix, de)) { m[ix + 0x1F] = 0x05; return; }    // $490F $28DF
                Ww(m, ix + 0x12, de);                                    // $4919
                H4805(m, ix);                                            // $491F
                if (((Rw(m, 0xC442) + 0x0100) & 0xFFFF) >= Rw(m, ix + 0x10)) return; // $4922-$492F
                m[ix + 0x1F] = 0x00;                                     // $4930
                return;
            }
            case 0x4935:                                                 // estado 4: salto (sube)
            {
                if (init)
                {
                    m[ix + 0x1F] |= 0x80;                                // $493B
                    H4790(m, ix, 0x01);
                    Ww(m, ix + 0x16, 0x5E64);                            // $4944
                }
                MoveX(m, ix);                                            // $494D $2725
                int hl = Rw(m, ix + 0x16);                               // $4950
                int a = m[hl];
                if (a == 0xFF)                                           // $4957
                {
                    Ww(m, ix + 0x16, (hl - 1) & 0xFFFF);                 // $495B
                    m[ix + 0x1F] = 0x85;                                 // $4962
                    return;
                }
                Ww(m, ix + 0x16, (hl + 1) & 0xFFFF);                     // $4967
                Ww(m, ix + 0x12, (Rw(m, ix + 0x12) + a) & 0xFFFF);       // $496E-$497B
                H4805(m, ix);                                            // $497E
                return;
            }
            case 0x4982:                                                 // estado 5: cae
            {
                if (init)
                {
                    m[ix + 0x1F] |= 0x80;                                // $4988
                    H4790(m, ix, 0x01);
                    Ww(m, ix + 0x16, 0x5E9B);                            // $4991
                }
                H4805(m, ix);                                            // $499A
                MoveX(m, ix);                                            // $499D $2725
                int hl = Rw(m, ix + 0x16);                               // $49A0
                Ww(m, ix + 0x12, (Rw(m, ix + 0x12) - m[hl]) & 0xFFFF);   // $49A6-$49B6
                hl = (hl - 1) & 0xFFFF;                                  // $49BA
                if (m[hl] != 0xFF) Ww(m, ix + 0x16, hl);                 // $49BB-$49C3
                int de = GroundAt(m, ix, Rw(m, ix + 0x10));              // $49C6 $294F
                int y = Rw(m, ix + 0x12);
                if (y >= de) return;                                     // $49C9-$49D2
                if (!ScreenBounds.OnScreen(m, ix)) { m[ix + 0x1F] = 0x00; return; } // $49D3 bit1 (o franja ancha)
                int d = (y - de) & 0xFFFF;                               // $49DE
                if ((d >> 8) != 0xFF || (d & 0xFF) < 0xF8) { m[ix + 0x14] = 0x00; return; } // $49DE-$49E4 $49F1
                Ww(m, ix + 0x12, de);                                    // $49E6 aterriza
                m[ix + 0x1F] = 0x03;                                     // $49EC
                return;
            }
            case 0x49F6:                                                 // estado 6: golpeado (pausa)
                if (init)
                {
                    m[ix + 0x1F] |= 0x80;                                // $49FC
                    m[ix + 0x1A] = 0x0C;
                }
                if (--m[ix + 0x1A] != 0) return;                         // $4A04
                m[ix + 0x1F] = 0x03;                                     // $4A08
                return;
            case 0x4A0D:                                                 // estado 7: jugador muerto
                if (init)
                {
                    m[ix + 0x1F] |= 0x80;                                // $4A13
                    H4790(m, ix, 0x04);
                    return;
                }
                H4805(m, ix);                                            // $4A1D
                return;
            default: Missing(s); return;
        }
    }

    // ---------------- $457B (tipo $1B): estados $4588 $34F7 $45A8 ----------------
    static void H457B(byte[] m, int ix)
    {
        int s = Jump(m, 0x4582, (m[ix + 0x1F] - 1) & 0xFF);
        switch (s)
        {
            case 0x4588:
                Ww(m, ix + 0x12, Hover(m, ix, 0x10));                    // $4588 $3A4A
                H4593(m, ix);                                            // $4593
                return;
            case 0x34F7: H34F7(m, ix); return;
            case 0x45A8:
                if (OffScreen(m, ix)) return;                            // $45A8 $2D7F
                if (!Touch(m, ix, 0x0608)) return;                       // $45AC $205A
                TitleRom.Sound(m, 0x0A);                                 // $45B3 $0440
                Vitality(m, 0x0A00);                                     // $45B8 $4EF4
                Collected(m, ix, 0x04);                                  // $45BE jp $32E9
                return;
            default: Missing(s); return;
        }
    }

    // ---------------- $4BE1 (tipos $26 $27): estados $4BF4 $34F7 $4C13 $4C36 $4C6D $4696 ----------------
    static void H4BE1(byte[] m, int ix)
    {
        int s = Jump(m, 0x4BE8, (m[ix + 0x1F] - 1) & 0xFF);
        switch (s)
        {
            case 0x4BF4:
            {
                SetScript(m, ix, 0x0225);                                // $4BF4 $1FA3
                Animate(m, ix);                                          // $4BFA $1FC0
                int de = GroundY(m, ix);                                 // $4BFD $293A
                int l = (0x80 - m[ix + 0x08]) & 0xFF;                    // $4C00-$4C06
                Ww(m, ix + 0x12, (l + de) & 0xFFFF);                     // $4C08
                m[ix + 0x1F]++;                                          // $4C0F
                return;
            }
            case 0x34F7: H34F7(m, ix); return;
            case 0x4C13:
                if (OffScreen(m, ix)) return;                            // $4C13 $2D7F
                if (((Rw(m, 0xC850) + 0x0080) & 0xFFFF) >= Rw(m, ix + 0x10)) // $4C17-$4C27
                {
                    m[ix + 0x1F]++;                                      // $4C29
                    m[ix + 0x19] = (byte)(m[0xC086] & 0xC4);             // $4C2C-$4C31
                }
                H4CA7(m, ix);
                return;
            case 0x4C36:
            {
                if (OffScreen(m, ix)) return;                            // $4C36 $2D7F
                bool drop = (m[ix + 0x18] & 0x01) != 0 && (m[0xC086] & 0xC4) != m[ix + 0x19]; // $4C3A-$4C48
                if (!drop)
                {
                    int de = (m[ix + 0x18] & 0x01) != 0 ? 0x0000 : 0x0030; // $4C4D-$4C56
                    drop = ((Rw(m, 0xC850) + de) & 0xFFFF) >= Rw(m, ix + 0x10); // $4C59-$4C63
                }
                if (drop)
                {
                    m[ix + 0x17]--;                                      // $4C65
                    m[ix + 0x1F]++;                                      // $4C68
                }
                H4CA7(m, ix);
                return;
            }
            case 0x4C6D:
            {
                if (OffScreen(m, ix)) return;                            // $4C6D $2D7F
                if (!ScreenBounds.OnScreen(m, ix) && Rw(m, ix + 0x12) < GroundY(m, ix)) // $4C71-$4C83
                {
                    m[ix + 0x1F] = 0x00;                                 // $4C85
                    return;
                }
                MoveY(m, ix);                                            // $4C8A $43B2
                int a = m[ix + 0x17];                                    // $4C8D
                if (a >= 0xB1) m[ix + 0x17] = (byte)(a - 0x03);          // $4C90-$4C96
                int de2 = GroundAt(m, ix, Rw(m, ix + 0x10));             // $4C99 $294F
                if (Rw(m, ix + 0x12) < de2) { H4CB4(m, ix); return; }    // $4C9C-$4CA5
                H4CA7(m, ix);
                return;
            }
            case 0x4696: H4696(m, ix); return;
            default: Missing(s); return;
        }
    }

    // $4CA7: arma (puntos 1 y $4CB4) o choque con el jugador.
    static void H4CA7(byte[] m, int ix)
    {
        if (Weapon(m, ix, 0x0A04, out _))                                // $4CA7 $20A9
        {
            Score(m, 0x01);                                              // $4CAF $0E48
            H4CB4(m, ix);
            return;
        }
        if (!Touch(m, ix, 0x0A04)) return;                               // $4CC2 $205A
        if (PlayerHit(m, ix)) return;                                    // $4CC6 $3359 $334A
        m[0xC85E] |= 0x04;                                               // $4CCC
    }

    // $4CB4: se rompe (script $0226, estado 6)
    static void H4CB4(byte[] m, int ix)
    {
        SetScript(m, ix, 0x0226);                                        // $4CB4 $1FA3
        Animate(m, ix);                                                  // $4CBA $1FC0
        m[ix + 0x1F] = 0x06;                                             // $4CBD
    }

    // ---------------- $2E24 (tipo $2C, rey): estados $2E39 $34F7 $2E54 $2F7E $2F93 $2FAE $4696 ----------------
    // IX+$19: bit7 lanzando, bit6 sentido, bit5 paso del script, bits 0-3 fase del lanzamiento.
    // IX+$1A golpes que quedan hasta el lanzamiento, IX+$1B cuenta atras; $C47C-$C47F parametros del area.
    static void H2E24(byte[] m, int ix)
    {
        int s = Jump(m, 0x2E2B, (m[ix + 0x1F] - 1) & 0xFF);
        switch (s)
        {
            case 0x2E39:
                m[0xC918] = 0xAD;                                        // $2E39
                m[ix + 0x1A] = m[0xC47C];                                // $2E3E
                m[ix + 0x1B] = m[0xC47D];                                // $2E44
                m[ix + 0x19] = 0x00;                                     // $2E4A
                Start(m, ix, 0x0700);                                    // $2E4E jp $4050
                return;
            case 0x34F7: H34F7(m, ix); return;
            case 0x2E54: H2E54(m, ix); return;
            case 0x2F7E:
                m[ix + 0x19] = 0x00;                                     // $2F7E
                m[ix + 0x14] = 0x04;
                m[ix + 0x17] = 0x40;
                SetScript(m, ix, 0x0700);                                // $2F8A $1FA3
                Animate(m, ix);                                          // jp $1FC0
                return;
            case 0x2F93:
            {
                if (!ScreenBounds.OnScreen(m, ix)) { m[ix + 0x1F] = 0x00; return; } // $2F93 bit1 (o franja ancha)
                MoveX(m, ix);                                            // $2F9E $2725
                MoveY(m, ix);                                            // $2FA1 $43B2
                int a = (m[ix + 0x14] + 0x04) & 0xFF;                    // $2FA4
                if ((a & 0x80) != 0) return;                             // $2FA9 ret m
                m[ix + 0x14] = (byte)a;
                return;
            }
            case 0x2FAE:
                m[ix + 0x1F]++;                                          // $2FAE
                ObjectSpawn.Enqueue(m, ix, 0x1C);                        // $2FB1 $2D1A
                SetScript(m, ix, 0x0714);                                // $2FB6 $1FA3
                Animate(m, ix);                                          // $2FBC $1FC0
                TitleRom.Sound(m, 0x0C);                                 // $2FBF jp $0440
                return;
            case 0x4696: H4696(m, ix); return;
            default: Missing(s); return;
        }
    }

    // $2E54: estado 3 del rey (choques, paseo y lanzamientos)
    static void H2E54(byte[] m, int ix)
    {
        if (Weapon(m, ix, 0x1224, out int iy)) m[iy + 0x19] = 0x1A;     // $2E54-$2E5C (a = $08 + d de $20D0)
        if (Touch(m, ix, 0x1224)) m[0xC85E] |= 0x04;                     // $2E5F-$2E67
        if ((m[ix + 0x19] & 0x80) != 0) { H2F2C(m, ix); return; }        // $2E69
        if (--m[ix + 0x1B] != 0) return;                                 // $2E70
        m[ix + 0x1B] = m[0xC47D];                                        // $2E74
        if (m[ix + 0x1A] == 0) { H2EE2(m, ix); return; }                 // $2E7A
        m[ix + 0x1A]--;                                                  // $2E80
        H2E83(m, ix);
    }

    // $2E83: un paso a izquierda/derecha dentro de la pantalla; alterna el script $0700/$0701.
    static void H2E83(byte[] m, int ix)
    {
        while (true)
        {
            int cam = Rw(m, 0xC442);                                     // $2E83
            int x = Rw(m, ix + 0x10);
            int hl;
            if ((m[ix + 0x19] & 0x40) == 0)                              // $2E8C
            {
                if (((cam + 0xFFC0) & 0xFFFF) < x) hl = (x + 0xFFF8) & 0xFFFF; // $2E92-$2EA4
                else { m[ix + 0x19] |= 0x40; hl = (x + 0x0008) & 0xFFFF; }    // $2E9B $2EC2
            }
            else
            {
                hl = (cam + 0x00A0) & 0xFFFF;                            // $2EA7
                if (hl >= x) hl = 0x3EC0;                                // $2EAC-$2EB6
                if (hl < x) { m[ix + 0x19] &= 0xBF; continue; }          // $2EB7-$2EC0 jr $2E83
                hl = (x + 0x0008) & 0xFFFF;                              // $2EC2
            }
            Ww(m, ix + 0x10, hl);                                        // $2EC6
            int script = 0x0700;                                         // $2ECC
            int a = m[ix + 0x19];
            if ((a & 0x20) == 0) script++;                               // $2ED2
            m[ix + 0x19] = (byte)(a ^ 0x20);                             // $2ED7
            SetScript(m, ix, script);                                    // $2EDC $1FA3
            Animate(m, ix);                                              // jp $1FC0
            return;
        }
    }

    // $2EE2: nueva tanda de golpes (aleatoria, 'ld a,r') y lanzamientos ($C480 = $C47F).
    static void H2EE2(byte[] m, int ix)
    {
        int c = (Refresh(m, ix) & 0x03) + 1;                             // $2EE2-$2EE7
        int a = m[0xC47C] - c;                                           // $2EE8
        if (a < 0) a = 0;
        m[ix + 0x1A] = (byte)(a + 1);                                    // $2EEF
        m[0xC480] = m[0xC47F];                                           // $2EF3
        H2EF9(m, ix);
    }

    // $2EF9: busca ranura libre en $C920/$C940 para el proyectil ($AE); sin ranura, $2E83.
    static void H2EF9(byte[] m, int ix)
    {
        int iy = 0xC920;                                                 // $2EF9
        int b = 2;
        while (m[iy + 0x1F] != 0)                                        // $2F02
        {
            iy += 0x20;
            if (--b == 0) { H2E83(m, ix); return; }                      // $2F0C
        }
        m[ix + 0x19] = (byte)((m[ix + 0x19] & 0x70) | 0x80);             // $2F0F-$2F16
        m[ix + 0x1B] = m[0xC47E];                                        // $2F19
        m[iy + 0x18] = 0xAE;                                             // $2F1F
        SetScript(m, ix, 0x0702);                                        // $2F23 $1FA3
        Animate(m, ix);                                                  // jp $1FC0
    }

    // $2F2C: fases del lanzamiento (tabla $2F3E con IX+$19 & $0F: $2F48 $2F48 $2F48 $2F4C $2F58).
    static void H2F2C(byte[] m, int ix)
    {
        if (--m[ix + 0x1B] != 0) return;                                 // $2F2C
        m[ix + 0x1B] = m[0xC47E];                                        // $2F30
        int s = Jump(m, 0x2F3E, m[ix + 0x19] & 0x0F);                    // $2F36 $0ABB
        switch (s)
        {
            case 0x2F48: m[ix + 0x19]++; return;
            case 0x2F4C:
                m[ix + 0x19]++;                                          // $2F4C
                SetScript(m, ix, 0x0703);                                // $2F4F $1FA3
                Animate(m, ix);                                          // jp $1FC0
                return;
            case 0x2F58:
                m[ix + 0x19] &= 0x60;                                    // $2F58
                m[ix + 0x1B] = m[0xC47D];                                // $2F60
                SetScript(m, ix, 0x0700);                                // $2F66 $1FA3
                Animate(m, ix);                                          // $2F6C $1FC0
                if ((Refresh(m, ix) & 0x0F) >= 0x0C) return;             // $2F6F-$2F75
                if (--m[0xC480] == 0) return;                            // $2F76
                H2EF9(m, ix);                                            // $2F7B
                return;
            default: Missing(s); return;
        }
    }

    // $30E1: pegado al rey ($C8C0): X = $C8D0 - 2, Y = $C8D2 + $1C
    static void H30E1(byte[] m, int ix)
    {
        Ww(m, ix + 0x10, (Rw(m, 0xC8D0) + 0xFFFE) & 0xFFFF);
        Ww(m, ix + 0x12, (Rw(m, 0xC8D2) + 0x001C) & 0xFFFF);
    }

    // ---------------- $2FC4 (tipo $2D, cabeza del rey): estados $2FD7 $2FF6 $304B $30A9 $30C7 $30DB ----------------
    // IX+$19 golpes que quedan ($C300*3 + 8), IX+$1A parpadeo tras un golpe.
    static void H2FC4(byte[] m, int ix)
    {
        int s = Jump(m, 0x2FCB, (m[ix + 0x1F] - 1) & 0xFF);
        switch (s)
        {
            case 0x2FD7:
            {
                m[ix + 0x1F]++;                                          // $2FD7
                H30E1(m, ix);                                            // $2FDA
                int c = m[0xC300];
                m[ix + 0x19] = (byte)(c * 3 + 0x08);                     // $2FDD-$2FE5
                SetScript(m, ix, 0x0700 | ((c + 0x07) & 0xFF));          // $2FE8-$2FF0 $1FA3
                Animate(m, ix);                                          // jp $1FC0
                return;
            }
            case 0x2FF6:
                if (Weapon(m, ix, 0x120E, out _))                        // $2FF6 $20A9
                {
                    if (--m[ix + 0x19] == 0)                             // $2FFE jr z,$3034 (derrotado)
                    {
                        ObjectSpawn.Enqueue(m, ix, 0x2D);                // $3034 $2D1A
                        Score(m, 0x09);                                  // $3039 $0E48
                        m[ix + 0x1F]++;                                  // $303E
                        m[ix + 0x17] = 0x20;
                        m[0xC8DF] = 0x04;                                // $3045
                        return;
                    }
                    m[ix + 0x1A] = 0x08;                                 // $3003
                    ObjectSpawn.Enqueue(m, ix, 0x46);                    // $3007 $2D1A
                    Score(m, 0x03);                                      // $300C $0E48
                }
                if (Touch(m, ix, 0x120E)) m[0xC85E] |= 0x04;             // $3011-$301C
                H30E1(m, ix);                                            // $301E
                if (m[ix + 0x1A] != 0 && --m[ix + 0x1A] == 0)            // $3021-$302A
                    ObjectSpawn.Enqueue(m, ix, 0x2D);                    // $302C $2D1A
                H30E1(m, ix);                                            // $3031 jp $30E1
                return;
            case 0x304B:
            {
                MoveY(m, ix);                                            // $304B $43B2
                m[ix + 0x17] -= 0x02;                                    // $304E
                int de = GroundY(m, ix);                                 // $3056 $293A
                if (Rw(m, ix + 0x12) >= de) return;                      // $3059-$3062
                Ww(m, ix + 0x12, de);                                    // $3063
                if (m[0xC313] != 0) m[0xC8DF] = 0x06;                    // $3069-$3071
                else m[0xC8B8] = 0xAF;                                   // $3076
                if (m[0xC300] < 0x07)                                    // $307B
                {
                    m[ix + 0x1F]++;                                      // $3082
                    ObjectSpawn.Enqueue(m, ix, 0x1C);                    // $3085 $2D1A
                    SetScript(m, ix, 0x0315);                            // $308A $1FA3
                    Animate(m, ix);                                      // $3090 $1FC0
                    TitleRom.Sound(m, 0x0A);                             // $3093 jp $0440
                    return;
                }
                m[0xC479]++;                                             // $3098
                m[ix + 0x1F] = 0x06;                                     // $309C
                SetScript(m, ix, 0x070F);                                // $30A0 $1FA3
                Animate(m, ix);                                          // jp $1FC0
                return;
            }
            case 0x30A9:
                Animate(m, ix);                                          // $30A9 $1FC0
                if ((m[ix + 0x1E] & 0x01) == 0) return;                  // $30AC
                SetScript(m, ix, 0x0300 | ((m[0xC300] + 0x16) & 0xFF));  // $30B1-$30B9 $1FA3
                Animate(m, ix);                                          // $30BC $1FC0
                m[ix + 0x1F]++;                                          // $30BF
                ObjectSpawn.Enqueue(m, ix, 0x39);                        // $30C2 jp $2D1A
                return;
            case 0x30C7:
                if (!Touch(m, ix, 0x0608)) return;                       // $30C7 $205A
                m[0xC479]++;                                             // $30CE
                m[ix + 0x1F] = 0x00;                                     // $30D2
                TitleRom.Sound(m, 0x0D);                                 // $30D6 jp $0440
                return;
            case 0x30DB:
                Animate(m, ix);                                          // $30DB $1FC0
                OffScreen(m, ix);                                        // jp $2D7F
                return;
            default: Missing(s); return;
        }
    }

    // ---------------- $30FC (tipo $2E, proyectil del rey): estados $310D $3137 $314D $3164 $3199 ----------------
    // Sigue las fases del rey ($C8D9: bit7 lanzando, bits 0-3 fase) y despues rebota por el suelo.
    static void H30FC(byte[] m, int ix)
    {
        int s = Jump(m, 0x3103, (m[ix + 0x1F] - 1) & 0xFF);
        switch (s)
        {
            case 0x310D:
                m[ix + 0x1F]++;                                          // $310D
                m[ix + 0x19] = 0x00;
                Ww(m, ix + 0x10, (Rw(m, 0xC8D0) + 0x001C) & 0xFFFF);     // $3114
                Ww(m, ix + 0x12, (Rw(m, 0xC8D2) + 0x0010) & 0xFFFF);     // $3121
                SetScript(m, ix, 0x0704);                                // $312E $1FA3
                Animate(m, ix);                                          // jp $1FC0
                return;
            case 0x3137:
            case 0x314D:
            case 0x3164:
            {
                Animate(m, ix);                                          // $3137/$314D/$3164 $1FC0
                int a = m[0xC8D9];
                if ((a & 0x80) == 0) { m[ix + 0x1F] = 0x00; return; }    // $3194
                int phase = s == 0x3137 ? 0 : s == 0x314D ? 1 : 2;       // $3143 / $3159 dec / $3170 cp 2
                if ((a & 0x0F) == phase) return;
                m[ix + 0x1F]++;
                if (s == 0x3137) { SetScript(m, ix, 0x0705); return; }   // $3147 jp $1FA3
                if (s == 0x314D) { SetScript(m, ix, 0x0706); return; }   // $315E jp $1FA3
                int b = m[0xC300];                                       // $3176
                int c = (b * 2) & 0xFF;
                m[ix + 0x14] = (byte)(-((b * 4 + 0x40) & 0xFF));         // $317C-$3181 neg
                m[ix + 0x17] = (byte)((Refresh(m, ix) & 0x08) + 0x30 - c - b); // $3184-$318C ('ld a,r')
                TitleRom.Sound(m, 0x12);                                 // $318F jp $0440
                return;
            }
            case 0x3199:
            {
                if (OffScreen(m, ix)) return;                            // $3199 $2D7F
                Animate(m, ix);                                          // $319D $1FC0
                if (Touch(m, ix, 0x0404))                                // $31A0 $205A
                {
                    m[0xC85E] |= 0x04;                                   // $31A8
                    m[0xC859] = 0x01;                                    // $31AD
                    m[ix + 0x14] = 0x00;                                 // $31B2
                    return;
                }
                MoveX(m, ix);                                            // $31B7 $2725
                MoveY(m, ix);                                            // $31BA $43B2
                m[ix + 0x17] -= 0x02;                                    // $31BD
                int de = GroundY(m, ix);                                 // $31C5 $293A
                if (Rw(m, ix + 0x12) >= de) return;                      // $31C8-$31D1
                Ww(m, ix + 0x12, de);                                    // $31D2 rebote
                int v = m[ix + 0x17];
                m[ix + 0x17] = (byte)(-v + ((sbyte)v >> 2));             // $31D8-$31E3 neg + sra sra
                return;
            }
            default: Missing(s); return;
        }
    }

    // ---------------- $31E7 (piezas del rey): estados $31F6 $321C $3230 $323E ----------------
    static void H31E7(byte[] m, int ix)
    {
        int s = Jump(m, 0x31EE, (m[ix + 0x1F] - 1) & 0xFF);
        switch (s)
        {
            case 0x31F6:
                SetScript(m, ix, 0x0700 | ((m[0xC300] + 0x08) & 0xFF));  // $31F6-$31FE $1FA3
                Animate(m, ix);                                          // $3201 $1FC0
                if (m[ix + 0x18] == 0x2B) { m[ix + 0x1F] = 0x04; H30E1(m, ix); return; } // $3204 $3215
                m[ix + 0x1F]++;                                          // $320B
                m[ix + 0x19] = 0x20;
                H3247(m, ix);                                            // $3212 jp $3247
                return;
            case 0x321C:
                m[ix + 0x19]--;                                          // $321C
                H3247(m, ix);                                            // $321F
                if (m[ix + 0x19] != 0) return;                           // $3222
                m[ix + 0x1F]++;                                          // $3227
                m[0xC8F8] = 0xAB;                                        // $322A
                return;
            case 0x3230: H3230(m, ix); return;
            case 0x323E:
                if (!ScreenBounds.Visible(m, ix)) { H3230(m, ix); return; } // $323E bit5 (o franja ancha)
                H30E1(m, ix);                                            // $3244 jp $30E1
                return;
            default: Missing(s); return;
        }
    }

    // $3230
    static void H3230(byte[] m, int ix)
    {
        m[ix + 0x1F] = 0x00;
        m[0xC8DF] = 0x05;                                                // $3234
        TitleRom.Sound(m, 0x10);                                         // $3239 jp $0440
    }

    // $3247: X = $C8D0 - 4, Y = $C8D2 + ($20 - IX+$19)
    static void H3247(byte[] m, int ix)
    {
        Ww(m, ix + 0x10, (Rw(m, 0xC8D0) + 0xFFFC) & 0xFFFF);
        Ww(m, ix + 0x12, (Rw(m, 0xC8D2) + ((0x20 - m[ix + 0x19]) & 0xFF)) & 0xFFFF);
    }

    // ---------------- $45C3 (tipo $1F): estados $45D0 $34F7 $45FA ($C476 bit0 = uno ya activo) ----------------
    static void H45C3(byte[] m, int ix)
    {
        int s = Jump(m, 0x45CA, (m[ix + 0x1F] - 1) & 0xFF);
        switch (s)
        {
            case 0x45D0:
                Ww(m, ix + 0x12, Hover(m, ix, 0x10));                    // $45D0 $3A4A
                if (m[0xC476] != 0) { H4593(m, ix); return; }            // $45DB-$45E0 jp nz,$4593
                m[0xC476] |= 0x01;                                       // $45E3
                m[ix + 0x1F] = 0x02;                                     // $45E5
                SetScript(m, ix, 0x0312);                                // $45E9 $1FA3
                Animate(m, ix);                                          // $45EF $1FC0
                m[ix + 0x18] = 0x1F;                                     // $45F2
                ObjectSpawn.Enqueue(m, ix, 0x1F);                        // $45F7 jp $2D1A
                return;
            case 0x34F7: H34F7(m, ix); return;
            case 0x45FA:
                if (!ScreenBounds.Visible(m, ix))                        // $45FA bit5 (o franja ancha)
                {
                    m[ix + 0x1F] = 0x00;                                 // $4600
                    m[0xC476] &= 0xFE;                                   // $4604
                    return;
                }
                if (!Touch(m, ix, 0x0608)) return;                       // $460A $205A
                TitleRom.Sound(m, 0x0A);                                 // $4611 $0440
                m[0xC476] = 0x02;                                        // $4616
                Collected(m, ix, 0x01);                                  // $461B jp $32E9
                return;
            default: Missing(s); return;
        }
    }

    // ---------------- $4620 (tipo $1E, leche): estados $462D $34F7 $464D ----------------
    static void H4620(byte[] m, int ix)
    {
        int s = Jump(m, 0x4627, (m[ix + 0x1F] - 1) & 0xFF);
        switch (s)
        {
            case 0x462D:
                Ww(m, ix + 0x12, Hover(m, ix, 0x10));                    // $462D $3A4A
                SetScript(m, ix, 0x0307);                                // $4638 $1FA3
                Animate(m, ix);                                          // $463E $1FC0
                m[ix + 0x1F] = 0x02;                                     // $4641
                m[ix + 0x18] = 0x1E;                                     // $4645
                ObjectSpawn.Enqueue(m, ix, 0x1E);                        // $464A jp $2D1A
                return;
            case 0x34F7: H34F7(m, ix); return;
            case 0x464D:
            {
                if (OffScreen(m, ix)) return;                            // $464D $2D7F
                if (!Touch(m, ix, 0x0608)) return;                       // $4651 $205A
                TitleRom.Sound(m, 0x0A);                                 // $4658 $0440
                int de = Rw(m, 0xC469);                                  // $465D
                if (de <= 0x1CFF)                                        // $4661-$4667
                {
                    Ww(m, 0xC469, 0x2000);                               // $4669 vitalidad llena
                    Collected(m, ix, 0x03);                              // $466F jp $32E9
                    return;
                }
                if (de <= 0x2000) Ww(m, 0xC469, 0x2000);                 // $4674-$467F
                Collected(m, ix, 0x08);                                  // $4682 jp $32E9
                return;
            }
            default: Missing(s); return;
        }
    }

    // ---------------- $39D7 (tipo $16): 1 aparece delante del jugador, 2 espera (IX+$1A), 3 $4687, >3 $4696 ----------------
    static void H39D7(byte[] m, int ix)
    {
        int st = m[ix + 0x1F];
        if (st == 0x01)                                                  // $39DA
        {
            m[ix + 0x1F] = 0x02;                                         // $39DE
            Ww(m, ix + 0x10, (Rw(m, 0xC850) + 0x0080) & 0xFFFF);         // $39E2
            SetScript(m, ix, 0x030E);                                    // $39EF $1FA3
            Animate(m, ix);                                              // $39F5 $1FC0
            Ww(m, ix + 0x12, (GroundY(m, ix) + 0x0018) & 0xFFFF);        // $39F8 $293A
            m[ix + 0x1A] = 0xB4;                                         // $3A05
            return;
        }
        if (st == 0x03) { H4687(m, ix); return; }                        // $3A0A jp z,$4687
        if (st > 0x03) { H4696(m, ix); return; }                         // $3A0F jp nc,$4696
        if (OffScreen(m, ix)) return;                                    // $3A12 $2D7F
        if (--m[ix + 0x1A] == 0)                                         // $3A16
        {
            m[ix + 0x1F] = 0x04;                                         // $3A1B
            H46A6(m, ix);                                                // jp $46A6
            return;
        }
        if (!Touch(m, ix, 0x0408)) return;                               // $3A22 $205A
        Score(m, 0x07);                                                  // $3A29 $0E48
        Vitality(m, 0x0500);                                             // $3A2E $4EF4
        TitleRom.Sound(m, 0x0D);                                         // $3A34 $0440
        m[ix + 0x1F] = 0x03;                                             // $3A39
        m[ix + 0x1A] = 0x3C;
        SetScript(m, ix, 0x0403);                                        // $3A41 $1FA3
        Animate(m, ix);                                                  // jp $1FC0
    }

    // ---------------- $32B1 (tipo $29): estados $32BE $32D8 $2D7F ----------------
    static void H32B1(byte[] m, int ix)
    {
        int s = Jump(m, 0x32B8, (m[ix + 0x1F] - 1) & 0xFF);
        switch (s)
        {
            case 0x32BE:
                SetScript(m, ix, 0x0713);                                // $32BE $1FA3
                Animate(m, ix);                                          // $32C4 $1FC0
                Ww(m, ix + 0x12, (GroundY(m, ix) + 0x0020) & 0xFFFF);    // $32C7 $293A
                m[ix + 0x1F]++;                                          // $32D4
                return;
            case 0x32D8:
                Animate(m, ix);                                          // $32D8 $1FC0
                if ((m[ix + 0x1E] & 0x01) == 0) return;                  // $32DB
                m[ix + 0x1F]++;                                          // $32E0
                m[0xC313] = 0x02;                                        // $32E3
                return;
            case 0x2D7F: OffScreen(m, ix); return;                       // jp $2D7F
            default: Missing(s); return;
        }
    }

    // ---------------- $3267 (tipo $28): estados $3278 $34F7 $327E $3293 $2D7F ----------------
    static void H3267(byte[] m, int ix)
    {
        int s = Jump(m, 0x326E, (m[ix + 0x1F] - 1) & 0xFF);
        switch (s)
        {
            case 0x3278: Start(m, ix, 0x0710); return;                   // $3278 jp $4050
            case 0x34F7: H34F7(m, ix); return;
            case 0x327E:
                Animate(m, ix);                                          // $327E $1FC0
                if (!Touch(m, ix, 0x0830)) return;                       // $3281 $205A
                m[0xC85F] = 0x06;                                        // $3288
                Start(m, ix, 0x0711);                                    // $328D jp $4050
                return;
            case 0x3293:
                Animate(m, ix);                                          // $3293 $1FC0
                if ((m[ix + 0x1E] & 0x01) == 0) return;                  // $3296
                m[0xC9B8] = 0xA9;                                        // $329B
                Ww(m, 0xC9B0, 0x3F40);                                   // $32A0
                m[0xC85F] = 0x07;                                        // $32A6
                Start(m, ix, 0x0712);                                    // $32AB jp $4050
                return;
            case 0x2D7F: OffScreen(m, ix); return;                       // jp $2D7F
            default: Missing(s); return;
        }
    }
}
