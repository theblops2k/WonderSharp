namespace WonderBoy.Game;

using static ObjectRoutines;

// Despachador per-enemigo $2D9B y manejadores, traduccion 1:1. $2D9B salta por la tabla de ROM $2DA2
// con IX+$18 - 1 ($0ABB: indice*2 en 8 bits); los manejadores saltan igual por su tabla de estados con
// IX+$1F - 1 (algunos con & $0F). Las tablas se leen de la ROM, no se copian.
// Portados: $36A8 (tipo $02), $37C3 ($0E), $3894 ($10-$14), $3408 ($1C, estados 1-6), $330A ($40),
// $4687 ($41) y sus subrutinas: $205A, $20A9/$20D0, $2725, $2959/$294F/$2660, $3359/$334A/$3374,
// $32E9, $3A4A, $43B2, $46A6, $4696, $46E1/$34F7, $4B8E, $4BB4, $4EF4, $0E48, $0440 (TitleRom.Sound).
// Ademas $423E (tipo $09), $3B6F ($04), $3AB6 ($03, con $3D27), $46B4 ($1A, con $46A6) y $377F ($01, con $4805).
// Tambien $4509 (tipo $18, flag $C407) y $3A68 ($19, flag $C408; reutiliza $4593 del manejador $457B).
// Y $3D5A (tipos $3A $3B $3E $3F, plataformas) con $3DFF, $3EAF, $3F17/$3F77 ($3DEB, $2120), $3F8D ($26FA) y $3FF4.
// $40C4 (tipo $0D) con $41DA (arma/choque, 2 golpes) y $3D27.
// $42E6 (tipo $0B), $3CA8 ($06 $0C, con $3D27 y $36E7).
// $43DA (con $44E1) y $339C (huevo variante, comparte estados con $3408 via EggStep; $33C7).
// $4A21 (tipo $24) con $4A87 y $3D27.
// $46B4 4.o premio: $4768 jp $0E2E (TitleRom.LifeUp; sin verificar si ram_rand no llega a ese premio).
// No toca VRAM ni sprite RAM (salvo $14D0 via $0E2E). Sonido: cola de TitleRom.Sound ($0440/$0428).
// Lo no portado no se ejecuta: Dispatch devuelve la direccion de la rutina que falta (0 = portado).
public static partial class Enemies
{
    public const int Table = 0x2DA2;

    // Contadores de diagnostico para --check-enemies: choques con jugador ($205A) y arma ($20A9)
    public static int TouchHits, WeaponHits;

    // Direccion de la primera rutina sin portar alcanzada en la ultima llamada (0 = ninguna)
    static int missing;

    // $0ABB: destino de la tabla en tbl con indice a (a+a en 8 bits)
    static int Jump(byte[] m, int tbl, int a) => Word(m, tbl, a);

    // Handler de un tipo (IX+$18), leido de la tabla $2DA2
    public static int HandlerOf(byte[] m, int type) => Jump(m, Table, (type - 1) & 0xFF);

    // $2D9B. Devuelve 0 si todo el camino esta portado; si no, la direccion que falta (no ejecutada).
    public static int Dispatch(byte[] m, int ix)
    {
        missing = 0;
        int h = HandlerOf(m, m[ix + 0x18]);
        switch (h)
        {
            case 0x36A8: H36A8(m, ix); break;
            case 0x37C3: H37C3(m, ix); break;
            case 0x3894: H3894(m, ix); break;
            case 0x3408: H3408(m, ix); break;
            case 0x330A: H330A(m, ix); break;
            case 0x4687: H4687(m, ix); break;
            case 0x423E: H423E(m, ix); break;
            case 0x3B6F: H3B6F(m, ix); break;
            case 0x3AB6: H3AB6(m, ix); break;
            case 0x46B4: H46B4(m, ix); break;
            case 0x377F: H377F(m, ix); break;
            case 0x4509: H4509(m, ix); break;
            case 0x3A68: H3A68(m, ix); break;
            case 0x3D5A: H3D5A(m, ix); break;
            case 0x4AD3: H4AD3(m, ix); break;
            case 0x4A21: H4A21(m, ix); break;
            case 0x40C4: H40C4(m, ix); break;
            case 0x43DA: H43DA(m, ix); break;
            case 0x339C: H339C(m, ix); break;
            case 0x4025: H4025(m, ix); break;
            case 0x371A: H371A(m, ix); break;
            case 0x42E6: H42E6(m, ix); break;
            case 0x3CA8: H3CA8(m, ix); break;
            case 0x3831: H3831(m, ix); break;                       // Enemies.Extra.cs
            case 0x40B1: H40B1(m, ix); break;
            case 0x478A: H478A(m, ix); break;
            case 0x457B: H457B(m, ix); break;
            case 0x4BE1: H4BE1(m, ix); break;
            case 0x2E24: H2E24(m, ix); break;
            case 0x3267: H3267(m, ix); break;
            case 0x2FC4: H2FC4(m, ix); break;
            case 0x30FC: H30FC(m, ix); break;
            case 0x31E7: H31E7(m, ix); break;
            case 0x45C3: H45C3(m, ix); break;
            case 0x4620: H4620(m, ix); break;
            case 0x39D7: H39D7(m, ix); break;
            case 0x32B1: H32B1(m, ix); break;
            default: missing = h; break;
        }
        return missing;
    }

    static void Missing(int a) { if (missing == 0) missing = a; }

    // ---------------- manejadores ----------------

    // $36A8: estados $36B7 $34F7 $36BD $3708
    static void H36A8(byte[] m, int ix)
    {
        int s = Jump(m, 0x36AF, (m[ix + 0x1F] - 1) & 0xFF);
        switch (s)
        {
            case 0x36B7: Start(m, ix, 0x0201); return;
            case 0x34F7: H34F7(m, ix); return;
            case 0x36BD:
                if (OffScreen(m, ix)) return;                        // $36BD
                if (Weapon(m, ix, 0x0608, out int iy)) m[iy + 0x19] = 0x01; // $36C1-$36C9
                if (!Touch(m, ix, 0x070A)) return;                   // $36CD
                if (m[ix + 0x18] == 0x31) { H36E7(m, ix); return; }  // $36D4
                if (PlayerHit(m, ix)) return;                        // $36DB $3359 $334A
                m[0xC85E] |= 0x08;                                   // $36E1
                return;
            case 0x3708:
                if (OffScreen(m, ix)) return;                        // $3708
                Animate(m, ix);
                if ((m[ix + 0x1E] & 0x01) == 0) return;              // $370F
                m[ix + 0x19] = 0x18;                                 // $3714
                H3725(m, ix);
                return;
            default: Missing(s); return;
        }
    }

    // $36E7 (tipos $31/$32/$33 al tocar al jugador)
    static void H36E7(byte[] m, int ix)
    {
        m[ix + 0x18] = 0x02;
        Vitality(m, 0x0400);                                         // $36EB $4EF4
        m[0xC85F] = 0x05;
        m[ix + 0x1F] = 0x04;
        ObjectSpawn.Enqueue(m, ix, 0x1C);                            // $36FA
        SetScript(m, ix, 0x0315);
        Animate(m, ix);
    }

    // $371A (tipo $39, muneca): estados $372D $3758
    static void H371A(byte[] m, int ix)
    {
        int s = Jump(m, 0x3721, (m[ix + 0x1F] - 1) & 0xFF);
        switch (s)
        {
            case 0x372D: H372D(m, ix); return;
            case 0x3758:
                if (OffScreen(m, ix)) return;                        // $3758 $2D7F
                if (!Touch(m, ix, 0x080C)) return;                   // $375C $205A
                TitleRom.Sound(m, 0x13);                             // $3763 $0440
                if (m[0xC312] != 0) { Collected(m, ix, 0x03); return; } // $3768-$376E $32E9
                m[0xC312] = 0x01;                                    // $3771
                m[0xC314]++;                                         // $3776
                Collected(m, ix, 0x04);                              // $377A $32E9
                return;
            default: Missing(s); return;
        }
    }

    // $3725
    static void H3725(byte[] m, int ix)
    {
        m[ix + 0x18] = 0x39;
        ObjectSpawn.Enqueue(m, ix, 0x39);                            // $372A
        H372D(m, ix);
    }

    // $372D
    static void H372D(byte[] m, int ix)
    {
        m[ix + 0x1F] = 0x02;
        SetScript(m, ix, m[0xC312] != 0 ? 0x030F : 0x0314);          // $3731-$373D
        Animate(m, ix);
        int de = GroundY(m, ix);                                     // $3743
        int l = m[ix + 0x19];
        int hl = ((l << 1) & 0xFF) | ((l & 0x80) != 0 ? 0xFF00 : 0); // $3746-$374F
        Ww(m, ix + 0x12, hl + de);
    }

    // $37C3: estados $37D2 $46E1 $37DC $4B8E
    static void H37C3(byte[] m, int ix)
    {
        int s = Jump(m, 0x37CA, (m[ix + 0x1F] - 1) & 0xFF);
        switch (s)
        {
            case 0x37D2:
                m[ix + 0x14] = 0xFF;                                 // $37D5
                Start(m, ix, 0x0221);
                return;
            case 0x46E1: H34F7(m, ix); return;                       // $46E1 = $34F7
            case 0x37DC:
                if (OffScreen(m, ix)) return;                        // $37DC
                Animate(m, ix);
                Ww(m, ix + 0x12, GroundY(m, ix));                    // $37E3
                MoveX(m, ix);                                        // $37EC $2725
                if (Rw(m, ix + 0x12) != Ahead(m, ix, 0x10))          // $37EF-$37FD
                    m[ix + 0x14] = 0x00;
                if (Weapon(m, ix, 0x0808, out int iy))               // $3803
                {
                    m[ix + 0x1F]++;                                  // $380B
                    Killed(m, ix, iy, 0x00, 0x0229);
                    return;
                }
                if (!Touch(m, ix, 0x0808)) return;                   // $3816 (DE sigue en $0808)
                if (m[ix + 0x18] == 0x33) { H36E7(m, ix); return; }  // $381D
                if (PlayerHit(m, ix)) return;
                m[0xC85E] |= 0x04;                                   // $382B
                return;
            case 0x4B8E: H4B8E(m, ix); return;
            default: Missing(s); return;
        }
    }

