namespace WonderBoy.Game;

using static ObjectRoutines;

// Armas del jugador: traduccion 1:1 de $4CD2-$4E3E (llamada desde $2193 y $21BC tras $274D).
// Dos ranuras $C860 y $C880 ($C3DF = 2 de contador). $C462 = 1 salvo si una ranura libre se
// recorre sin peticion ($C463 = 0). IX+$1F: 0 libre, 1 en la mano, 2 en vuelo, 3 rebote.
//   $4D08  lanzar (peticion $C463): $C311 bit1, velocidad/desplazamiento segun $C408 (monopatin) y
//          $C85E bit4 (direccion), posicion respecto al jugador, script $1FA3, IX+$19 = $C84C.
//   $4D78  estado 1 sigue al jugador hasta que IX+$19 llega a 0 (sonido $17, pasa a 2); estados 2/3
//          $2716 + $43B2 con gravedad (1, o 4 en 3; minimo $A0), $1FC0 y fin por $294F / IX+$1E bit5.
//   $4E14  impacto (IX+$1E bit2, estado 2): con IX+$19 != 0 rebota (sonido $0E, estado 3).
public static class Weapon
{
    public static void Update(byte[] m) // $4CD2
    {
        m[0xC462] = 0x01;
        m[0xC3DF] = 0x02;
        int ix = 0xC860;
        do
        {
            if (m[ix + 0x1F] != 0) H4D78(m, ix);
            else if (m[0xC463] != 0) H4D08(m, ix);
            else m[0xC462] = 0x00;                                   // $4CEC
            ix += 0x20;
        } while (--m[0xC3DF] != 0);
    }

    // $4D08: lanzamiento
    static void H4D08(byte[] m, int ix)
    {
        m[0xC463] = 0x00;
        m[0xC311] |= 0x02;
        m[ix + 0x1F] = 0x01;
        int c = 0x40, de = 0xFFF0, hl = 0x0100;
        if (m[0xC408] == 0)                                          // $4D1D
        {
            de = 0xFFE8;
            if ((m[0xC85E] & 0x10) != 0) { c = 0xC8; de = 0x0018; hl = 0x0101; } // $4D2D
        }
        m[ix + 0x14] = (byte)c;                                      // $4D34
        Ww(m, ix + 0x1A, de);
        Ww(m, ix + 0x10, Rw(m, 0xC850) + de);                        // $4D3D
        Ww(m, ix + 0x12, Rw(m, 0xC852) + (m[0xC408] != 0 ? 0x06 : 0x0C)); // $4D47
        m[ix + 0x16] = m[ix + 0x17] = m[ix + 0x18] = m[ix + 0x1E] = 0x00;
        SetScript(m, ix, hl);                                        // $4D6B $1FA3
        Animate(m, ix);                                              // $4D6E $1FC0
        m[ix + 0x19] = m[0xC84C];                                    // $4D71
    }

    // $4D78: arma activa
    static void H4D78(byte[] m, int ix)
    {
        int st = m[ix + 0x1F];
        if (st == 0x01)
        {
            if ((m[ix + 0x1E] & 0x04) != 0) { m[ix + 0x1F] = 0x00; return; } // $4D7F
            int de = Rw(m, ix + 0x1A);                               // $4D8A
            Ww(m, ix + 0x10, Rw(m, 0xC850) + de);
            Ww(m, ix + 0x12, Rw(m, 0xC852) + (m[0xC408] != 0 ? 0x06 : 0x0C)); // $4D9A
            if (--m[ix + 0x19] != 0) return;                         // $4DB0
            // $4DB4 cpl/inc de: DE ya es el desplazamiento Y de $4D9D ($0C, o $06 con $C408), no IX+$1A
            Ww(m, ix + 0x10, Rw(m, 0xC850) - (m[0xC408] != 0 ? 0x06 : 0x0C));
            TitleRom.Sound(m, 0x17);                                 // $4DC5
            m[ix + 0x1F]++;
            return;
        }
        if (st != 0x03 && (m[ix + 0x1E] & 0x04) != 0) { H4E14(m, ix); return; } // $4DCE
        MoveX2716(m, ix);                                            // $4DD8
        Enemies.MoveY(m, ix);                                        // $4DDB $43B2
        int c = m[ix + 0x1F] == 0x03 ? 0x04 : 0x01;                  // $4DDE
        int a = (m[ix + 0x17] - c) & 0xFF;                           // $4DE9
        if ((a & 0x80) != 0 && a < 0xA1) a = 0xA0;                   // $4DED jp p / cp $A1
        m[ix + 0x17] = (byte)a;
        Animate(m, ix);                                              // $4DF9 $1FC0
        if (ScreenBounds.Visible(m, ix))                             // $4DFC bit5 (o franja ancha)
        {
            int g = Enemies.GroundAt(m, ix, Rw(m, ix + 0x10));       // $4E02 $294F
            if (Rw(m, ix + 0x12) >= g) return;                       // $4E0B sbc hl,de / ret nc
        }
        m[ix + 0x1F] = 0x00;                                         // $4E0F
    }

    // $4E14: impacto en vuelo
    static void H4E14(byte[] m, int ix)
    {
        if (m[ix + 0x19] == 0) { m[ix + 0x1F] = 0x00; return; }
        TitleRom.Sound(m, 0x0E);                                     // $4E1F
        m[ix + 0x1F]++;
        int a = 0x08, hl = 0x0100;
        if ((m[ix + 0x14] & 0x80) == 0) { a = 0xF8; hl = 0x0101; }   // $4E2C
        m[ix + 0x14] = (byte)a;
        m[ix + 0x17] = 0x10;
        SetScript(m, ix, hl);                                        // $4E3C jp $1FA3
    }

    // $2716: X += IX+$14 (3.5 con signo, el doble que $2725), fraccion en IX+$15
    static void MoveX2716(byte[] m, int ix)
    {
        int v = m[ix + 0x14];
        int f = ((v << 5) & 0xFF) + m[ix + 0x15];
        m[ix + 0x15] = (byte)f;
        Ww(m, ix + 0x10, Rw(m, ix + 0x10) + ((sbyte)v >> 3) + (f >> 8));
    }
}
