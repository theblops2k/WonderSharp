namespace WonderBoy.Game;

using static ObjectRoutines;

// Objeto 0 ($C800): traduccion 1:1 de $24EB-$252A (llamada desde $2193 y $21BC, justo antes de $21CF).
// No recorre las 17 ranuras: solo trabaja con IX=$C800. Opera sobre el espacio de la CPU (64 KB).
// Subrutinas que alcanza ($2D7F, $4050 -> $1FA3, $1FC0, $293A -> $26BD/$2698): ObjectRoutines.cs.
// Lee:      $C443, $C850/$C851, $C008 bit7, $C44A/$C44B, $C44C/$C44D, IX+$08 $0A-$0F $10/$11 $18 $19 $1E $1F
// Escribe:  IX+$04 $05 $08 $09 $0A-$0F $10 $11 $12 $13 $18 $19 $1C $1D $1F, IX+$1E bit 0
// No toca VRAM ni sprite RAM.
public static class SlotUpdate
{
    public const int Ix = 0xC800;

    // Devuelve 0 si no hace nada, 1 si va por $2D7F, 2 si avanza $C819 (posicion + script nuevos).
    public static int Update(byte[] m)
    {
        int ix = Ix, a;
        if (m[ix + 0x18] == 0)                                       // $24EF
        {
            m[ix + 0x18] = 0x01;                                     // $24F5
            a = (m[0xC443] >> 4) & 0x0F;                             // $24F9
        }
        else
        {
            if (m[ix + 0x1F] != 0) { OffScreen(m, ix); return 1; }   // $2504 jp nz,$2D7F
            a = m[ix + 0x19];                                        // $250B
            if (a == 0x04) return 0;                                 // $2510
            a = (a + 1) & 0xFF;
        }
        m[ix + 0x19] = (byte)a;                                      // $2512
        m[ix + 0x10] = 0x40;                                         // $2515
        int hl = 0x0800 | a;                                         // $2519
        a = (a << 4) & 0xFF;                                         // $251C
        if (a >= 0x40) a--;                                          // $2520
        m[ix + 0x11] = (byte)a;                                      // $2525
        Start(m, ix, hl);                                            // $2528 jp $4050
        return 2;
    }
}
