namespace WonderBoy.Game;

// Altura del terreno de la ronda (eje Y de mundo hacia arriba).
// Traduccion de $2660 (suelo del jugador) y $26BD (suelo de $293A, otros objetos).
// Datos en ROM: TypeTable = ($C44A), un byte por columna de 256 px (nibble bajo = tipo);
// HeightTable = ($C44C), una palabra por columna (altura base). Pozos: tabla $5F28.
public sealed class Terrain
{
    const int PitTable = 0x5F28;
    readonly byte[] rom;
    public ushort TypeTable;   // $C44A/$C44B
    public ushort HeightTable; // $C44C/$C44D

    public Terrain(byte[] rom, ushort typeTable, ushort heightTable)
    {
        this.rom = rom; TypeTable = typeTable; HeightTable = heightTable;
    }

    byte Rd(int a) { a &= 0xFFFF; return a < rom.Length ? rom[a] : (byte)0; }
    ushort Rw(int a) => (ushort)(Rd(a) | Rd(a + 1) << 8);

    // $2660: tipo = [TypeTable + col] (si el byte es $FF, el de la columna anterior).
    public ushort Height(ushort x)
    {
        byte col = (byte)(x >> 8), e = (byte)x;
        byte t = Rd(TypeTable + col);
        if (t == 0xFF) t = Rd(TypeTable + col - 1);
        return Shape(t & 0x0F, col, e, pitsFrom: 4);
    }

    // $26BD: igual, pero la columna anterior se usa si el nibble bajo es $0F y los
    // pozos solo existen con tipo >= $0A.
    public ushort HeightExact(ushort x)
    {
        byte col = (byte)(x >> 8), e = (byte)x;
        int t = Rd(TypeTable + col) & 0x0F;
        if (t == 0x0F) t = Rd(TypeTable + col - 1) & 0x0F;
        return Shape(t, col, e, pitsFrom: 0x0A);
    }

    ushort Shape(int type, byte col, byte e, int pitsFrom)
    {
        int h = Rw(HeightTable + (byte)(col * 2)); // $0A9A: indice 2*A en 8 bits
        if (type < 3)
        {
            int s = e >> 3;                          // pendiente: 1 px cada 8
            return (ushort)(type == 1 ? h + s : h - s); // tipo 1 sube; 0 y 2 bajan
        }
        if (type < pitsFrom) return (ushort)h;     // tipo 3 (y 4-9 en $26BD): llano
        // $2698: pozo si H <= e <= L, con HL = palabra [$5F28 + 2*(tipo-4)].
        ushort w = Rw(PitTable + (byte)((type - 4) * 2));
        byte from = (byte)(w >> 8), to = (byte)w;
        if (e >= from && e <= to) h += type >= 0x0A ? -0x20 : -0x100;
        return (ushort)h;
    }
}
