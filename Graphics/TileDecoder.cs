using System.IO.Compression;
using Raylib_cs;

namespace WonderBoy.Graphics;

// Decodificador de graficos de wboy2u (Sega System 1), segun MAME system1.cpp.
//
// Tiles (region "tiles", 0xC000 bytes, gfx_layout charlayout en system1.cpp:2174):
//   8x8, RGN_FRAC(1,3) = 2048 tiles, 3 bpp, planos en RGN_FRAC(0,3)/(1,3)/(2,3)
//   (0x0000/0x4000/0x8000). El primer plano listado es el MSB del pixel.
//   Fila y = byte y del tile (8 bytes/tile por plano); pixel x = bit (7-x) del byte.
//   Carga (system1.cpp:4429): 7497,7496,7495,7494,7493,7492 a 0x0000..0xA000.
//
// Sprites (region "sprites", 0x10000 bytes, sin gfx_layout: los dibuja system1_v.cpp
//   draw_sprites directamente de la ROM): 4 bpp empaquetado, 2 pixeles por byte,
//   nibble alto primero (recorrido normal) o nibble bajo primero (bit 15 de la
//   direccion = espejo horizontal, se recorre hacia atras). Bancos de 0x8000.
//   Cada linea avanza 'pitch' bytes (int16 de la spriteram) y termina al leer
//   el nibble 0xF. Carga (system1.cpp:4437): 7485,7487,7486,7488 a 0x0000..0xC000.
public static class TileDecoder
{
    public const int TileCount = 2048;
    public const int TileBytesPerPlane = 0x4000;
    public const int SpriteBankSize = 0x8000;

    static readonly string[] TileRoms = { "epr-7497.62", "epr-7496.61", "epr-7495.64", "epr-7494.63", "epr-7493.66", "epr-7492.65" };
    static readonly string[] SpriteRoms = { "epr-7485.117", "epr-7487.04", "epr-7486.110", "epr-7488.05" };

    public static byte[] LoadRegion(string zipPath, string[] names)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        using var ms = new MemoryStream();
        foreach (var n in names)
        {
            var e = zip.GetEntry(n) ?? throw new FileNotFoundException($"{n} no esta en {zipPath}");
            using var s = e.Open();
            s.CopyTo(ms);
        }
        return ms.ToArray();
    }

    public static byte[] LoadTileRom(string zipPath) => LoadRegion(zipPath, TileRoms);
    public static byte[] LoadSpriteRom(string zipPath) => LoadRegion(zipPath, SpriteRoms);

    // Devuelve tiles[n][y*8+x] = indice de color 0..7.
    public static byte[][] DecodeTiles(byte[] rom)
    {
        int planeSize = rom.Length / 3;
        int count = planeSize / 8;
        var tiles = new byte[count][];
        for (int t = 0; t < count; t++)
        {
            var px = new byte[64];
            for (int y = 0; y < 8; y++)
            {
                int o = t * 8 + y;
                byte p2 = rom[o], p1 = rom[planeSize + o], p0 = rom[2 * planeSize + o];
                for (int x = 0; x < 8; x++)
                {
                    int b = 7 - x;
                    px[y * 8 + x] = (byte)(((p2 >> b) & 1) << 2 | ((p1 >> b) & 1) << 1 | ((p0 >> b) & 1));
                }
            }
            tiles[t] = px;
        }
        return tiles;
    }

    // Una linea de sprite como en draw_sprites: desde addr (16 bits, bit15 = flip X)
    // hasta el nibble 0xF (maxWidth como tope de seguridad).
    public static List<byte> DecodeSpriteLine(byte[] rom, int bank, int addr, int maxWidth = 256)
    {
        var line = new List<byte>();
        int baseOfs = (bank % (rom.Length / SpriteBankSize)) * SpriteBankSize;
        bool flip = (addr & 0x8000) != 0;
        int cur = addr;
        while (line.Count < maxWidth)
        {
            byte d = rom[baseOfs + (cur & 0x7FFF)];
            int c1 = flip ? d & 0x0F : d >> 4;
            int c2 = flip ? d >> 4 : d & 0x0F;
            cur += flip ? -1 : 1;
            if (c1 == 15) break;
            line.Add((byte)c1);
            if (c2 == 15) break;
            line.Add((byte)c2);
        }
        return line;
    }

    // Paleta de prueba: 8 grises para tiles (0 = negro).
    public static Color TestTileColor(int c) => c == 0 ? Color.Black : new Color(c * 36, c * 36, c * 36, 255);

    // Paleta de prueba de 16 colores para sprites; 15 (fin de linea) en magenta oscuro.
    static readonly Color[] SprPal =
    {
        new(0,0,0,255), new(255,255,255,255), new(255,0,0,255), new(0,200,0,255),
        new(0,0,255,255), new(255,255,0,255), new(0,255,255,255), new(255,128,0,255),
        new(128,64,0,255), new(255,160,160,255), new(128,128,128,255), new(64,64,64,255),
        new(160,255,160,255), new(160,160,255,255), new(200,200,200,255), new(96,0,96,255),
    };
    public static Color TestSpriteColor(int c) => SprPal[c & 15];

    // Hoja de tiles: 'perRow' tiles por fila, sin separacion. 2048 tiles -> 256x512 (perRow=32).
    public static Image BuildTileSheet(byte[][] tiles, int perRow = 32)
    {
        int rows = (tiles.Length + perRow - 1) / perRow;
        var img = Raylib.GenImageColor(perRow * 8, rows * 8, Color.Black);
        for (int t = 0; t < tiles.Length; t++)
        {
            int ox = (t % perRow) * 8, oy = (t / perRow) * 8;
            for (int i = 0; i < 64; i++)
                if (tiles[t][i] != 0)
                    Raylib.ImageDrawPixel(ref img, ox + (i & 7), oy + (i >> 3), TestTileColor(tiles[t][i]));
        }
        return img;
    }

    // Hoja cruda de sprites: la ROM como flujo 4bpp con 'pitch' bytes por linea,
    // en columnas de 'stripRows' lineas. pitch=16 -> 32 px; 64 KB -> 4096 lineas -> 16 columnas.
    public static Image BuildSpriteSheet(byte[] rom, int pitch = 16, int stripRows = 256)
    {
        int lines = rom.Length / pitch;
        int strips = (lines + stripRows - 1) / stripRows;
        int w = pitch * 2 + 2;
        var img = Raylib.GenImageColor(strips * w, stripRows, new Color(40, 0, 40, 255));
        for (int l = 0; l < lines; l++)
        {
            int ox = (l / stripRows) * w, y = l % stripRows;
            for (int b = 0; b < pitch; b++)
            {
                byte d = rom[l * pitch + b];
                Raylib.ImageDrawPixel(ref img, ox + b * 2, y, TestSpriteColor(d >> 4));
                Raylib.ImageDrawPixel(ref img, ox + b * 2 + 1, y, TestSpriteColor(d & 0x0F));
            }
        }
        return img;
    }

    public static Image Scale(Image img, int k)
    {
        var copy = Raylib.ImageCopy(img);
        Raylib.ImageResizeNN(ref copy, img.Width * k, img.Height * k);
        return copy;
    }
}
