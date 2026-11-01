namespace WonderBoy.Game;

// Subrutinas comunes de objeto (IX = base de ranura de $20 bytes), traduccion 1:1. Las usan $24EB
// (SlotUpdate) y los manejadores de enemigos de $2D9B. Operan sobre el espacio de la CPU (64 KB).
// $4050 (arranque), $1FA3 (fija script, tabla $9700), $1FC0/$2009 (avanza animacion),
// $2D7F (fuera de pantalla), $293A -> $26BD/$2698 (altura de terreno, tablas $C44A/$C44C y $5F28).
// No tocan VRAM ni sprite RAM.
public static class ObjectRoutines
{
    public static ushort Rw(byte[] m, int a) => (ushort)(m[a] | m[(a + 1) & 0xFFFF] << 8);
    public static void Ww(byte[] m, int a, int v) { m[a] = (byte)v; m[a + 1] = (byte)(v >> 8); }

    // $0A9A: hl = palabra en hl + 2*a (a+a en 8 bits)
    public static int Word(byte[] m, int hl, int a) => Rw(m, (hl + ((a * 2) & 0xFF)) & 0xFFFF);

    // $2D7F: si IX+$1E bit5 = 0 y $C850 >= X objeto, IX+$1F = 0 y devuelve true (carry).
    public static bool OffScreen(byte[] m, int ix)
    {
        if ((m[ix + 0x1E] & 0x20) != 0) return false;
        if (Rw(m, 0xC850) < Rw(m, ix + 0x10)) return false;
        if (InWideView(m, ix)) return false;                         // solo WIDESCREEN (ScreenBounds)
        m[ix + 0x1F] = 0x00;
        return true;
    }

    // true si el objeto aun se ve en la franja ancha (ScreenBounds.Left/Right); ORIGINAL / NO: false.
    public static bool InWideView(byte[] m, int ix) => ScreenBounds.InWideView(m, ix);

    // $4050
    public static void Start(byte[] m, int ix, int hl)
    {
        m[ix + 0x1F]++;                                              // $4050
        SetScript(m, ix, hl);                                        // $1FA3
        Animate(m, ix);                                              // $1FC0
        Ww(m, ix + 0x12, GroundY(m, ix));                            // $4059 $293A, $405C
    }

    // $1FA3: script = tabla $9700[H][L]
    public static void SetScript(byte[] m, int ix, int hl)
    {
        int p = Word(m, 0x9700, hl >> 8);
        p = Word(m, p, hl & 0xFF);
        Ww(m, ix + 0x0A, p);
        m[ix + 0x0C] = 0x01;
        m[ix + 0x1E] &= 0xFE;
    }

    // $1FC0: avanza el script de animacion
    public static void Animate(byte[] m, int ix)
    {
        if (--m[ix + 0x0C] != 0) return;
        int hl = Rw(m, ix + 0x0A);
        for (int guard = 0; ; guard++)
        {
            if (guard > 0x10000) throw new InvalidOperationException("$1FC0: script sin fin");
            int a = m[hl]; hl = (hl + 1) & 0xFFFF;                   // $1FCA
            if (a != 0) { Frame(m, ix, hl, a); return; }
            a = m[hl]; hl = (hl + 1) & 0xFFFF;                       // $1FD0
            if (a == 0x00) { m[ix + 0x1E] |= 0x01; return; }         // $1FD6 fin
            if (a == 0x01) { hl = Rw(m, hl); continue; }             // $1FDF salto
            if (a == 0x02)                                           // $1FEB inicio de bucle
            {
                m[ix + 0x0D] = m[hl]; hl = (hl + 1) & 0xFFFF;
                Ww(m, ix + 0x0E, hl);
                continue;
            }
            if (m[ix + 0x0D] == 0) continue;                         // $1FF8 fin de bucle
            m[ix + 0x0D]--;
            hl = Rw(m, ix + 0x0E);
        }
    }

    // $2009: fotograma con duracion a
    static void Frame(byte[] m, int ix, int hl, int a)
    {
        m[ix + 0x1E] &= 0xFE;
        m[ix + 0x0C] = (byte)a;
        int p = Rw(m, hl);
        Ww(m, ix + 0x0A, hl + 2);
        int e, d, x;
        if ((m[0xC008] & 0x80) == 0)                                 // $201D
        {
            x = m[p] >> 1;
            e = m[p + 3]; d = m[p + 4];
            m[ix + 0x05] = 0x00;
        }
        else
        {
            x = (-(m[p] >> 1)) & 0xFF;
            e = m[p + 5]; d = m[p + 6];
            m[ix + 0x05] = 0xFF;
        }
        m[ix + 0x04] = (byte)x;                                      // $2048
        m[ix + 0x08] = m[p + 1];
        m[ix + 0x09] = m[p + 2];
        m[ix + 0x1C] = (byte)e;
        m[ix + 0x1D] = (byte)d;
    }

    // $293A: Y suelo bajo X objeto + IX+$08/2
    public static int GroundY(byte[] m, int ix) => (Terrain(m, Rw(m, ix + 0x10)) + (m[ix + 0x08] >> 1)) & 0xFFFF;

    // $26BD (+$2698): altura de terreno en la X de mundo x
    public static int Terrain(byte[] m, int x)
    {
        int d = x >> 8, e = x & 0xFF;
        int p = (Rw(m, 0xC44A) + d) & 0xFFFF;                        // $26BF $0A92
        int b = m[p] & 0x0F;
        if (b == 0x0F) b = m[(p - 1) & 0xFFFF] & 0x0F;               // $26CC
        int hl = Word(m, Rw(m, 0xC44C), d);                          // $26D1
        if (b < 0x03)                                                // $26DB rampa
        {
            int de = e >> 3;
            if (b != 0x01) de = -de;
            return (hl + de) & 0xFFFF;
        }
        if (b < 0x0A) return hl;                                     // $26F3
        int t = Word(m, 0x5F28, b - 4);                              // $2698
        int hi = t >> 8, lo = t & 0xFF;
        if (hi <= e && lo >= e) hl = (hl + (b < 0x0A ? 0xFF00 : 0xFFE0)) & 0xFFFF;
        return hl;
    }
}