    // $3894: estados ($1F & $0F) $38A7 $38C4 $390F $39AB $4696
    static void H3894(byte[] m, int ix)
    {
        int s = Jump(m, 0x389D, ((m[ix + 0x1F] & 0x0F) - 1) & 0xFF);
        switch (s)
        {
            case 0x38A7:
            {
                int hl = Word(m, 0x5EE1, m[ix + 0x18] & 0x0F);       // $38A7-$38AF
                m[ix + 0x19] = m[(hl + 1) & 0xFFFF];                 // $38B7 d
                m[ix + 0x1A] = m[hl];                                // $38BA e
                m[ix + 0x1B] = m[(hl + 2) & 0xFFFF];                 // $38BD
                m[ix + 0x1F]++;
                return;
            }
            case 0x38C4:
            {
                int hl = (Rw(m, 0xC850) + m[ix + 0x19]) & 0xFFFF;    // $38C4-$38CC
                if (hl < Rw(m, ix + 0x10)) return;                   // $38D6
                m[ix + 0x1F]++;
                int l = 0x13;                                        // $38DA
                int a = (m[ix + 0x18] - 0x08) & 0xFF;
                if (a != 0x0D)
                {
                    l = a;                                           // $38E6
                    if ((m[0xC476] & 0x02) != 0)
                    {
                        m[ix + 0x1F] |= 0x80;                        // $38EE
                        l = (0x15 + l) & 0xFF;
                    }
                }
                SetScript(m, ix, 0x0300 | l);                        // $38F6
                Animate(m, ix);
                Ww(m, ix + 0x12, Hover(m, ix, m[ix + 0x1A]));        // $38FE $3A4A
                m[ix + 0x19] = 0xB4;
                return;
            }
            case 0x390F:
                if (--m[ix + 0x19] == 0) { H3914(m, ix); return; }   // $390F
                if (OffScreen(m, ix)) return;                        // $391B
                {
                    int a = m[ix + 0x18] & 0x0F;                     // $391F
                    if (a != 0x05 && (m[ix + 0x1F] & 0x80) == 0 && (m[0xC476] & 0x02) != 0)
                    {
                        m[ix + 0x1F] |= 0x80;                        // $3935
                        SetScript(m, ix, 0x0300 | ((a + 0x1D) & 0xFF));
                        Animate(m, ix);
                    }
                }
                if (!Touch(m, ix, 0x080C)) return;                   // $3944
                if ((m[ix + 0x18] & 0x0F) == 0x05)                   // $394B
                {
                    m[ix + 0x1F]++;                                  // $3954
                    Ww(m, ix + 0x10, Rw(m, ix + 0x10) + 0x60);
                    Ww(m, ix + 0x12, Rw(m, ix + 0x12) - 0x38);
                    m[ix + 0x19] = 0x78;
                    SetScript(m, ix, 0x030D);
                    Animate(m, ix);
                    return;
                }
                TitleRom.Sound(m, 0x0D);                             // $3986
                Vitality(m, 0x80 << ((m[ix + 0x1B] & 0x0F) + 1));    // $398B-$3999
                Collected(m, ix, (m[ix + 0x1F] & 0x80) == 0 ? m[ix + 0x1B] : 0x03); // $399C-$39A8
                return;
            case 0x39AB:
                if (OffScreen(m, ix)) return;                        // $39AB
                if (--m[ix + 0x19] == 0) { H3914(m, ix); return; }   // $39AF
                if (!Touch(m, ix, 0x0808)) return;                   // $39B5
                TitleRom.Sound(m, 0x0D);                             // $39BC
                Vitality(m, 0x0500);                                 // $39C1
                Collected(m, ix, (m[0xC476] & 0x02) == 0 ? m[ix + 0x1B] : 0x04); // $39C7-$39D4
                return;
            case 0x4696: H4696(m, ix); return;
            default: Missing(s); return;
        }
    }

    // $3914: $1F = 5 y $46A6
    static void H3914(byte[] m, int ix)
    {
        m[ix + 0x1F] = 0x05;
        H46A6(m, ix);
    }

    // $3408 (huevo): estados ($1F & $0F) $34E1 $34F7 $3500 $3549 $3585 | $3425 $35B6 $3495 $34A4 $34CA
    static void H3408(byte[] m, int ix)
    {
        EggStep(m, ix, Jump(m, 0x3411, ((m[ix + 0x1F] & 0x0F) - 1) & 0xFF));
    }

    // Estados del huevo compartidos por $3408 y $339C (mismas direcciones de ROM)
    static void EggStep(byte[] m, int ix, int s)
    {
        switch (s)
        {
            case 0x34E1:
                SetScript(m, ix, 0x0300);
                Animate(m, ix);
                Ww(m, ix + 0x12, GroundY(m, ix));                    // $34EA
                m[ix + 0x1F]++;
                return;
            case 0x34F7: H34F7(m, ix); return;
            case 0x3500:
                if (OffScreen(m, ix)) return;                        // $3500
                if (Touch(m, ix, 0x0812))                            // $3504 -> $352F
                {
                    m[ix + 0x1F]++;
                    m[ix + 0x14] = m[0xC854];
                    m[ix + 0x16] = 0x00;
                    m[ix + 0x17] = 0x40;
                    SetScript(m, ix, 0x0301);
                    Animate(m, ix);
                    return;
                }
                if (!Weapon(m, ix, 0x0608, out int iy)) return;      // $350C
                if ((m[ix + 0x1F] & 0x80) != 0)                      // $3513
                {
                    m[ix + 0x1F]++;
                    H356F(m, ix);
                    return;
                }
                m[ix + 0x1F] |= 0x80;                                // $351E
                m[iy + 0x19] = 0x01;
                SetScript(m, ix, 0x0301);
                Animate(m, ix);
                return;
            case 0x3549:
                if (!ScreenBounds.OnScreen(m, ix)) { m[ix + 0x1F] = 0x00; return; }
                MoveX(m, ix);                                        // $3554
                MoveY(m, ix);                                        // $3557 $43B2
                m[ix + 0x17] -= 0x03;
                if (Rw(m, ix + 0x12) >= GroundAt(m, ix, Rw(m, ix + 0x10))) return; // $3562-$356E
                H356F(m, ix);
                return;
            case 0x3585:
                if (!ScreenBounds.OnScreen(m, ix)) { m[ix + 0x1F] = 0x00; return; }
                if ((m[ix + 0x1E] & 0x01) != 0) return;              // $3590
                m[ix + 0x1F]++;
                m[ix + 0x16] = 0x00;
                m[ix + 0x14] = (byte)(Rw(m, 0xC850) >= Rw(m, ix + 0x10) ? 0x20 : 0xE0); // $359C-$35AE
                m[ix + 0x17] = 0x20;
                return;
            case 0x3425: H3425(m, ix); return;
            case 0x35B6: H35B6(m, ix); return;
            case 0x3495:
                Animate(m, ix);                                      // $3495 $1FC0
                Ww(m, 0xC409, 0x0258);                               // $3498
                m[ix + 0x1F]++;                                      // $349E
                H368D(m, ix);                                        // $34A1 jp $368D
                return;
            case 0x34A4:
                m[0xC46E] = 0x01;                                    // $34A4
                if ((m[0xC85F] & 0x0F) >= 0x02)                      // $34A9-$34B0
                {
                    Animate(m, ix);                                  // $34B2 $1FC0
                    H368D(m, ix);                                    // $34B5
                    int t = (Rw(m, 0xC409) - 1) & 0xFFFF;            // $34B8-$34BC
                    Ww(m, 0xC409, t);
                    if (t != 0) return;                              // $34BF
                }
                m[ix + 0x17] = 0x10;                                 // $34C2
                m[ix + 0x1F]++;
                return;
            case 0x34CA:
                if (!ScreenBounds.OnScreen(m, ix))                      // $34CA
                {
                    m[ix + 0x1F] = 0x00;                             // $34D0
                    m[0xC46E] = 0x00;
                    return;
                }
                m[0xC46E] = 0x01;                                    // $34D9
                H3671(m, ix);                                        // $34DE jp $3671
                return;
            default: Missing(s); return;
        }
    }

    // $339C (huevo, variante): estados ($1F & $0F) $34E1 $34F7 $3500 $3549 $3585 | $33B9 $35B6 $33BF $33C7 $3671
    static void H339C(byte[] m, int ix)
    {
        int s = Jump(m, 0x33A5, ((m[ix + 0x1F] & 0x0F) - 1) & 0xFF);
        switch (s)
        {
            case 0x33B9:
                SetScript(m, ix, 0x020F);                                // $33B9 jp $3478 $1FA3
                Animate(m, ix);                                          // $347B $1FC0
                m[ix + 0x1F]++;                                          // $347E
                m[0xC478] = m[0xC3DF];                                   // $3481
                m[0xC477] = 0x01;                                        // $3487
                m[ix + 0x1B] = 0xB4;                                     // $348C
                ObjectSpawn.Enqueue(m, ix, 0x1A);                        // $3490 $2D1A
                return;
            case 0x33BF:
                TitleRom.Sound(m, 0x04);                                 // $33BF $0440
                m[ix + 0x1F]++;                                          // $33C4
                H33C7(m, ix);                                            // sigue en $33C7
                return;
            case 0x33C7: H33C7(m, ix); return;
            case 0x3671: H3671(m, ix); return;
            default: EggStep(m, ix, s); return;
        }
    }

    // $33C7: sigue al jugador; con $C46E o $C440 pasa a bonus ($3374), si no espera a $C85F/$C46A
    static void H33C7(byte[] m, int ix)
    {
        Animate(m, ix);                                              // $33C7 $1FC0
        H368D(m, ix);                                                // $33CA
        int e = 0x08;                                                // $33CD
        if (m[0xC46E] != 0 || m[0xC440] != 0)                        // $33CF-$33D9
        {
            if (m[0xC46E] == 0) e--;                                 // $33DB
            TitleRom.Sound(m, 0x05);                                 // $33DC $0440
            H3374(m, ix, e);                                         // $33E1 jp $3374
            return;
        }
        m[0xC46F] = 0x01;                                            // $33E4
        if ((m[0xC85F] & 0x0F) >= 0x02 && m[0xC46A] >= 0x03) return; // $33E9-$33F7
        m[ix + 0x1F]++;                                              // $33F8
        m[ix + 0x17] = 0x20;
        m[0xC46F] = 0x00;                                            // $33FF
        TitleRom.Sound(m, 0x05);                                     // $3403 jp $0440
    }

    // $43DA: estados $43ED $4411 $444E $44C6 $44CF $4B8E (tabla $43E1)
    static void H43DA(byte[] m, int ix)
    {
        int s = Jump(m, 0x43E1, (m[ix + 0x1F] - 1) & 0xFF);
        switch (s)
        {
            case 0x43ED:
            {
                SetScript(m, ix, 0x020B);                                // $43ED $1FA3
                Animate(m, ix);                                          // $43F3 $1FC0
                int de = GroundY(m, ix);                                 // $43F6 $293A
                int l = (m[ix + 0x19] & 0xE0) >> 1;                      // $43F9-$43FF rrca
                Ww(m, ix + 0x12, (de + l) & 0xFFFF);                     // $4402
                m[ix + 0x16] = 0x00;                                     // $4409
                m[ix + 0x1F]++;
                return;
            }
            case 0x4411:
            {
                if (OffScreen(m, ix)) return;                            // $4411 $2D7F
                if ((m[ix + 0x1E] & 0x20) == 0) return;                  // $4415
                if (H44E1(m, ix)) return;                                // $441A
                Animate(m, ix);                                          // $441D $1FC0
                if ((m[ix + 0x19] & 0x02) != 0)                          // $4420
                {
                    int hl = (Rw(m, 0xC850) + 0x80) & 0xFFFF;            // $4426-$442C
                    if (hl < Rw(m, ix + 0x10)) return;                   // $4434 ret c
                }
                m[ix + 0x1F]++;                                          // $4437
                int b = m[ix + 0x19];
                int c = b & 0xE0;
                if ((((b & 0x1C) << 3) & 0xFF) != c) return;             // $443A-$4448
                m[ix + 0x1F] = 0x04;                                     // $4449
                return;
            }
            case 0x444E:
            {
                OffScreen(m, ix);                                        // $444E $2D7F (sin ret c)
                int b0 = m[ix + 0x19];
                int c = (b0 & 0xE0) >> 1;                                // $4455-$4458 rrca
                int b = ((b0 & 0x1C) << 2) & 0xFF;                       // $445A-$445E
                bool up = b < c;                                         // $445F jr c,$4480
                m[ix + 0x17] = (byte)(up ? 0xF0 : 0x10);                 // $4480 / $4462
                MoveY(m, ix);                                            // $43B2
                int de = (GroundY(m, ix) + b) & 0xFFFF;                  // $293A, $446E-$4471
                int y = Rw(m, ix + 0x12);
                bool reached = up ? y < de : y >= de;                    // $447C jr c / $449A jr nc
                if (reached)
                {
                    Ww(m, ix + 0x12, de);                                // $449C
                    int a = (c >> 2) | ((b << 1) & 0xFF) | (b0 & 0x03);  // $44A2-$44AE
                    m[ix + 0x19] = (byte)a;
                    m[ix + 0x1F]++;                                      // $44B2
                    if ((a & 0x01) != 0)                                 // $44B5
                    {
                        m[ix + 0x1F]++;                                  // $44B9
                        m[ix + 0x1A] = 0x08;
                    }
                }
                if (H44E1(m, ix)) return;                                // $44C0
                Animate(m, ix);                                          // $44C3 jp $1FC0
                return;
            }
            case 0x44C6:
                if (H44E1(m, ix)) return;                                // $44C6
                Animate(m, ix);                                          // $44C9
                OffScreen(m, ix);                                        // $44CC jp $2D7F
                return;
            case 0x44CF:
                if (H44E1(m, ix)) return;                                // $44CF
                Animate(m, ix);                                          // $44D2
                OffScreen(m, ix);                                        // $44D5
                if (--m[ix + 0x1A] != 0) return;                         // $44D8
                m[ix + 0x1F] = 0x03;                                     // $44DC
                return;
            case 0x4B8E: H4B8E(m, ix); return;
            default: Missing(s); return;
        }
    }

