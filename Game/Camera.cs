namespace WonderBoy.Game;

// Camara de scroll: traduccion 1:1 de $21CF-$2296 (llamada desde $2193 tras $274D/$4CD2/$2BD5/$24EB,
// y desde $21BC sin jugador). Opera sobre el espacio de la CPU (64 KB: ROM $0000-$BFFF + RAM).
// IX = $C442: +$01=$C443, +$03=$C445, +$07=$C449, +$0C/$0D=$C44E (tabla de pendiente/fin), +$0E=$C450.
// Lee:      $C440 $C85F $C451 $C442 $C850 $C450 $C44E $C448 $C449 $C444 $C000 $C002 $C479, ROM ($C44E)+n, $5F25
// Escribe:  $C442 $C000 $C450 $C451 $C440 $C479 $C448 $C449 $C444/$C445 $C002
// Sonido: $0440 (TitleRom.Sound, cola $C0DB+).
public static class Camera
{
    const int SlopeTable = 0x5F25;

    static ushort Rw(byte[] m, int a) => (ushort)(m[a] | m[a + 1] << 8);
    static void Ww(byte[] m, int a, int v) { m[a] = (byte)v; m[a + 1] = (byte)(v >> 8); }

    // Un paso de $21CF. Devuelve true si la camara avanzo (paso $21F6 en adelante).
    public static bool Update(byte[] m)
    {
        if (m[0xC440] != 0) return false;                       // $21CF fin de ronda en curso
        if ((m[0xC85F] & 0x0F) < 2) return false;               // $21D4 jugador muerto/inactivo
        if (m[0xC451] != 0) return false;                       // $21E0 fin de nivel alcanzado

        // $21E5: hl = $C442 - $C850 - $20 (16 bits); sigue solo si hay acarreo (resultado < 0 sin signo).
        int t = (Rw(m, 0xC442) - Rw(m, 0xC850)) & 0xFFFF;
        if (t >= 0x20) return false;                            // $21F5 ret nc
        int e = (byte)-(byte)(t - 0x20);                        // $21F6 neg l -> 1..$20

        Ww(m, 0xC442, Rw(m, 0xC442) + e);                       // $21FC
        Ww(m, 0xC000, (Rw(m, 0xC000) + e) & 0x01FF);            // $2203 scroll X de 9 bits

        byte a = m[0xC443];                                     // $220E columna de 256 px
        if (a != m[0xC450])
        {
            m[0xC450] = a;                                      // $2216
            a = m[(Rw(m, 0xC44E) + m[0xC443]) & 0xFFFF];        // $2219 call $0A92
            a &= 0x0F;
            if (a == 0x0F)                                      // $2227 fin de nivel
            {
                m[0xC451] = 1;
                if (m[0xC479] != 0) return true;
                m[0xC440] = 1;
                TitleRom.Sound(m, 0xCA);                      // $223A jp $0440
                return true;
            }
            if (a == 0x0E)                                      // $223F
            {
                TitleRom.Sound(m, 0xC4);                      // $2243
                m[0xC479] = 1;
                a = 0x03;
            }
            m[0xC448] = m[(SlopeTable + (byte)((a & 0x03) - 1)) & 0xFFFF]; // $224F-$2259
        }

        // $225C: a = $C448 * e (8 bits); sra; + $C449
        byte s = m[0xC448];
        byte p = s;
        for (int k = e - 1; k > 0; k--) p = (byte)(p + s);
        a = (byte)(((sbyte)p >> 1) + m[0xC449]);
        m[0xC449] = a;                                          // $226B
        int d = (sbyte)a >> 4;                                  // $226E sra x4, d = signo
        int c444 = (Rw(m, 0xC444) + d) & 0xFFFF;
        Ww(m, 0xC444, c444);                                    // $2281 (+ $2284 ld (ix+3),h)
        m[0xC002] = (byte)(m[0xC002] - (byte)d);                // $2287
        m[0xC449] &= 0x0F;                                      // $228E
        return true;
    }
}
