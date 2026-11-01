namespace WonderBoy.Game;

// Relleno de tilemap: traduccion 1:1 de $2297-$239D (llamada desde $2193/$21BC justo despues de $21CF).
// Cada vez que $C442 cruza un multiplo de 16 px escribe media columna (2 celdas de 8 px de ancho,
// pares tile/atributo) en VRAM $E000-$E7FF: 16 pasos ($C453=0..$0F) por columna de 256 px.
// Opera sobre el espacio de la CPU (64 KB: ROM $0000-$BFFF + RAM), igual que Camera.Update.
// Lee:      $C442 $C452 $C453 $C30F $C454 (puntero a tabla de nivel, 4 bytes/entrada) $C456-$C45E,
//           ROM $6A78 (32 bytes/fila: punteros a bloques), $6FB8 (16 bytes/fila), $902A (17 bytes/bloque).
// Escribe:  $C452 $C453 $C30F $C456/$C457 $C458/$C459 $C45A $C45B $C45C $C45D $C45E, VRAM $E000-$E7FF.
public static class TilemapFill
{
    const int BlockTable = 0x6A78;   // $22C3
    const int RowTable = 0x6FB8;     // $22D2
    const int TileBlocks = 0x902A;   // $2352

    static ushort Rw(byte[] m, int a) => (ushort)(m[a] | m[a + 1] << 8);
    static void Ww(byte[] m, int a, int v) { m[a] = (byte)v; m[a + 1] = (byte)(v >> 8); }

    // Un paso de $2297. Devuelve 0 si no hizo nada, 1 si escribio un paso, 2 si ademas abrio columna nueva.
    public static int Update(byte[] m)
    {
        byte a = (byte)(m[0xC442] & 0xF0);                      // $229A
        if (a == m[0xC452]) return 0;                           // $229F ret z
        m[0xC452] = a;                                          // $22A1
        return Pass(m);
    }

    // $22A2: un paso del flujo (lo usan $2297 y la carga de nivel $1431). Devuelve 1, o 2 si abrio columna.
    public static int Pass(byte[] m)
    {
        int result = 1;
        if (m[0xC453] >= 0x10)                                  // $22A5 jr c,$2303
        {
            result = 2;
            m[0xC453] = 0;                                      // $22A9
            int idx = m[0xC30F] & 0x7F;                         // $22B0 valor previo
            m[0xC30F]++;                                        // $22B1
            int p = (Rw(m, 0xC454) + idx * 4) & 0xFFFF;         // $2375
            byte e = m[p], d = m[p + 1], c = m[p + 2], b = m[p + 3];
            Ww(m, 0xC456, (e * 32 + BlockTable) & 0xFFFF);      // $22BB
            Ww(m, 0xC458, (d * 16 + RowTable) & 0xFFFF);        // $22CB
            m[0xC45C] = c;                                      // $22D9
            m[0xC45B] = (byte)(b & 0x1F);                       // $22DD
            m[0xC45A] = 0; m[0xC45D] = 0;                       // $22E3
            byte s = 1;                                         // $22EA
            if ((b & 0x80) != 0) s = (byte)-s;                  // $22EB
            if ((b & 0x20) != 0) { m[0xC45D] = s; s = (byte)-s; } // $22F1
            s = (byte)-s;                                       // $22FA
            if ((b & 0x40) != 0) m[0xC45A] = s;                 // $22FC
        }

        // $2303: primera mitad, bloque de la fila ($C456) desplazado por $C45C.
        int q = Rw(m, 0xC456);
        int de = Rw(m, q);
        Ww(m, 0xC456, q + 2);
        byte attr = m[de];                                      // $230D
        de = (de + 1) & 0xFFFF;
        int src = (m[0xC45C] + de) & 0xFFFF;                    // $2310

        int hl = (((m[0xC45B] * 8) & 0xFF) * 8) | 0xE000;       // $2318 fila*8 en 8 bits, luego *8 en 16
        if ((m[0xC30F] & 1) == 0) hl |= 0x20;                   // $2328 mitad derecha en columnas pares
        hl = (hl & 0xFF00) | ((hl + m[0xC453] * 2) & 0xFF);     // $2331 solo L

        m[0xC45E] = 0x20;                                       // $2338
        hl = Copy(m, src, hl, attr);                            // $233D

        // $2341: segunda mitad, bloque de 17 bytes ($902A + n*17) indicado por ($C458).
        int r = Rw(m, 0xC458);
        int n = m[r];
        Ww(m, 0xC458, r + 1);
        int blk = (n * 17 + TileBlocks) & 0xFFFF;               // $2349
        attr = m[blk];                                          // $2358
        Copy(m, (blk + 1) & 0xFFFF, hl, attr);                  // $235B ($C45E no se recarga)

        m[0xC453]++;                                            // $235E
        if ((m[0xC453] & 3) != 0) return result;                // $2365
        m[0xC45C] = (byte)(m[0xC45C] + m[0xC45D]);              // $2366
        m[0xC45B] = (byte)(m[0xC45B] + m[0xC45A]);              // $236E
        return result;
    }

    // $2384: copia tiles hacia abajo (stride $40, envolviendo en $E000-$E7FF) hasta $01 o $C45E=0.
    static int Copy(byte[] m, int de, int hl, byte attr)
    {
        while (true)
        {
            byte t = m[de];
            if (t == 0x01) return hl;                           // $2385 ret z
            m[hl] = t;                                          // $2388
            m[(hl + 1) & 0xFFFF] = attr;                        // $238A
            de = (de + 1) & 0xFFFF;
            hl = ((hl + 0x40) & 0xFFFF) & ~0x0800;              // $238D res 3,h
            m[0xC45E]--;                                        // $2394
            if (m[0xC45E] == 0) return hl;                      // $239B
        }
    }
}