    // $44E1: arma (derribado, sale del manejador: true) o choque con el jugador.
    // $3359/$334A con pop hl solo saltan el resto de $44E1 y vuelven al estado (false).
    static bool H44E1(byte[] m, int ix)
    {
        if (Weapon(m, ix, 0x0504, out int iy))                       // $44E1 $20A9
        {
            m[ix + 0x1F] = 0x06;                                     // $44E9 pop hl
            Killed(m, ix, iy, 0x02, 0x0217);                         // $44F3 jp $4BB4
            return true;
        }
        if (!Touch(m, ix, 0x0504)) return false;                     // $44F6 $205A
        if (PlayerHit(m, ix)) return false;                          // $44FD $3359 $334A
        m[0xC85E] |= 0x04;                                           // $4503
        return false;
    }

    // $42E6 (tipo $0B): estados $42F7 $34F7 $4319 $4B8E (tabla $42EF, indice IX+$1F & $0F).
    // IX+$1F bit6/bit7: fases del vuelo en $4319 ($434C).
    static void H42E6(byte[] m, int ix)
    {
        int s = Jump(m, 0x42EF, ((m[ix + 0x1F] & 0x0F) - 1) & 0xFF);
        switch (s)
        {
            case 0x42F7:
                SetScript(m, ix, 0x0208);                                // $42F7 $1FA3
                Animate(m, ix);                                          // $42FD $1FC0
                Ww(m, ix + 0x12, (GroundY(m, ix) + 0x68) & 0xFFFF);      // $4300 $293A, $4303-$430A
                m[ix + 0x19] = 0xF8;                                     // $430D
                m[ix + 0x1A] = 0x04;                                     // $4311
                m[ix + 0x1F]++;                                          // $4315
                return;
            case 0x34F7: H34F7(m, ix); return;
            case 0x4319:
            {
                if (OffScreen(m, ix)) return;                            // $4319 $2D7F
                Animate(m, ix);                                          // $431D $1FC0
                MoveX(m, ix);                                            // $4320 $2725
                MoveY(m, ix);                                            // $4323 $43B2
                if (Weapon(m, ix, 0x0606, out int iy))                   // $4326 $20A9
                {
                    m[ix + 0x1F]++;                                      // $432E
                    Killed(m, ix, iy, 0x02, 0x0213);                     // $4331 jp $4BB4
                    return;
                }
                if (Touch(m, ix, 0x0606))                                // $4339 $205A
                {
                    if (PlayerHit(m, ix)) return;                        // $4341 $3359 $334A
                    m[0xC85E] |= 0x04;                                   // $4347
                }
                byte f = m[ix + 0x1F];
                if ((f & 0x80) == 0)                                     // $434C
                {
                    if ((f & 0x40) != 0)                                 // $4352
                    {
                        int a = m[ix + 0x17] + 0x04;                     // $4384
                        if (a > 0xFF)                                    // $4389 jr nc
                        {
                            a = 0x00;                                    // $438B
                            m[ix + 0x1A] = 0x04;
                            m[ix + 0x1F] |= 0x80;                        // $4390
                        }
                        m[ix + 0x17] = (byte)a;                          // $4394
                        return;
                    }
                    int hl = (Rw(m, 0xC850) + 0x00E0) & 0xFFFF;          // $4358-$435E
                    if (hl >= Rw(m, ix + 0x10))                          // $4365 sbc, jr c,$4398
                    {
                        m[ix + 0x1F] |= 0x40;                            // $436A
                        m[ix + 0x17] = 0xA0;                             // $436E
                        m[ix + 0x14] = 0xF0;                             // $4372
                        m[ix + 0x19] = 0xF0;                             // $4376
                        if (m[0xC441] == 0) return;                      // $437A
                        m[ix + 0x19] = 0xE8;                             // $437F
                        return;
                    }
                }
                m[ix + 0x17] = (byte)(m[ix + 0x17] + m[ix + 0x19]);      // $4398-$439E
                if (--m[ix + 0x1A] != 0) return;                         // $43A1
                m[ix + 0x19] = (byte)(-m[ix + 0x19]);                    // $43A5 neg
                m[ix + 0x1A] = 0x08;                                     // $43AD
                return;
            }
            case 0x4B8E: H4B8E(m, ix); return;
            default: Missing(s); return;
        }
    }

    // $3CA8 (tipos $06 y $0C; $34 al tocar va a $36E7): estado 1 espera, 2 avanza, otro $3D27 (caida)
    static void H3CA8(byte[] m, int ix)
    {
        int st = m[ix + 0x1F];
        if (st == 0x01)                                                  // $3CAB
        {
            if ((m[ix + 0x1E] & 0x20) == 0) return;                      // $3CAF
            m[ix + 0x1F] = 0x02;                                         // $3CB4
            SetScript(m, ix, 0x0210);                                    // $3CB8 $1FA3
            m[ix + 0x14] = (byte)(m[ix + 0x18] == 0x06 ? 0xF0 : 0xE0);   // $3CBE-$3CC9
            return;
        }
        if (st != 0x02) { H3D27(m, ix); return; }                        // $3CCD jr nz,$3D27
        if (OffScreen(m, ix)) return;                                    // $3CD1 $2D7F
        MoveX(m, ix);                                                    // $3CD5 $2725
        Animate(m, ix);                                                  // $3CD8 $1FC0
        Ww(m, ix + 0x12, GroundY(m, ix));                                // $3CDB $293A, $3CDE
        if (Touch(m, ix, 0x0A08))                                        // $3CE4 $205A
        {
            if (m[ix + 0x18] == 0x34) { H36E7(m, ix); return; }          // $3CEC jp z,$36E7
            if (PlayerHit(m, ix)) return;                                // $3CF4 $3359 $334A
            m[0xC85E] |= 0x04;                                           // $3CFA
            return;
        }
        if (!Weapon(m, ix, 0x0A08, out _)) return;                       // $3D00 $20A9
        m[ix + 0x1F] = 0x03;                                             // $3D07
        Score(m, 0x03);                                                  // $3D0B $0E48
        SetScript(m, ix, 0x0209);                                        // $3D10 $1FA3
        Animate(m, ix);                                                  // $3D16 $1FC0
        Ww(m, ix + 0x16, 0x5EC7);                                        // $3D19 tabla de caida
        TitleRom.Sound(m, 0x1B);                                         // $3D22 jp $0440
    }

    // $35B6 (estado 7 del huevo, hada): se acerca al jugador ($C850-$20, $C852+$20) durante IX+$1B frames
    static void H35B6(byte[] m, int ix)
    {
        if (!ScreenBounds.Visible(m, ix))                            // $35B6 bit5 (o franja ancha)
        {
            m[0xC477] = 0x00;                                        // $35BC
            m[ix + 0x1F] = 0x00;
            return;
        }
        Animate(m, ix);                                              // $35C4 $1FC0
        MoveX(m, ix);                                                // $35C7 $2725
        MoveY(m, ix);                                                // $35CA $43B2
        m[ix + 0x1A] = 0x00;                                         // $35CD
        int dx = (Rw(m, 0xC850) - 0x20 - Rw(m, ix + 0x10)) & 0xFFFF; // $35D1-$35DF
        if (((Rw(m, 0xC850) + 0xFFE0) & 0xFFFF) < Rw(m, ix + 0x10))  // $35E1 (acarreo)
        {
            m[ix + 0x1A]++;                                          // $35E3
            dx = (-dx) & 0xFFFF;                                     // $35E6-$35EC
        }
        int dy = (Rw(m, 0xC852) + 0x20 - Rw(m, ix + 0x12)) & 0xFFFF; // $35EF-$35FD
        if (((Rw(m, 0xC852) + 0x20) & 0xFFFF) < Rw(m, ix + 0x12))    // $35FF
        {
            m[ix + 0x1A] |= 0x02;                                    // $3601
            dy = (-dy) & 0xFFFF;                                     // $3605-$360B
        }
        if ((((dy >> 8) | (dx >> 8)) == 0 && (dy & 0xFF) < 0x06 && (dx & 0xFF) < 0x0C) // $360C-$3618
            || --m[ix + 0x1B] == 0)                                  // $3625
        {
            m[0xC477] = 0x00;                                        // $361A
            m[ix + 0x1A] = 0x00;
            m[ix + 0x1F]++;
            return;
        }
        m[ix + 0x14] = H3642(m, ix, dx, m[ix + 0x14]);               // $362A-$3630
        m[ix + 0x1A] >>= 1;                                          // $3634 srl
        m[ix + 0x17] = H3642(m, ix, dy, m[ix + 0x17]);               // $3638-$363E
    }

    // $3642: velocidad c + min(de/4, 3) hacia el signo de IX+$1A bit0, limitada a +/-$28
    static byte H3642(byte[] m, int ix, int de, int c)
    {
        de >>= 2;                                                    // $3642-$3648
        int a = (de >> 8) != 0 || (de & 0xFF) >= 0x03 ? 0x03 : de & 0xFF; // $364A-$3653
        if ((m[ix + 0x1A] & 0x01) == 0)                              // $3655
        {
            a = (a + c) & 0xFF;                                      // $365B
            if ((c & 0x80) != 0) return (byte)a;                     // $365C
            return (byte)(a < 0x28 ? a : 0x28);                      // $365F-$3662
        }
        a = ((-a) + c) & 0xFF;                                       // $3665-$3667
        if ((c & 0x80) == 0) return (byte)a;                         // $3668
        return (byte)(a >= 0xD9 ? a : 0xD8);                         // $366B-$366E
    }

    // $3671: sigue al jugador (IX+$14 = $C854 + $20) mientras siga activo
    static void H3671(byte[] m, int ix)
    {
        if (!ScreenBounds.OnScreen(m, ix)) { m[ix + 0x1F] = 0x00; return; } // $3671-$367B
        m[ix + 0x14] = (byte)(m[0xC854] + 0x20);                     // $367C-$3681
        MoveX(m, ix);                                                // $3684 $2725
        MoveY(m, ix);                                                // $3687 $43B2
        Animate(m, ix);                                              // $368A jp $1FC0
    }

