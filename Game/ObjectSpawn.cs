namespace WonderBoy.Game;

using static ObjectRoutines;

// Creacion de objeto en una ranura: traduccion 1:1 de $2CA5-$2D45 y $2D46-$2D7E (llamada desde $2BD5,
// $2C15, para ranuras $C8A0 + n*$20, n = 0..11, con IX+$18 bit7 = 1, o IX+$18 = 0 y $C479 = 0).
// $C401/$C403/$C405: tres punteros a listas de ROM ordenadas por posicion (entradas de 2-4 bytes:
// [tipo | X bits 7-6] [X bits 15-8] [+$19 si tipo >= $38] [+$1A si tipo >= $3C]). $2D46 deja en $C401
// la de menor posicion. Cola $C0C7: 6 entradas de 3 bytes [tipo] [destino lo] [destino hi], la vacia
// $07A6 (IRQ $0038) copiando 16 bytes de la tabla $57A1[tipo-1] a $D800 + (IX & $3FF) / 2 (paleta).
// Lee:      $C0C7.., $C401-$C406, listas de ROM, IX+$18
// Escribe:  IX+$10 $11 $12-$17 $18 $19 $1A $1B $1E $1F, $C401-$C406, una entrada de la cola $C0C7
// No toca VRAM ni sprite RAM. No llama a nada fuera de si misma.
public static class ObjectSpawn
{
    public const int Queue = 0xC0C7;

    // $2CA5. Devuelve 0 si la cola esta ocupada ($C0C7 != 0), 1 si la lista no tiene objeto (tipo 0),
    // 2 si crea un objeto de la lista, 3 si activa uno pendiente (IX+$18 bit7), 4 si la cola esta llena.
    public static int Spawn(byte[] m, int ix)
    {
        if (m[Queue] != 0) return 0;                                 // $2CA5
        int r = 3;
        if ((m[ix + 0x18] & 0x80) == 0)                              // $2CAB
        {
            Merge(m, 0xC403);                                        // $2CB1
            Merge(m, 0xC401);                                        // $2CB7
            int hl = Rw(m, 0xC401);                                  // $2CBD
            int e = m[hl];
            int a = e & 0x3F;
            if (a == 0) return 1;                                    // $2CC4
            m[ix + 0x19] = 0x00;                                     // $2CC5
            m[ix + 0x1A] = 0x00;
            int c = a;
            m[ix + 0x18] = (byte)a;                                  // $2CCE
            int d = m[(hl + 1) & 0xFFFF];                            // $2CD1
            hl = (hl + 2) & 0xFFFF;
            int x = ((d << 8) | (e & 0xC0)) >> 2;                    // $2CD4-$2CDC
            m[ix + 0x10] = (byte)x;                                  // $2CDD
            m[ix + 0x11] = (byte)(x >> 8);
            if (c >= 0x38)                                           // $2CE4
            {
                m[ix + 0x19] = m[hl]; hl = (hl + 1) & 0xFFFF;        // $2CE8
                if (c >= 0x3C)                                       // $2CED
                {
                    m[ix + 0x1A] = m[hl]; hl = (hl + 1) & 0xFFFF;    // $2CF1
                }
            }
            Ww(m, 0xC401, hl);                                       // $2CF6
            r = 2;
        }
        int t = m[ix + 0x18] & 0x7F;                                 // $2CF9
        for (int i = 0; i < 6; i++) m[ix + 0x12 + i] = 0x00;         // $2CFF-$2D0B
        m[ix + 0x18] = (byte)t;                                      // $2D0D
        m[ix + 0x1B] = 0x00;
        m[ix + 0x1E] = 0x00;
        m[ix + 0x1F] = 0x01;                                         // $2D16
        return Enqueue(m, ix, t) ? r : 4;                            // $2D1A (c = tipo)
    }

    // $2D1A (DI ... EI): pone [c] [destino lo] [destino hi] en la primera entrada libre de la cola
    // $C0C7. Devuelve false si las 6 estan ocupadas ($2D2E). La usan tambien los manejadores de $2D9B.
    public static bool Enqueue(byte[] m, int ix, int c)
    {
        for (int i = 0; i < 6; i++)
        {
            int iy = Queue + 3 * i;
            if (m[iy] != 0) continue;                                // $2D24
            m[iy] = (byte)c;                                         // $2D30
            int dst = 0xD800 + ((ix & 0x3FF) >> 1);                  // $2D36-$2D3C
            m[iy + 1] = (byte)dst;                                   // $2D3E
            m[iy + 2] = (byte)(dst >> 8);
            return true;                                             // $2D44 (EI)
        }
        return false;                                                // $2D2E
    }

    // $07A6 (IRQ $0038, si $C009 != 0): vacia la cola. Por cada entrada copia 16 bytes de la tabla
    // $57A1[tipo-1] al destino (paleta $D8xx) y desplaza $C0CA-$C0D9 sobre $C0C7-$C0D6.
    public static void Drain(byte[] m)
    {
        while (m[Queue] != 0)                                        // $07A6
        {
            int src = Word(m, 0x57A1, m[Queue] - 1);                 // $07AB $0A9A
            int dst = Rw(m, Queue + 1);                              // $07B2
            for (int i = 0; i < 0x10; i++) m[(dst + i) & 0xFFFF] = m[(src + i) & 0xFFFF];
            for (int i = 0; i < 0x10; i++) m[Queue + i] = m[Queue + 3 + i]; // $07BB
        }
    }

    // $2D46: hl -> [p1][p2]. Si la entrada de p2 tiene objeto y la de p1 no, o su posicion
    // (byte0 & $C0 | byte1 << 8) es >= la de p2, intercambia los dos punteros.
    public static void Merge(byte[] m, int hl)
    {
        int p1 = Rw(m, hl), p2 = Rw(m, hl + 2);                      // $2D46-$2D4E
        if ((m[p2] & 0x3F) == 0) return;                             // $2D4F
        int bc = (m[(p2 + 1) & 0xFFFF] << 8) | (m[p2] & 0xC0);       // $2D58-$2D5D
        if ((m[p1] & 0x3F) != 0)                                     // $2D60
        {
            int pos1 = (m[(p1 + 1) & 0xFFFF] << 8) | (m[p1] & 0xC0); // $2D68-$2D6F
            if (pos1 < bc) return;                                   // $2D71
        }
        Ww(m, hl, p2);                                               // $2D75-$2D7D
        Ww(m, hl + 2, p1);
    }
}
