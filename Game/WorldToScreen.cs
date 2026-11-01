namespace WonderBoy.Game;

// Mundo -> pantalla de los objetos: traduccion 1:1 de $239E-$24EA (llamada desde $2193 tras $21CF/$2297,
// desde $21BC, y desde $12FB/$133B/$16DC). Opera sobre el espacio de la CPU (64 KB: ROM $0000-$BFFF + RAM).
// Recorre 17 ranuras de $20 bytes en $C800-$CA1F (IX); solo procesa las que tienen IX+$1F != 0 ($23CF).
// Lee:      IX+$04 $08 $09 $10/$11 (X mundo) $12/$13 (Y mundo) $1C/$1D $1F, $C442 (camara X), $C444 (Y), $C47A
// Escribe:  $C3DF (contador, termina en 0), $C47A/$C47B, IX+$00 $01 $02 $03 $06 $07, IX+$1E bits 1 y 5
// No toca VRAM ni sprite RAM (la lista de sprites la construye despues otra rutina).
public static class WorldToScreen
{
    public const int Slots = 0xC800, SlotCount = 0x11, SlotSize = 0x20;

    static ushort Rw(byte[] m, int a) => (ushort)(m[a] | m[a + 1] << 8);
    static void Ww(byte[] m, int a, int v) { m[a] = (byte)v; m[a + 1] = (byte)(v >> 8); }

    // $239E: bucle de 17 ranuras.
    public static void Update(byte[] m)
    {
        int ix = Slots;
        m[0xC3DF] = SlotCount;
        do
        {
            m[ix + 0x1E] &= unchecked((byte)~0x22);                 // $23A7 res 1 / res 5
            m[ix + 0x01] = 0x00;                                    // $23AF
            m[ix + 0x03] = 0x01;                                    // $23B3
            if (m[ix + 0x1F] != 0) Slot(m, ix);                     // $23B7
            ix += SlotSize;
            m[0xC3DF]--;
        } while (m[0xC3DF] != 0);
    }

    static int Abs8(int a) => (a & 0x80) != 0 ? (-a) & 0xFF : a;

    // $23CF: una ranura activa.
    static void Slot(byte[] m, int ix)
    {
        // $23CF: de = 2*|IX+4| (8 bits) + IX+9 (7 bits con signo en bit 6)
        int de = (Abs8(m[ix + 0x04]) * 2) & 0xFF;
        int a = m[ix + 0x09] & 0x7F;
        int d = 0;
        if ((a & 0x40) != 0) { a |= 0x80; d = 0xFF; }
        de = (de + (d << 8 | a)) & 0xFFFF;
        // $23EC: x = X - de - ($C442 - $100)
        int x = (Rw(m, ix + 0x10) - de) & 0xFFFF;
        x = (x - ((Rw(m, 0xC442) - 0x100) & 0xFFFF)) & 0xFFFF;
        int hl = (x + 0xFFF4) & 0xFFFF;                            // $23FE
        int h;
        if ((hl & 0x8000) != 0)
        {
            // $2406: x < 12, recorte por la izquierda
            int b = (Abs8(m[ix + 0x04]) * 4) & 0xFF;
            if ((hl >> 8) != 0xFF) return;                          // $2412
            int c = (-(hl & 0xFF)) & 0xFF;                          // $2416
            if (c == 0) return;
            if (c >= b) return;                                     // $241B
            a = c >> 2;
            int e = a; d = 0;
            if ((m[ix + 0x09] & 0x80) != 0)                         // $242A
            {
                e = (~a) & 0xFF; d = 0xFF;
                de = ((d << 8 | e) + 1) & 0xFFFF;
            }
            else de = e;
            int p = (Rw(m, ix + 0x1C) + de) & 0xFFFF;               // $2434
            Ww(m, 0xC47A, de);
            Ww(m, ix + 0x06, p);
            int l = ((-(c & 0x03)) + 0x0C) & 0xFF;                  // $243F
            h = (de >> 8) & 0xFE;                                   // $2447 ld h,d / res 0,h
            hl = h << 8 | l;
        }
        else
        {
            // $244C
            Ww(m, 0xC47A, 0);
            if (x + 0xFE04 > 0xFFFF) return;                        // $2452 ret c (x >= $1FC)
            m[ix + 0x06] = m[ix + 0x1C];
            m[ix + 0x07] = m[ix + 0x1D];
            hl = x;
        }
        // $2464
        h = (m[ix + 0x07] & 0x80) | (hl >> 8);
        m[ix + 0x02] = (byte)hl;
        m[ix + 0x03] = (byte)h;
        m[ix + 0x1E] |= 0x20;

        // $2475: y = Y + IX+8/2 - $C444; sigue solo si es negativo (prestamo) y > -$E0
        int ysum = Rw(m, ix + 0x12) + (m[ix + 0x08] >> 1);
        ysum &= 0xFFFF;
        int cy = Rw(m, 0xC444);
        if (ysum >= cy) return;                                     // $248B ret nc
        int y = (ysum - cy) & 0xFFFF;
        if ((y >> 8) != 0xFF) return;                               // $248C
        a = (-(y & 0xFF)) & 0xFF;
        if (a >= 0xE0) return;                                      // $2493
        m[ix + 0x1E] |= 0x02;
        m[ix + 0x00] = (byte)a;
        int cc = m[ix + 0x04];
        int s = a + m[ix + 0x08];
        if ((cc & 0x80) == 0)
        {
            m[ix + 0x01] = (byte)(s > 0xFF ? 0xFF : s);             // $24A4
            return;
        }
        if (s <= 0xFF && s < 0xDE)                                  // $24AF
        {
            m[ix + 0x01] = (byte)s;
            return;
        }
        // $24BC: recorte inferior: IX+6/7 = IX+1C/1D - ((s-$DD) * (IX+4 - 1)) + $C47A
        int e2 = (s - 0xDD) & 0xFF;
        int mul = (((cc - 1) & 0xFF) * e2) & 0xFFFF;
        int r = (Rw(m, ix + 0x1C) - mul + Rw(m, 0xC47A)) & 0xFFFF;
        Ww(m, ix + 0x06, r);
        m[ix + 0x01] = 0xDF;
    }
}