    // $368D: coloca el objeto en ($C850-$20, $C852+$20)
    static void H368D(byte[] m, int ix)
    {
        Ww(m, ix + 0x10, Rw(m, 0xC850) - 0x20);                      // $368D-$3697
        Ww(m, ix + 0x12, Rw(m, 0xC852) + 0x20);                      // $369A-$36A4
    }

    // $3425 (estado 6 del huevo): contenido segun $A400[$C301], 2 bits elegidos por bits 4-5 de IX+$11
    static void H3425(byte[] m, int ix)
    {
        if (m[ix + 0x18] != 0x17 && m[0xC407] == 0)                  // $3425-$3430 jp z,$452E
        {
            m[0xC407] |= 0x01;                                       // $452E
            SetScript(m, ix, 0x0100);                                // $4533 $1FA3
            Animate(m, ix);                                          // $4539 $1FC0
            m[ix + 0x1F] = 0x02;                                     // $453C
            m[ix + 0x18] = 0x18;                                     // $4540
            ObjectSpawn.Enqueue(m, ix, 0x18);                        // $4545 $2D1A
            return;
        }
        int t = m[0xA400 + m[0xC301]];                               // $3433-$343C
        int b = ((m[ix + 0x11] >> 4) & 0x03) + 1;                    // $343D-$3447
        for (int i = 0; i < b; i++) t = ((t << 2) | (t >> 6)) & 0xFF; // $3448-$344B rlca x2, djnz
        switch (Jump(m, 0x3452, t & 0x03))                           // $344D $0ABB
        {
            case 0x3A80:
                if (m[0xC408] != 0) { H4593(m, ix); return; }        // $3A80 jp nz,$4593
                m[ix + 0x1F] = 0x02;                                 // $3A87
                SetScript(m, ix, 0x0102);                            // $3A8B $1FA3
                Animate(m, ix);
                m[ix + 0x18] = 0x19;                                 // $3A94
                ObjectSpawn.Enqueue(m, ix, 0x19);                    // $3A99 $2D1A
                return;
            case 0x4638:
                SetScript(m, ix, 0x0307);                            // $4638 $1FA3
                Animate(m, ix);                                      // $463E $1FC0
                m[ix + 0x1F] = 0x02;                                 // $4641
                m[ix + 0x18] = 0x1E;                                 // $4645
                ObjectSpawn.Enqueue(m, ix, 0x1E);                    // $464A $2D1A
                return;
            case 0x45DB:
                if (m[0xC476] != 0) { H4593(m, ix); return; }        // $45DB-$45E0 jp nz,$4593
                m[0xC476] |= 0x01;                                   // $45E3
                m[ix + 0x1F] = 0x02;                                 // $45E5
                SetScript(m, ix, 0x0312);                            // $45E9 $1FA3
                Animate(m, ix);
                m[ix + 0x18] = 0x1F;                                 // $45F2
                ObjectSpawn.Enqueue(m, ix, 0x1F);                    // $45F7 $2D1A
                return;
            case 0x345A:
                TitleRom.Sound(m, 0x02);                             // $345A $0440
                TitleRom.Sound(m, 0x0A);                             // $345F $0440
                TitleRom.Sound(m, m[0xC310] < 0x03 ? 0xCE : 0xCF);   // $3464-$346E $0440
                m[0xC475] = 0x00;                                    // $3471
                SetScript(m, ix, 0x020E);                            // $3475 $1FA3
                Animate(m, ix);                                      // $347B $1FC0
                m[ix + 0x1F]++;                                      // $347E
                m[0xC478] = m[0xC3DF];                               // $3481
                m[0xC477] = 0x01;                                    // $3487
                m[ix + 0x1B] = 0xB4;                                 // $348C
                ObjectSpawn.Enqueue(m, ix, 0x1A);                    // $3490 $2D1A (IX+$18 no cambia)
                return;
        }
    }

    // $4593: contenido ya recogido -> tipo $1B
    static void H4593(byte[] m, int ix)
    {
        m[ix + 0x1F] = 0x02;                                         // $4593
        SetScript(m, ix, 0x0310);                                    // $4597 $1FA3
        Animate(m, ix);                                              // $459D $1FC0
        m[ix + 0x18] = 0x1B;                                         // $45A0
        ObjectSpawn.Enqueue(m, ix, 0x1B);                            // $45A5 $2D1A
    }

    // $356F
    static void H356F(byte[] m, int ix)
    {
        SetScript(m, ix, 0x0315);
        Animate(m, ix);
        Ww(m, ix + 0x12, GroundY(m, ix));                            // $3578
        m[ix + 0x1F]++;
    }

    // $330A (tipo $40, enemigo eliminado con bonus): estados $3315 $4687
    static void H330A(byte[] m, int ix)
    {
        int s = Jump(m, 0x3311, (m[ix + 0x1F] - 1) & 0xFF);
        switch (s)
        {
            case 0x3315:
                if (!ScreenBounds.OnScreen(m, ix))                      // $3315
                {
                    m[ix + 0x1F] = 0x00;
                    Score(m, m[ix + 0x1A]);                          // $331F $0E48
                    return;
                }
                Animate(m, ix);                                      // $3325
                if ((m[ix + 0x1E] & 0x01) == 0) return;
                m[ix + 0x1F]++;
                ObjectSpawn.Enqueue(m, ix, 0x44);                    // $3330
                Score(m, m[ix + 0x1A]);
                m[ix + 0x1A] = 0x3C;
                SetScript(m, ix, 0x0400 | m[ix + 0x1B]);             // $333F
                Animate(m, ix);
                return;
            case 0x4687: H4687(m, ix); return;
            default: Missing(s); return;
        }
    }

    // $423E (tipo $09): estados $424D $34F7 $4271 $4B8E
    static void H423E(byte[] m, int ix)
    {
        int s = Jump(m, 0x4245, (m[ix + 0x1F] - 1) & 0xFF);
        switch (s)
        {
            case 0x424D:
                m[ix + 0x14] = (byte)(m[0xC441] != 0 ? 0xE8 : 0xF0);  // $424D-$4257
                m[ix + 0x19] = 0x02;
                m[ix + 0x1A] = (byte)((m[ix + 0x18] & 0x01) != 0 ? 0x10 : 0x18); // $425E
                Start(m, ix, 0x0211);                                // $426E $4050
                return;
            case 0x34F7: H34F7(m, ix); return;
            case 0x4271:
            {
                if (OffScreen(m, ix)) return;                        // $4271
                Animate(m, ix);
                MoveX(m, ix);                                        // $4278 $2725
                MoveY(m, ix);                                        // $427B $43B2
                if (Weapon(m, ix, 0x0608, out int iy))               // $4281
                {
                    m[ix + 0x1F]++;                                  // $4286
                    Killed(m, ix, iy, 0x03, 0x0212);                 // $428E $4BB4
                    return;
                }
                if (Touch(m, ix, 0x0608))                            // $4294
                {
                    if (PlayerHit(m, ix)) return;
                    m[0xC85E] |= 0x04;                               // $429F (sigue en $42A4)
                }
                m[ix + 0x17] = (byte)(m[ix + 0x17] + m[ix + 0x19]);  // $42A4
                if (--m[ix + 0x1A] != 0) return;                     // $42AD
                m[ix + 0x19] = (byte)(-m[ix + 0x19]);                // $42B1
                m[ix + 0x1A] = (byte)((m[ix + 0x18] & 0x01) != 0 ? 0x20 : 0x30); // $42B9
                return;
            }
            case 0x4B8E: H4B8E(m, ix); return;
            default: Missing(s); return;
        }
    }

    // $4025 (tipos $36 $37, trampolin): estados $403A $34F7 $4063 $4074 $4084 $34F7 $4084. Con el jugador
    // encima ($3F17, IX+$19 bit4) cambia de script; en $4084 lo impulsa ($C857 = $70, $C85F = $83).
    static void H4025(byte[] m, int ix)
    {
        int s = Jump(m, 0x402C, (m[ix + 0x1F] - 1) & 0xFF);
        switch (s)
        {
            case 0x403A:
                m[ix + 0x19] = 0x00;                                 // $403A
                if ((m[ix + 0x18] & 0x01) != 0) { Start(m, ix, 0x0207); return; } // $4041-$4045 jr nz,$4050
                m[ix + 0x1F] = 0x06;                                 // $4047
                SetScript(m, ix, 0x0222);                            // $404B jr $4053 $1FA3
                Animate(m, ix);                                      // $4056 $1FC0
                Ww(m, ix + 0x12, GroundY(m, ix));                    // $4059 $293A, $405C
                return;
            case 0x34F7: H34F7(m, ix); return;
            case 0x4063:
                if (OffScreen(m, ix)) return;                        // $4063 $2D7F
                H3F17(m, ix);                                        // $4067
                if ((m[ix + 0x19] & 0x10) == 0) return;              // $406A
                Start(m, ix, 0x0222);                                // $406F jr $4050
                return;
            case 0x4074:
                if (OffScreen(m, ix)) return;                        // $4074 $2D7F
                H3F17(m, ix);                                        // $4078
                if ((m[ix + 0x19] & 0x10) != 0) return;              // $407B
                m[ix + 0x1F]++;                                      // $4080
                return;
            case 0x4084:
                if (OffScreen(m, ix)) return;                        // $4084 $2D7F
                H3F17(m, ix);                                        // $4088
                if ((m[ix + 0x19] & 0x10) == 0) return;              // $408B
                TitleRom.Sound(m, 0x19);                             // $4090 $0440
                if ((m[0xC85F] & 0x0F) >= 0x02)                      // $4095-$409C
                {
                    m[0xC857] = 0x70;                                // $409E
                    m[0xC85F] = 0x83;                                // $40A3
                }
                m[ix + 0x1F] = 0x02;                                 // $40A8
                Start(m, ix, 0x0207);                                // $40AC jr $4050
                return;
            default: Missing(s); return;
        }
    }

    // $40C4 (tipo $0D): estados $40D7 $40F6 $41D4 $41D4 $41D4 $3D27 (fijo, 2 golpes).
    // $40B1 (tipos $07 $08 $22): estados $40D7 $40F6 $410A $4167 $41D4 $3D27 (salta; Enemies.Extra.cs).
    static void H40C4(byte[] m, int ix) => H40C4(m, ix, 0x40CB);
    static void H40B1(byte[] m, int ix) => H40C4(m, ix, 0x40B8);

    static void H40C4(byte[] m, int ix, int table)
    {
        int s = Jump(m, table, (m[ix + 0x1F] - 1) & 0xFF);
        switch (s)
        {
            case 0x410A: H410A(m, ix); return;
            case 0x4167: H4167(m, ix); return;
            case 0x40D7:
                SetScript(m, ix, (m[ix + 0x18] & 0x01) != 0 ? 0x020C : 0x0214); // $40D7-$40E3 $1FA3
                Animate(m, ix);                                      // $40E6 $1FC0
                Ww(m, ix + 0x12, GroundAt(m, ix, Rw(m, ix + 0x10))); // $40E9 $294F
                m[ix + 0x1F]++;                                      // $40F2
                return;
            case 0x40F6:
                if ((m[ix + 0x1E] & 0x20) == 0) return;              // $40F6
                Animate(m, ix);                                      // $40FB $1FC0
                m[ix + 0x1A] = 0x02;                                 // $40FE golpes
                m[ix + 0x1B] = 0x04;                                 // $4102
                m[ix + 0x1F]++;                                      // $4106
                return;
            case 0x41D4:
                if (H41DA(m, ix)) return;                            // $41D4
                OffScreen(m, ix);                                    // $41D7 jp $2D7F
                return;
            case 0x3D27: H3D27(m, ix); return;
            default: Missing(s); return;
        }
    }

    // $41DA: arma ($20A9, de=$0A0C) y choque ($205A, de=$0A0A). true si sale del manejador (pop hl).
    static bool H41DA(byte[] m, int ix)
    {
        if (Weapon(m, ix, 0x0A0C, out _))                            // $41DA-$41E0
        {
            if (--m[ix + 0x1A] == 0)                                 // $41E3 -> $4200
            {
                m[ix + 0x1F] = 0x06;                                 // $4201
                Ww(m, ix + 0x16, 0x5EC7);                            // $4205 tabla de caida
                Score(m, 0x04);                                      // $420E $0E48
                SetScript(m, ix, 0x0216);                            // $4213 $1FA3
                Animate(m, ix);                                      // $4219 $1FC0
                ObjectSpawn.Enqueue(m, ix, m[ix + 0x18]);            // $421C $2D1A
                TitleRom.Sound(m, 0x1B);                             // $4222 $0440
                return true;
            }
            ObjectSpawn.Enqueue(m, ix, m[ix + 0x18] == 0x0D ? 0x42 : 0x43); // $41E8-$41F2 $2D1A
            Score(m, 0x01);                                          // $41F5 $0E48
            m[ix + 0x1E] |= 0x04;                                    // $41FA
        }
        Animate(m, ix);                                              // $4227 $1FC0
        if (!Touch(m, ix, 0x0A0A)) return false;                     // $422A $205A
        if (!PlayerHit(m, ix)) m[0xC85E] |= 0x04;                    // $4232 $3359 $334A, $4238
        return true;
    }

    // $3B6F (tipo $04): estados $3B80 $3BD6 $3BDF $3BD6 $3BF2
    static void H3B6F(byte[] m, int ix)
    {
        int s = Jump(m, 0x3B76, (m[ix + 0x1F] - 1) & 0xFF);
        switch (s)
        {
            case 0x3B80:
            {
                m[ix + 0x1B] = 0x00;                                 // $3B80
                int t = m[ix + 0x18];
                int b;
                if ((m[ix + 0x10] & 0x10) == 0)                      // $3B84
                {
                    m[ix + 0x1F] = 0x04;                             // $3B8A
                    SetScript(m, ix, 0x0200 | ((t - 1) & 0xFF));
                    Ww(m, ix + 0x12, GroundAt(m, ix, Rw(m, ix + 0x10))); // $3B98 $294F
                    b = t == 0x04 ? 0xE0 : 0xC8;                     // $3BA1
                }
                else
                {
                    m[ix + 0x1B]++;                                  // $3BB8
                    m[ix + 0x1F]++;
                    SetScript(m, ix, 0x0200 | ((t + 0x1B) & 0xFF));  // $3BBE
                    b = t == 0x04 ? 0x20 : 0x38;                     // $3BC9
                }
                m[ix + 0x14] = (byte)b;                              // $3BAC
                m[ix + 0x16] = 0x00;
                m[ix + 0x17] = 0x00;
                return;
            }
            case 0x3BD6:                                             // $3BD6 = $34F7
                // Solo WIDESCREEN con NewLogicWide=T: la roca en espera (estado 4) arranca al tocar el borde
                // derecho de la franja ancha (ScreenBounds.Right), no el del original: entra rodando desde fuera
                // y no se ve su nacimiento. NewLogicWide=F u ORIGINAL / NO (InWideView = false): igual que $34F7.
                if (ScreenBounds.NewLogicWide && m[ix + 0x1F] == 0x04 && (m[ix + 0x1E] & 0x20) == 0 && InWideView(m, ix))
                { m[ix + 0x1F]++; return; }
                H34F7(m, ix); return;
            case 0x3BDF:
                if (ScreenBounds.Visible(m, ix)) return;             // $3BDF bit5 (o franja ancha)
                Ww(m, ix + 0x12, GroundAt(m, ix, Rw(m, ix + 0x10))); // $3BE4 $294F
                m[ix + 0x1F] = 0x05;
                return;
            case 0x3BF2: H3BF2(m, ix); return;
            default: Missing(s); return;
        }
    }

    // $3BF2: estado 5 de $3B6F (rebota sobre el terreno)
    static void H3BF2(byte[] m, int ix)
    {
        if ((m[ix + 0x1E] & 0x20) == 0 && (m[ix + 0x14] & 0x80) != 0 // $3BF2-$3C0A
            && Rw(m, 0xC850) >= Rw(m, ix + 0x10)
            && !InWideView(m, ix))                                   // solo WIDESCREEN (ScreenBounds)
        {
            m[ix + 0x1F] = 0x00;                                     // $3C0C
            return;
        }
        int de = Ahead(m, ix, m[ix + 0x08] >> 1);                    // $3C11 $2959
        if (((Rw(m, ix + 0x12) + 0x08) & 0xFFFF) < de)               // $3C19-$3C26
        {
            TitleRom.Sound(m, 0x0F);                                 // $3C28 $0440
            m[ix + 0x14] = (byte)(-m[ix + 0x14]);                    // $3C2D
            int l = (m[ix + 0x17] + 0x18) & 0xFF;                    // $3C35
            m[ix + 0x17] = (byte)l;
            int a = (m[ix + 0x18] - 1) & 0xFF;                       // $3C3E
            if ((l & 0x80) == 0) a = (a + 0x1C) & 0xFF;
            SetScript(m, ix, 0x0200 | a);                            // $3C48 $1FA3
        }
        if (Weapon(m, ix, 0x060E, out int iy)) m[iy + 0x19] = 0x01;  // $3C4E-$3C56
        Animate(m, ix);                                              // $3C5A
        MoveX(m, ix);                                                // $3C5D $2725
        int v = (m[ix + 0x17] - 0x02) & 0xFF;                        // $3C60
        if ((v & 0x80) != 0 && v < 0xA1) v = 0xA0;                   // $3C65-$3C6C
        m[ix + 0x17] = (byte)v;
        MoveY(m, ix);                                                // $3C71 $43B2
        int sum = Terrain2660(m, Rw(m, ix + 0x10)) + (m[ix + 0x08] >> 1); // $3C74 $294F
        int cy = sum > 0xFFFF ? 1 : 0;                               // carry de add hl,de ($2978)
        de = sum & 0xFFFF;
        if (Rw(m, ix + 0x12) - de - cy < 0 && (m[ix + 0x17] & 0x80) != 0) // $3C7D sbc sin or a
        {
            m[ix + 0x17] = (byte)((sbyte)(byte)(-m[ix + 0x17]) >> 1); // $3C88 neg, sra
            Ww(m, ix + 0x12, de);                                    // $3C8F
        }
        if (!Touch(m, ix, 0x060E)) return;                           // $3C95
        if (PlayerHit(m, ix)) return;                                // $3C9C
        m[0xC85E] |= 0x04;                                           // $3CA2
    }

    // $3AB6 (tipo $03): estados 1-3 por cp, el resto $3D27
    static void H3AB6(byte[] m, int ix)
    {
        switch (m[ix + 0x1F])
        {
            case 0x01:
            {
                int hl = (Rw(m, 0xC850) + (m[0xC441] != 0 ? 0x0080 : 0x0100)) & 0xFFFF; // $3ABD-$3ACC
                if (hl < Rw(m, ix + 0x10)) return;                   // $3AD3 sbc
                m[ix + 0x1F] = 0x02;                                 // $3AD7
                SetScript(m, ix, 0x0202);
                Animate(m, ix);
                Ww(m, ix + 0x12, GroundY(m, ix) + 0xFFE8);           // $3AE4-$3AEE
                return;
            }
            case 0x02:
            {
                int de = GroundY(m, ix);                             // $3AF6 $293A
                int y = Rw(m, ix + 0x12);
                if (y < de) { Ww(m, ix + 0x12, y + 0x04); return; }  // $3B00-$3B14
                Ww(m, ix + 0x12, de);                                // $3B15
                m[ix + 0x1F] = 0x03;
                return;
            }
            case 0x03:
            {
                Animate(m, ix);                                      // $3B25
                if (OffScreen(m, ix)) return;                        // $3B28
                if (Touch(m, ix, 0x0A06))                            // $3B2F
                {
                    if (m[ix + 0x18] == 0x35) { H36E7(m, ix); return; } // $3B37
                    if (PlayerHit(m, ix)) return;
                    m[0xC85E] |= 0x04;                               // $3B42
                    return;
                }
                if (!Weapon(m, ix, 0x0A06, out _)) return;           // $3B4B
                m[ix + 0x1F] = 0x04;                                 // $3B4F
                Score(m, 0x03);                                      // $3B55 $0E48
                SetScript(m, ix, 0x0206);
                Animate(m, ix);
                Ww(m, ix + 0x16, 0x5EC7);                            // $3B61 tabla de caida
                TitleRom.Sound(m, 0x1B);                             // $3B6C $0440
                return;
            }
            default: H3D27(m, ix); return;
        }
    }

    // $3D27: Y += desplazamiento con signo de la tabla apuntada por IX+$16 (avanza hasta el $80)
    static void H3D27(byte[] m, int ix)
    {
        if (!ScreenBounds.OnScreen(m, ix)) { m[ix + 0x1F] = 0x00; return; }
        int hl = Rw(m, ix + 0x16);                                   // $3D32
        int e = m[hl];
        hl = (hl + 1) & 0xFFFF;
        if (m[hl] != 0x80) Ww(m, ix + 0x16, hl);                     // $3D3A-$3D42
        Ww(m, ix + 0x12, Rw(m, ix + 0x12) + (sbyte)e);               // $3D45-$3D56
    }

    // $3D5A (tipos $3A $3B $3E $3F, plataformas): $1F = 1 inicializa, 2 espera IX+$1E bit5, resto $3DFF + $2D7F
    static void H3D5A(byte[] m, int ix)
    {
        int s = m[ix + 0x1F];
        if (s == 0x01)
        {
            m[ix + 0x1F] = 0x02;                                     // $3D61
            int n = (m[(Rw(m, 0xC44A) + m[ix + 0x11]) & 0xFFFF] & 0xF0) >> 4; // $3D65-$3D73 $0A92
            SetScript(m, ix, 0x0200 | m[0x5F1F + n]);                // $3D75-$3D7E $0A92 $1FA3
            Animate(m, ix);                                          // $3D81 $1FC0
            if (((0x28 >> (n & 0x07)) & 1) != 0)                     // $3D84-$3D8C rrca n+1 veces
                ObjectSpawn.Enqueue(m, ix, 0x45);                    // $3D90 $2D1A
            int de = GroundY(m, ix);                                 // $3D93 $293A
            int a = m[ix + 0x19] & 0xF8;                             // $3D96-$3DA1
            int hl = ((a << 1) & 0xFF) | ((a & 0x80) != 0 ? 0xFF00 : 0);
            Ww(m, ix + 0x12, hl + de);                               // $3DA2-$3DA6
            return;
        }
        if (s == 0x02)
        {
            if ((m[ix + 0x1E] & 0x20) == 0) return;                  // $3DAE
            m[ix + 0x1F] = 0x03;                                     // $3DB3
            m[ix + 0x16] = 0x00;
            m[ix + 0x17] = 0x00;
            m[ix + 0x19] &= 0x07;                                    // $3DBF
            if ((m[ix + 0x18] & 0x01) == 0 && (m[ix + 0x19] & 0x04) == 0) return; // $3DC7-$3DD1
            int b = m[ix + 0x19];                                    // $3DD2
            if ((b & 0x04) == 0) return;
            m[ix + 0x17] = (byte)((b & 0x02) != 0 ? 0x18 : 0x10);    // $3DD8-$3DE0
            return;
        }
        H3DFF(m, ix);                                                // $3DE4
        OffScreen(m, ix);                                            // $3DE7 $2D7F
    }

    // $3DFF: con IX+$18 bit2 espera a que el jugador pase X - 2*IX+$1A; luego tabla $3E3A por IX+$19 & 7
    // (+8 si IX+$18 bit0 = 0)
    static void H3DFF(byte[] m, int ix)
    {
        if ((m[ix + 0x18] & 0x04) != 0)
        {
            int e = m[ix + 0x1A];                                    // $3E0B sla e con signo
            int de = ((e << 1) & 0xFF) | ((e & 0x80) != 0 ? 0xFF00 : 0);
            int t = (Rw(m, ix + 0x10) - de) & 0xFFFF;
            if (t >= Rw(m, 0xC850)) { H3F17(m, ix); return; }        // $3E1F
            m[ix + 0x18] &= 0xFB;                                    // $3E22
            m[ix + 0x1B] = 0x00;
        }
        int a = m[ix + 0x19] & 0x07;                                 // $3E2A
        if ((m[ix + 0x18] & 0x01) == 0) a += 0x08;
        int h = Jump(m, 0x3E3A, a);                                  // $3E37 $0ABB
        switch (h)
        {
            case 0x3F17: H3F17(m, ix); return;
            case 0x3E54:
                if (!ScreenBounds.OnScreen(m, ix)) { m[ix + 0x1F] = 0x00; return; } // $3E54
                if ((m[ix + 0x19] & 0x08) == 0)                      // $3E5F
                {
                    H3F17(m, ix);                                    // $3E65
                    if ((m[ix + 0x19] & 0x10) != 0) m[ix + 0x19] |= 0x08; // $3E68-$3E6D
                    return;
                }
                {
                    int v = (m[ix + 0x17] + 0x02) & 0xFF;            // $3E72
                    if (v >= 0x60) v = 0x60;
                    m[ix + 0x17] = (byte)v;
                }
                H3EAF(m, ix);                                        // $3E80
                return;
            case 0x3E82:
                if ((m[ix + 0x19] & 0x08) == 0)
                {
                    m[ix + 0x17]++;                                  // $3E88
                    if (m[ix + 0x17] >= 0x20)
                    {
                        m[ix + 0x17] = 0x20;                         // $3E92
                        m[ix + 0x19] |= 0x08;
                    }
                }
                else if (--m[ix + 0x17] == 0)                        // $3E9C
                    m[ix + 0x19] = (byte)((m[ix + 0x19] & 0xF7) ^ 0x01); // $3EA1-$3EAC
                H3EAF(m, ix);
                return;
            case 0x3EAF: H3EAF(m, ix); return;
            case 0x3F8D: H3F8D(m, ix); return;
            case 0x3FF4:
                H3F17(m, ix);                                        // $3FF4
                if ((m[ix + 0x19] & 0x10) == 0) return;
                {
                    int c = 0x11;                                    // $3FFC
                    if (m[0xC479] >= 0x03)
                    {
                        c = 0x14;                                    // $4005
                        m[ix + 0x17] = 0x10;
                        m[0xC440] = 0x01;
                        TitleRom.Sound(m, m[0xC313] != 0 ? 0xC9 : 0xCA); // $4010-$401A $0440
                    }
                    m[ix + 0x19] = (byte)c;                          // $401D
                    m[ix + 0x18] = 0x3B;
                }
                return;
            default: Missing(h); return;
        }
    }

    // $3EAF: Y += IX+$17 (4.4, negativo si IX+$19 bit0) con fraccion en IX+$16; con IX+$18 bit0 e IX+$19
    // bit2, limite contra la camara $C444 (ajusta IX+$13 y quita IX+$19 bit4). Sigue en $3F17.
    static void H3EAF(byte[] m, int ix)
    {
        int hl = m[ix + 0x17];                                       // $3EAF
        if ((m[ix + 0x19] & 0x01) != 0) hl = (-hl) & 0xFFFF;         // $3EBA-$3EBE cpl, dec h, inc hl
        int sign = hl >> 8;                                          // $3EBF
        int sh = (hl << 4) & 0xFFFF;                                 // $3EC0-$3EC3
        int f = (sh & 0xFF) + m[ix + 0x16];                          // $3ECE
        m[ix + 0x16] = (byte)f;
        int de = (((sign << 8) | (sh >> 8)) + Rw(m, ix + 0x12) + (f >> 8)) & 0xFFFF; // $3ED4 adc
        Ww(m, ix + 0x12, de);
        if ((m[ix + 0x18] & 0x01) == 0 || (m[ix + 0x19] & 0x04) == 0) { H3F17(m, ix); return; } // $3EDD-$3EE7
        int cam = Rw(m, 0xC444);                                     // $3EE9
        if ((m[ix + 0x19] & 0x01) == 0)
        {
            int c = (-(m[ix + 0x08] >> 1) + 0x18) & 0xFF;            // $3EF2-$3EFB
            if (((cam - c) & 0xFFFF) >= de) { H3F17(m, ix); return; } // $3EFE-$3F03
            m[ix + 0x13]--;                                          // $3F05
        }
        else
        {
            if (((cam - 0x100) & 0xFFFF) < de) { H3F17(m, ix); return; } // $3F0A-$3F0E
            m[ix + 0x13]++;                                          // $3F10
        }
        m[ix + 0x19] &= 0xEF;                                        // $3F13
        H3F17(m, ix);
    }

    // $3F17: jugador encima (IX+$19 bit4): lo lleva con la plataforma ($C852, $C466/$C467) o lo suelta;
    // si no, $3F77 comprueba si se sube ($3DEB, $2120)
    static void H3F17(byte[] m, int ix)
    {
        if ((m[ix + 0x19] & 0x10) == 0)
        {
            int e = m[ix + 0x04];                                    // $3F77 $3DEB
            if ((e & 0x80) != 0) e = (-e) & 0xFF;
            e = (e << 1) & 0xFF;
            int d = ((m[ix + 0x08] >> 1) - 0x04 + 0x02) & 0xFF;      // $3DF6-$3DFD, $3F7A-$3F7B
            if (!H2120(m, ix, d << 8 | e)) return;                   // $3F7C
            if ((m[0xC85F] & 0x0F) != 0x02) return;                  // $3F80
            m[ix + 0x19] |= 0x10;                                    // $3F88
            return;
        }
        if ((m[0xC85F] & 0x0F) != 0x02) { m[ix + 0x19] &= 0xEF; return; } // $3F1D-$3F24
        int a = ((m[ix + 0x08] >> 1) - 0x04) & 0xFF;                 // $3F26-$3F2D
        a = ((m[0xC848] >> 1) + a) & 0xFF;                           // $3F2E-$3F33
        int y = (Rw(m, ix + 0x12) + a) & 0xFFFF;                     // $3F37-$3F3D
        Ww(m, 0xC852, y);                                            // $3F3E
        int dx = Math.Abs(Rw(m, 0xC850) - Rw(m, ix + 0x10));         // $3F42-$3F56
        if (dx > 0xFF) { m[ix + 0x19] &= 0xEF; return; }             // $3F57
        int w = m[ix + 0x04];                                        // $3F5B add a,a / neg
        w = (w & 0x80) != 0 ? (-((w << 1) & 0xFF)) & 0xFF : (w << 1) & 0xFF;
        if (((w + 0x04) & 0xFF) < dx) { m[ix + 0x19] &= 0xEF; return; } // $3F63-$3F66
        m[0xC466] = 0x01;                                            // $3F68
        Ww(m, 0xC467, y);                                            // $3F6D
    }

    // $3F8D: pausa IX+$16; luego movimiento horizontal IX+$17 fotogramas a +/-$18 o $20 (IX+$19 bit1),
    // arrastrando al jugador ($26FA con IX = $C840 y $C854 = IX+$14) si esta encima
    static void H3F8D(byte[] m, int ix)
    {
        if (m[ix + 0x16] != 0) { m[ix + 0x16]--; H3F17(m, ix); return; } // $3F8D-$3F96
        if (m[ix + 0x17] == 0)                                       // $3F99
        {
            int a = 0x18, c = 0x55;                                  // $3F9F
            if ((m[ix + 0x19] & 0x02) != 0) { a = 0x20; c = 0x40; }
            m[ix + 0x17] = (byte)c;                                  // $3FAD
            if ((m[ix + 0x19] & 0x01) == 0) a = (-a) & 0xFF;         // $3FB0-$3FB6
            m[ix + 0x14] = (byte)a;
        }
        H3F17(m, ix);                                                // $3FBB
        if ((m[ix + 0x19] & 0x10) != 0)                              // $3FBE
        {
            byte save = m[0xC854];                                   // $3FC9
            m[0xC854] = m[ix + 0x14];                                // $3FD1
            PlayerRoutine.H26FA(m, 0xC840);                          // $3FD4
            m[0xC854] = save;                                        // $3FD8
        }
        if (--m[ix + 0x17] != 0) { MoveX(m, ix); return; }           // $3FDD jp nz,$2725
        m[ix + 0x14] = 0x00;                                         // $3FE3
        m[ix + 0x19] ^= 0x01;
        m[ix + 0x16] = 0x0C;
    }

    // $2120: jugador sobre el objeto (e = margen X - 4, d = margen Y - 1, Y jugador - $C848/2), solo si
    // $C85F & $0F >= 2. Al acertar $C466 = 1 y $C467 = Y + IX+$08/2 + $0C.
    static bool H2120(byte[] m, int ix, int de)
    {
        if ((m[0xC85F] & 0x0F) < 0x02) return false;                 // $2120
        int dx = Math.Abs(Rw(m, 0xC850) - Rw(m, ix + 0x10));         // $212A-$213E
        if (dx > 0xFF || ((0x04 + (de & 0xFF)) & 0xFF) < dx) return false; // $2140-$2148
        int t = (Rw(m, 0xC852) - (m[0xC848] >> 1)) & 0xFFFF;         // $214B-$2157
        int dy = Math.Abs(t - Rw(m, ix + 0x12));                     // $2159-$216A
        if (dy > 0xFF || ((0x01 + (de >> 8)) & 0xFF) < dy) return false; // $216C-$2174
        m[0xC466] = 0x01;                                            // $2176
        Ww(m, 0xC467, Rw(m, ix + 0x12) + (((m[ix + 0x08] >> 1) + 0x0C) & 0xFF)); // $217B-$218C
        return true;
    }

    // $377F (tipo $01): $1F = 1 arranca con $0200; si no, $4805 y choque con el jugador
    static void H377F(byte[] m, int ix)
    {
        if (m[ix + 0x1F] == 0x01) { Start(m, ix, 0x0200); return; } // $3782-$3789 $4050
        if (ScreenBounds.Visible(m, ix)) Animate(m, ix);             // $378C $4805 bit5 (o franja ancha)
        if ((m[ix + 0x1E] & 0x80) == 0)                              // $378F
        {
            if ((m[ix + 0x1E] & 0x20) != 0) m[ix + 0x1E] |= 0x80;    // $3795-$379A
            return;
        }
        if (OffScreen(m, ix)) return;                                // $379F
        if (!Touch(m, ix, 0x0708)) return;                           // $37A3
        if (m[ix + 0x18] == 0x32) { H36E7(m, ix); return; }          // $37AA
        if (PlayerHit(m, ix)) return;                                // $37B2 $3359 $334A
        m[0xC85E] |= 0x04;                                           // $37B8
        m[0xC859] = 0x01;                                            // $37BD
    }

    // $4509 (tipo $18): estados $4518 $34F7 $4548 $4551 ($C407 bit0 = uno ya activo)
    static void H4509(byte[] m, int ix)
    {
        int s = Jump(m, 0x4510, (m[ix + 0x1F] - 1) & 0xFF);
        switch (s)
        {
            case 0x4518:
                if (m[0xC407] != 0) { m[ix + 0x1F] = 0x00; return; } // $4518-$4522
                Ww(m, ix + 0x12, Hover(m, ix, 0x20));                // $4523 $3A4A
                m[0xC407] |= 0x01;                                   // $452E
                SetScript(m, ix, 0x0100);                            // $4533 $1FA3
                Animate(m, ix);                                      // $4539 $1FC0
                m[ix + 0x1F] = 0x02;                                 // $453C
                m[ix + 0x18] = 0x18;                                 // $4540
                ObjectSpawn.Enqueue(m, ix, 0x18);                    // $4545 $2D1A
                return;
            case 0x34F7: H34F7(m, ix); return;
            case 0x4548: H34F7(m, ix); return;                       // $4548 = $34F7
            case 0x4551:
                if (!ScreenBounds.Visible(m, ix))                    // $4551 bit5 (o franja ancha)
                {
                    m[ix + 0x1F] = 0x00;                             // $4557
                    m[0xC407] &= 0xFE;                               // $455B
                    return;
                }
                if (!Touch(m, ix, 0x0606)) return;                   // $4561 $205A
                m[0xC407] = 0x02;                                    // $4568
                TitleRom.Sound(m, 0x0A);                             // $456D $0440
                m[ix + 0x1F] = 0x00;                                 // $4572
                Collected(m, ix, 0x01);                              // $4576 $32E9
                return;
            default: Missing(s); return;
        }
    }

    // $3A68 (tipo $19): estados $3A75 $34F7 $3A9C ($C408 != 0: ya recogido, pasa por $4593)
    static void H3A68(byte[] m, int ix)
    {
        int s = Jump(m, 0x3A6F, (m[ix + 0x1F] - 1) & 0xFF);
        switch (s)
        {
            case 0x3A75:
                Ww(m, ix + 0x12, Hover(m, ix, 0x10));                // $3A75 $3A4A
                if (m[0xC408] != 0)                                  // $3A80 jp nz,$4593
                {
                    m[ix + 0x1F] = 0x02;                             // $4593
                    SetScript(m, ix, 0x0310);                        // $4597 $1FA3
                    Animate(m, ix);
                    m[ix + 0x18] = 0x1B;                             // $45A0
                    ObjectSpawn.Enqueue(m, ix, 0x1B);                // $45A5 $2D1A
                    return;
                }
                m[ix + 0x1F] = 0x02;                                 // $3A87
                SetScript(m, ix, 0x0102);                            // $3A8B $1FA3
                Animate(m, ix);
                m[ix + 0x18] = 0x19;                                 // $3A94
                ObjectSpawn.Enqueue(m, ix, 0x19);                    // $3A99 $2D1A
                return;
            case 0x34F7: H34F7(m, ix); return;
            case 0x3A9C:
                if (OffScreen(m, ix)) return;                        // $3A9C $2D7F
                if (!Touch(m, ix, 0x0814)) return;                   // $3AA0 $205A
                m[0xC408] = 0x01;                                    // $3AA7
                TitleRom.Sound(m, 0x0A);                             // $3AAC $0440
                Collected(m, ix, 0x02);                              // $3AB1 $32E9
                return;
            default: Missing(s); return;
        }
    }

    // $46B4 (tipo $1A): estados $46C7 $46E1 $46EA $473C $4696 $4687
    static void H46B4(byte[] m, int ix)
    {
        int s = Jump(m, 0x46BB, (m[ix + 0x1F] - 1) & 0xFF);
        switch (s)
        {
            case 0x46C7:
                SetScript(m, ix, 0x0313);                            // $46CA $1FA3
                Animate(m, ix);
                Ww(m, ix + 0x12, GroundY(m, ix) + 0xFFF0);           // $46D0-$46DA
                m[ix + 0x1F]++;
                return;
            case 0x46E1: H34F7(m, ix); return;                       // $46E1 = $34F7
            case 0x46EA:
                if (OffScreen(m, ix)) return;                        // $46EA
                if (Touch(m, ix, 0x2008) && m[0xC85F] == 0x03)       // $46EE-$46FB
                    Ww(m, ix + 0x10, Rw(m, ix + 0x10) + 0x0040);     // $46FD-$470A
                else
                {
                    if (!Weapon(m, ix, 0x1208, out int iy)) return;  // $470F
                    m[iy + 0x19] = 0x01;                             // $4716
                }
                SetScript(m, ix, 0x0300 | (((m[0xC0DA] + 1) & 0x03) + 0x02)); // $471A-$4725
                Animate(m, ix);
                Ww(m, ix + 0x12, GroundY(m, ix));                    // $472B
                m[ix + 0x1A] = 0xB4;
                m[ix + 0x1F]++;
                return;
            case 0x473C:
                if (OffScreen(m, ix)) return;                        // $473C
                if (--m[ix + 0x1A] == 0)                             // $4740
                {
                    m[ix + 0x1F]++;
                    H46A6(m, ix);                                    // $4748
                    return;
                }
                if (!Touch(m, ix, 0x0610)) return;                   // $474B
                TitleRom.Sound(m, 0x13);                             // $4752 $0440
                int a = (m[0xC0DA] + 1) & 0x03;                      // $4757
                m[0xC0DA] = (byte)a;
                a = (a - 1) & 0xFF;
                if ((a & 0x80) != 0)                                 // $4761
                {
                    m[ix + 0x1F] = 0x00;                             // $4764
                    TitleRom.LifeUp(m);                              // $4768 jp $0E2E (vida extra, $14D0 en VRAM $E884)
                    return;
                }
                m[ix + 0x1F] = 0x06;                                 // $476B
                m[ix + 0x1A] = 0x3C;
                Score(m, a + 0x08);                                  // $4774 $0E48
                SetScript(m, ix, 0x0400 | (a + 0x04));               // $477A $1FA3
                Animate(m, ix);
                ObjectSpawn.Enqueue(m, ix, 0x10);                    // $4785 $2D1A
                return;
            case 0x4696: H4696(m, ix); return;
            case 0x4687: H4687(m, ix); return;
            default: Missing(s); return;
        }
    }

    // $46A6
    static void H46A6(byte[] m, int ix)
    {
        SetScript(m, ix, 0x0306);
        Animate(m, ix);
        ObjectSpawn.Enqueue(m, ix, 0x19);                            // $46AF $2D1A
    }

    // $4687 (tipo $41, marcador de puntos)
    static void H4687(byte[] m, int ix)
    {
        if (ScreenBounds.Visible(m, ix) && --m[ix + 0x1A] != 0) return;   // bit5 (o franja ancha)
        m[ix + 0x1F] = 0x00;                                         // $4691
    }

    // $4696
    static void H4696(byte[] m, int ix)
    {
        if (!ScreenBounds.Visible(m, ix)) { m[ix + 0x1F] = 0x00; return; }   // bit5 (o franja ancha)
        Animate(m, ix);                                              // $469C
        if ((m[ix + 0x1E] & 0x01) != 0) m[ix + 0x1F] = 0x00;
    }

    // $34F7 / $46E1: avanza de estado cuando el objeto entra en pantalla (IX+$1E bit5)
    static void H34F7(byte[] m, int ix)
    {
        if ((m[ix + 0x1E] & 0x20) == 0) return;
        m[ix + 0x1F]++;
    }

    // $4B8E: enemigo derribado cayendo
    static void H4B8E(byte[] m, int ix)
    {
        if (!ScreenBounds.OnScreen(m, ix)) { m[ix + 0x1F] = 0x00; return; }
        Animate(m, ix);                                              // $4B99
        MoveX(m, ix);
        MoveY(m, ix);
        int a = (m[ix + 0x17] - 0x04) & 0xFF;                        // $4BA2
        if ((a & 0x80) != 0 && a < 0xA1) a = 0xA0;                   // $4BA7-$4BAE
        m[ix + 0x17] = (byte)a;
    }

    // $4A21 (tipo $24): estados $4A30 $4A46 $4A5B $3D27
    static void H4A21(byte[] m, int ix)
    {
        int s = Jump(m, 0x4A28, (m[ix + 0x1F] - 1) & 0xFF);
        switch (s)
        {
            case 0x4A30:
                SetScript(m, ix, 0x021B);                                // $4A30 $1FA3
                Animate(m, ix);                                          // $4A36 $1FC0
                m[ix + 0x1F]++;                                          // $4A39
                Ww(m, ix + 0x12, H4BD0(m, ix));                          // $4A3C
                return;
            case 0x4A46:
                if ((m[ix + 0x1E] & 0x20) == 0) return;                  // $4A46 ret z
                m[ix + 0x14] = 0xE0;                                     // $4A4B
                m[ix + 0x16] = 0x00;
                m[ix + 0x17] = 0x50;
                m[ix + 0x1F]++;                                          // $4A57
                return;
            case 0x4A5B:
            {
                if ((m[ix + 0x1E] & 0x20) == 0) { m[ix + 0x1F] = 0x00; return; } // $4A5B-$4A65
                H4A87(m, ix);                                            // $4A66
                int de = H4BD0(m, ix);                                   // $4A69
                if (Rw(m, ix + 0x12) >= de) return;                      // $4A6C-$4A75 ret nc
                m[ix + 0x17] = (byte)-m[ix + 0x17];                      // $4A76 neg
                SetScript(m, ix, 0x021B);                                // $4A7E $1FA3
                Animate(m, ix);                                          // $4A84 jp $1FC0
                return;
            }
            case 0x3D27: H3D27(m, ix); return;
            default: Missing(s); return;
        }
    }

    // $4A87: movimiento, gravedad (-2) y choques de $4A21. Todos los caminos vuelven a $4A69
    // (el pop hl de $3359/$334A solo quita la vuelta a $4ACA).
    static void H4A87(byte[] m, int ix)
    {
        Animate(m, ix);                                              // $4A87 $1FC0
        MoveX(m, ix);                                                // $4A8A $2725
        MoveY(m, ix);                                                // $4A8D $43B2
        m[ix + 0x17] = (byte)(m[ix + 0x17] - 0x02);                  // $4A90
        if (Weapon(m, ix, 0x0612, out _))                            // $4A98 $20A9
        {
            Score(m, 0x03);                                          // $4AA0 $0E48
            Ww(m, ix + 0x16, 0x5EC7);                                // $4AA5 tabla de caida de $3D27
            m[ix + 0x1F] = 0x04;                                     // $4AAE
            SetScript(m, ix, 0x022C);                                // $4AB2 $1FA3
            Animate(m, ix);                                          // $4AB8 $1FC0
            TitleRom.Sound(m, 0x1B);                                 // $4ABB jp $0440
            return;
        }
        if (!Touch(m, ix, 0x0612)) return;                           // $4AC0 $205A
        if (PlayerHit(m, ix)) return;                                // $4AC7 $3359 $334A
        m[0xC85E] |= 0x04;                                           // $4ACD
    }

    // $4AD3 (tipo $23): estados $4AE2 $34F7 $4B0C $4B8E
    static void H4AD3(byte[] m, int ix)
    {
        int s = Jump(m, 0x4ADA, (m[ix + 0x1F] - 1) & 0xFF);
        switch (s)
        {
            case 0x4AE2:
            {
                SetScript(m, ix, 0x0227);                            // $4AE2 $1FA3
                Animate(m, ix);                                      // $4AE8 $1FC0
                Ww(m, ix + 0x12, H4BD0(m, ix));                      // $4AEB
                byte a = (byte)((m[ix + 0x18] & 0x02) != 0 ? 0x58 : 0x70); // $4AF4-$4AFC
                m[ix + 0x17] = a;
                m[ix + 0x1A] = 0x01;
                m[ix + 0x1B] = a;
                m[ix + 0x1F]++;                                      // $4B08
                return;
            }
            case 0x34F7: H34F7(m, ix); return;
            case 0x4B0C:
            {
                if (OffScreen(m, ix)) return;                        // $4B0C $2D7F
                Animate(m, ix);                                      // $4B10 $1FC0
                if (m[ix + 0x1A] != 0)                               // $4B13
                {
                    if (--m[ix + 0x1A] == 0)                         // $4B19
                    {
                        TitleRom.Sound(m, 0x18);                     // $4B1E $0440
                        SetScript(m, ix, 0x021C);                    // $4B23 $1FA3
                    }
                }
                else
                {
                    MoveY(m, ix);                                    // $4B2B $43B2
                    int de = H4BD0(m, ix);                           // $4B2E
                    int a = m[ix + 0x17];
                    if ((a & 0x80) != 0)                             // $4B34
                    {
                        m[ix + 0x17] = (byte)(a - 0x02);             // $4B38
                        if (Rw(m, ix + 0x12) < de)                   // $4B3D-$4B46
                        {
                            Ww(m, ix + 0x12, de);                    // $4B48
                            m[ix + 0x17] = m[ix + 0x1B];
                            m[ix + 0x1A] = 0x10;
                        }
                    }
                    else
                    {
                        m[ix + 0x17] = (byte)(a - 0x03);             // $4B5A
                        if (a < 0x03) SetScript(m, ix, 0x0227);      // $4B5F-$4B64 $1FA3
                    }
                }
                if (Weapon(m, ix, 0x060A, out int iy))               // $4B67 $20A9
                {
                    m[ix + 0x1F] = 0x04;                             // $4B6F
                    Killed(m, ix, iy, 0x03, 0x0228);                 // $4B78 $4BB4
                    return;
                }
                if (!Touch(m, ix, 0x060A)) return;                   // $4B7B $205A
                if (PlayerHit(m, ix)) return;                        // $4B82 $3359 $334A
                m[0xC85E] |= 0x04;                                   // $4B88
                return;
            }
            case 0x4B8E: H4B8E(m, ix); return;
            default: Missing(s); return;
        }
    }

    // $4BD0: DE = Y suelo ($293A) - (IX+$08 + $28)
    static int H4BD0(byte[] m, int ix)
    {
        int e = (m[ix + 0x08] + 0x28) & 0xFF;                        // $4BD4
        return (GroundY(m, ix) - e) & 0xFFFF;
    }

    // ---------------- subrutinas ----------------

    // $4BB4: derribado por el arma IY (a = codigo de puntos, hl = script)
    static void Killed(byte[] m, int ix, int iy, int a, int hl)
    {
        Score(m, a);                                                 // $4BB5 $0E48
        m[ix + 0x14] = (byte)((sbyte)m[iy + 0x14] >> 1);             // $4BB9 sra
        m[ix + 0x17] = 0x30;
        SetScript(m, ix, hl);
        Animate(m, ix);
        TitleRom.Sound(m, 0x1B);                                     // $4BCB $0440
    }

    // $3359 + $334A. true si el camino sale del manejador (pop hl) por $3374.
    static bool PlayerHit(byte[] m, int ix)
    {
        if (m[0xC46E] != 0)                                          // $3359
        {
            int a = m[0xC475];                                       // $335F
            int e = a;
            a++;
            if (a >= 0x16) a = 0x15;
            m[0xC475] = (byte)a;
            H3374(m, ix, m[0x5760 + e]);
            return true;
        }
        if (m[0xC408] != 0)                                          // $334A
        {
            m[0xC85F] = 0x05;
            H3374(m, ix, 0x00);
            return true;
        }
        return false;
    }

    // $3374: pasa a tipo $40 (bonus) con puntos e
    static void H3374(byte[] m, int ix, int e)
    {
        m[ix + 0x1A] = m[0x5757 + e];
        m[ix + 0x1B] = (byte)e;
        m[ix + 0x18] = 0x40;
        m[ix + 0x1F] = 0x01;
        ObjectSpawn.Enqueue(m, ix, 0x1C);                            // $3389
        SetScript(m, ix, 0x0315);
        Animate(m, ix);
        TitleRom.Sound(m, 0x1C);                                     // $3397
    }

    // $32E9: fruta recogida, pasa a tipo $41 (marcador) con puntos e
    static void Collected(byte[] m, int ix, int e)
    {
        m[ix + 0x18] = 0x41;
        m[ix + 0x1A] = 0x3C;
        Score(m, m[0x5757 + e]);                                     // $32F8 $0E48
        SetScript(m, ix, 0x0400 | e);                                // $32FC
        Animate(m, ix);
        ObjectSpawn.Enqueue(m, ix, 0x10);                            // $3305
    }

    // $2725: X += IX+$14 (4.4 con signo), fraccion en IX+$15
    internal static void MoveX(byte[] m, int ix)
    {
        int v = m[ix + 0x14];
        int f = ((v << 4) & 0xFF) + m[ix + 0x15];
        m[ix + 0x15] = (byte)f;
        Ww(m, ix + 0x10, Rw(m, ix + 0x10) + ((sbyte)v >> 4) + (f >> 8));
    }

    // $43B2: Y += IX+$17 (4.4 con signo), fraccion en IX+$16
    internal static void MoveY(byte[] m, int ix)
    {
        int v = m[ix + 0x17];
        int f = ((v << 4) & 0xFF) + m[ix + 0x16];
        m[ix + 0x16] = (byte)f;
        Ww(m, ix + 0x12, Rw(m, ix + 0x12) + ((sbyte)v >> 4) + (f >> 8));
    }

    // $2959: Y suelo ($2660) en X +/- e (segun signo de IX+$14) + IX+$08/2
    internal static int Ahead(byte[] m, int ix, int e)
    {
        int de = (m[ix + 0x14] & 0x80) != 0 ? 0xFF00 | ((-e) & 0xFF) : e;
        return GroundAt(m, ix, (Rw(m, ix + 0x10) + de) & 0xFFFF);
    }

    // $296D ($294F con x = X): $2660(x) + IX+$08/2
    internal static int GroundAt(byte[] m, int ix, int x) => (Terrain2660(m, x) + (m[ix + 0x08] >> 1)) & 0xFFFF;

    // $2660: altura de terreno (variante de $26BD: $FF completo y bloques 4-9 tambien por $2698)
    internal static int Terrain2660(byte[] m, int x)
    {
        int d = x >> 8, e = x & 0xFF;
        int p = (Rw(m, 0xC44A) + d) & 0xFFFF;                        // $2662 $0A92
        int a = m[p];
        if (a == 0xFF) a = m[(p - 1) & 0xFFFF];                      // $2669
        int b = a & 0x0F;
        int hl = Word(m, Rw(m, 0xC44C), d);                          // $2672
        if (b < 0x03)                                                // $267C rampa
        {
            int de = e >> 3;
            if (b != 0x01) de = -de;
            return (hl + de) & 0xFFFF;
        }
        if (b < 0x04) return hl;                                     // $2694
        int t = Word(m, 0x5F28, b - 4);                              // $2698
        int hi = t >> 8, lo = t & 0xFF;
        if (hi <= e && lo >= e) hl = (hl + (b < 0x0A ? 0xFF00 : 0xFFE0)) & 0xFFFF;
        return hl;
    }

    // $3A4A: GroundY + nibble alto (con signo si >= $C0) de ($C44E)[X hi] + c
    static int Hover(byte[] m, int ix, int c)
    {
        int de = GroundY(m, ix);
        int a = m[(Rw(m, 0xC44E) + m[ix + 0x11]) & 0xFFFF] & 0xF0;
        int hl = a | (a >= 0xC0 ? 0xFF00 : 0);
        return (hl + de + c) & 0xFFFF;
    }

    // $205A: choque con el jugador (d = margen Y - $0C, e = margen X - $0E), solo si $C85F & $0F >= 2
    static bool Touch(byte[] m, int ix, int de)
    {
        if ((m[0xC85F] & 0x0F) < 0x02) return false;
        int dx = Math.Abs(Rw(m, 0xC850) - Rw(m, ix + 0x10));
        if (dx > 0xFF || ((0x0E + (de & 0xFF)) & 0xFF) < dx) return false;
        int dy = Math.Abs(Rw(m, 0xC852) - Rw(m, ix + 0x12));
        if (dy > 0xFF || ((0x0C + (de >> 8)) & 0xFF) < dy) return false;
        TouchHits++;
        return true;
    }

    // $20A9 + $20D0: choque con las armas $C860/$C880. Al acertar pone IY+$1E bit2 e IY+$19 = 0.
    static bool Weapon(byte[] m, int ix, int de, out int iy)
    {
        for (iy = 0xC860; iy <= 0xC880; iy += 0x20)
        {
            if (m[iy + 0x1F] == 0 || (m[iy + 0x1E] & 0x04) != 0) continue;
            int dx = Math.Abs(Rw(m, iy + 0x10) - Rw(m, ix + 0x10));
            if (dx > 0xFF || ((0x08 + (de & 0xFF)) & 0xFF) < dx) continue;
            int dy = Math.Abs(Rw(m, iy + 0x12) - Rw(m, ix + 0x12));
            if (dy > 0xFF || ((0x08 + (de >> 8)) & 0xFF) < dy) continue;
            m[iy + 0x1E] |= 0x04;                                    // $2118
            m[iy + 0x19] = 0x00;                                     // $20C0
            WeaponHits++;
            return true;
        }
        return false;
    }

    // $4EF4: vitalidad $C469 += de, limitada a 0..$2400
    internal static void Vitality(byte[] m, int de)
    {
        int hl = (Rw(m, 0xC469) + de) & 0xFFFF;
        if ((hl & 0x8000) != 0) hl = 0;
        if (hl > 0x2400) hl = 0x2400;
        Ww(m, 0xC469, hl);
    }

    // $0E48: puntos a la cola ($C035), salvo si $C005 bit0
    internal static void Score(byte[] m, int a)
    {
        if ((m[0xC005] & 0x01) != 0) return;
        m[0xC033] = 0x01;
        int hl = Rw(m, 0xC035);
        m[hl] = (byte)a;
        Ww(m, 0xC035, hl + 1);
    }
}
