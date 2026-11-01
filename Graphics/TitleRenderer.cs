using Raylib_cs;

namespace WonderBoy.Graphics;

// Composicion de fondo + primer plano + sprites de System 1 a partir de volcados de RAM,
// segun MAME system1_v.cpp (tile_get_info, draw_sprites, video_update_common,
// screen_update_system1). Pantalla 512x224 (set_raw 640/0/512, 260/0/224): los tiles
// son de 256 px logicos y cada pixel ocupa 2 columnas; los sprites van a resolucion 512.
//
// Paleta ($D800-$DFFF, 2048 bytes): 1 byte por color, BBGGGRRR (wboy2u no tiene
//   PROMs de color). R = b0-2, G = b3-5, B = b6-7, por tablas de resistencias Lv3/Lv2.
//   Bancos: $000-$1FF sprites, $200-$3FF primer plano (FG), $400-$5FF fondo (BG).
// VRAM ($E000-$EFFF): 2 paginas de 32x32 celdas, 2 bytes por celda (little endian).
//   tiledata = lo | hi<<8; code = ((tiledata>>4)&0x800) | (tiledata&0x7FF);
//   color = (tiledata>>5)&0xFF; pixel = color*8 + indice (0..7, 0 = transparente).
//   Pagina 0 = BG (las 4 paginas logicas del BG apuntan a la 0), pagina 1 = FG (sin scroll).
//   Scroll BG: X = (vram[$FFC] | vram[$FFD]<<8)/2 + 14 (pixeles de 256), Y = vram[$FBD];
//   bgy = (y + Y) & 0xFF, bgx = (x/2 - X) & 0xFF.
// Sprites ($D000-$D7FF, 32 entradas de 16 bytes; [0]==$FF termina la lista):
//   [0] top-1, [1] bottom-1 (filas top+1 .. bottom), [2]|[3]<<8 & $1FF = X (espacio 512),
//   banco = b7->bit0, b6->bit1, b5->bit2 de [3]; [4..5] stride; [6..7] direccion origen.
//   Por fila: addr += stride, luego se recorren bytes (bit15 = espejo, -1) hasta nibble F.
//   Cada nibble ocupa 2 columnas de 512. pixel = nibble | sprite*16 (0 = transparente).
// Mezcla (PROM pr-5317.76, 256 bytes): indice = sprTransp | fgTransp<<1 |
//   ((fgpix>>9)&3)<<2 | bgTransp<<4 | ((bgpix>>9)&3)<<5; valor&3: 0 = sprite,
//   1 = $200|(fgpix&$1FF), 2/3 = $400|(bgpix&$1FF).
public static class TitleRenderer
{
    public const int Width = 512, Height = 224;

    // Niveles tras la red de resistencias (medidos contra mame_300.png; no son pal3bit/pal2bit).
    static readonly byte[] Lv3 = { 0x00, 0x24, 0x49, 0x6E, 0x91, 0xB6, 0xDB, 0xFF };
    static readonly byte[] Lv2 = { 0x00, 0x56, 0xA9, 0xFF };

    public static Color PalColor(byte d)
    {
        return new Color(Lv3[d & 7], Lv3[(d >> 3) & 7], Lv2[(d >> 6) & 3], (byte)255);
    }

    // OpacityBars (Config.cfg, 0..100): opacidad del fondo negro de las barras del HUD. 100 = opaco (original).
    // Pixel de barra = FG elegido por la PROM con color negro (BarBlack: pal $00 o $01) en las filas 0-3 (puntuacion, vidas y
    // vitalidad) o, en el nivel, en las celdas del inventario (filas 4-5, columnas 2-11, tile != 1 del telon).
    // Textos y barras de color no son negros: no cambian. Debajo se ve el BG / sprite (PROM con el FG vacio).
    public static int BarOpacity = 100;
    // Negro de barra: pal $00 (tile 1) o $01 (casi negro, 36,0,0). La ROM usa $01 como negro opaco en el
    // fondo de la vitalidad ($4E74, F0-FB) y de las vidas ($14D0, FC-FF): color 7, pen 6 = $01; y en el
    // fondo de las celdas del inventario ($0889 / $572D, colores $57 y $68, pen 7 = $01).
    public static bool BarBlack(byte d) => d <= 0x01;
    public const int BarRowsH = 4 * 8, ItemsH = 6 * 8;                     // filas 0-3 y filas 4-5
    // WIDESCREEN, filas 2-3: vidas ($14D0 en $E884, columnas 2-13, 12 celdas = 192 px de imagen) pegadas a la
    // izquierda; barra de vitalidad ($4E74 en $E89C, columnas 14-29, 16 celdas = 256 px) centrada en el ancho.
    // VitalityX: x de la imagen donde empieza la barra (nunca encima de las vidas, livesEnd = su x final).
    // VitalityFx: x del FG (espacio 512) para la fila 2-3 en x; -1 = fuera (franja negra de los lados).
    public const int VitCol = 14, VitW = 16 * 16, LivesW = 12 * 16;
    public static int VitalityX(int width, int livesEnd) => Math.Max((width - VitW) / 2 & ~1, livesEnd);
    public static int VitalityFx(int x, int hudX, int vitX)
    {
        if (x >= vitX && x < vitX + VitW) return VitCol * 16 + x - vitX;     // barra centrada
        int fx = x - hudX + 32;                                              // vidas: columna 2 en hudX
        return fx < VitCol * 16 ? fx : -1;                                   // su sitio original queda negro
    }
    public static bool ItemsCell(byte[] vram, int row, int col) =>
        col >= 2 && col <= 11 && WideView.FgCode(vram, row, col) != 1;
    // Seleccion de la PROM con el FG transparente (bit 1 = FG transparente, sin sus bits de prioridad 2-3).
    public static int UnderSel(byte[] prom, int idx) => prom[(idx & ~0x0E) | 2] & 3;
    public static Color BarBlend(Color under)
    {
        int k = 100 - BarOpacity;                                           // lo que se ve de debajo
        return new Color((byte)(under.R * k / 100), (byte)(under.G * k / 100), (byte)(under.B * k / 100), (byte)255);
    }

    public static int ScrollX(byte[] vram) => (vram[0xFFC] | vram[0xFFD] << 8) / 2 + 14;
    public static int ScrollY(byte[] vram) => vram[0xFBD];

    // Pixmap 256x256 de una pagina: valor = color*8 + pixel (11 bits).
    public static ushort[] RenderPage(byte[] vram, int page, byte[][] tiles) =>
        RenderPage(vram, page, tiles, new ushort[256 * 256]);

    // Igual, en un array reutilizado de 256*256 (se escriben todas sus posiciones: no hace falta vaciarlo).
    public static ushort[] RenderPage(byte[] vram, int page, byte[][] tiles, ushort[] pm)
    {
        int baseOff = page * 0x800;
        for (int cell = 0; cell < 32 * 32; cell++)
        {
            int td = vram[baseOff + cell * 2] | vram[baseOff + cell * 2 + 1] << 8;
            int code = (((td >> 4) & 0x800) | (td & 0x7FF)) % tiles.Length;
            int color = (td >> 5) & 0xFF;
            int ox = (cell & 31) * 8, oy = (cell >> 5) * 8;
            var t = tiles[code];
            for (int i = 0; i < 64; i++)
                pm[(oy + (i >> 3)) * 256 + ox + (i & 7)] = (ushort)(color * 8 + t[i]);
        }
        return pm;
    }

    // WIDESCREEN: lineas de la franja negra superior del FG (texto PLAYER / HIGH / puntuacion). Cuenta las
    // lineas seguidas desde y = 0 (como mucho maxLines) con el FG opaco en todas las columnas 2-29. A los
    // lados se rellenan con el tile negro de la columna 0 (FgBarPixel) para que la franja llene el ancho.
    public static int FgTopBar(ushort[] fg, int maxLines = 32)
    {
        for (int y = 0; y < maxLines; y++)
            for (int lx = 16; lx < 240; lx++)
                if ((fg[y * 256 + lx] & 7) == 0) return y;
        return maxLines;
    }

    // Pixel de la franja superior fuera de las columnas 2-29: el de la columna 0 (tile 1 negro) en la linea y.
    public static int FgBarPixel(ushort[] fg, int y, int lx) => fg[y * 256 + (lx & 7)];

    // WIDESCREEN: leyendas de las filas 0-1 del FG. PLAYER1 (fila 0) y su puntuacion (fila 1, $E844, 7 cifras)
    // ocupan las columnas 2-8; HIGH y record ($E858) las 9-22; PLAYER2 y su puntuacion ($E86E) las 23-29.
    // Columnas 2-8 pegadas a left (borde izquierdo), 23-29 pegadas a right (borde derecho, columna 29 al
    // final), 9-22 en su sitio centrado (ox). Cada bloque mantiene su justificacion original (cifras a la
    // derecha del bloque). El resto de la linea es la franja negra (tile 1 de la columna 0).
    public const int HudTextH = 2 * 8;                                       // filas 0-1
    public static int HudTopPixel(ushort[] fg, int y, int x, int left, int right, int ox)
    {
        const int Seg = 7 * 16;                                              // 7 columnas = 112 px de imagen
        if (x >= left && x < left + Seg) return fg[y * 256 + 2 * 8 + ((x - left) >> 1)];
        if (x >= right - Seg && x < right) return fg[y * 256 + 23 * 8 + ((x - right + Seg) >> 1)];
        int xs = x - ox;
        if (xs >= 9 * 16 && xs < 23 * 16) return fg[y * 256 + (xs >> 1)];
        return FgBarPixel(fg, y, (x - left) >> 1);
    }

    // Bitmap de sprites 512x256 (draw_sprites sin flip de pantalla ni colisiones).
    // bw/ox (vista ancha): bitmap de bw columnas con la X de cada sprite desplazada ox; sin recorte a 512.
    // xo (vista ancha): X con signo por sprite (int.MinValue = usar la X de 9 bits de la sprite RAM).
    public static ushort[] RenderSprites(byte[] spriteRam, byte[] sprRom, int bw = 512, int ox = 0, int[]? xo = null) =>
        RenderSprites(spriteRam, sprRom, new ushort[bw * 256], bw, ox, xo);

    // Igual, en un array reutilizado (al menos bw*256): se vacia con Array.Clear antes de dibujar.
    public static ushort[] RenderSprites(byte[] spriteRam, byte[] sprRom, ushort[] bm, int bw = 512, int ox = 0, int[]? xo = null)
    {
        Array.Clear(bm, 0, bw * 256);
        int banks = sprRom.Length / TileDecoder.SpriteBankSize;
        for (int n = 0; n < 32; n++)
        {
            int o = n * 16;
            if (spriteRam[o] == 0xFF) break;
            int top = spriteRam[o] + 1, bottom = spriteRam[o + 1] + 1;
            int xstart = (xo != null && xo[n] != int.MinValue ? xo[n]
                         : (spriteRam[o + 2] | spriteRam[o + 3] << 8) & 0x1FF) + ox;
            int b3 = spriteRam[o + 3];
            int bank = (((b3 & 0x80) >> 7) | ((b3 & 0x40) >> 5) | ((b3 & 0x20) >> 3)) % banks;
            int stride = spriteRam[o + 4] | spriteRam[o + 5] << 8;
            int src = spriteRam[o + 6] | spriteRam[o + 7] << 8;
            int bankBase = bank * TileDecoder.SpriteBankSize;            for (int y = top; y < bottom; y++)
            {
                src = (src + stride) & 0xFFFF;
                if (y < 0 || y >= Height) continue;
                bool flip = (src & 0x8000) != 0;
                int cur = src;
                for (int x = xstart; x < xstart + 1024; x += 4, cur = (cur + (flip ? -1 : 1)) & 0xFFFF)
                {
                    byte d = sprRom[bankBase + (cur & 0x7FFF)];
                    int c1 = flip ? d & 15 : d >> 4, c2 = flip ? d >> 4 : d & 15;
                    if (c1 == 15) break;
                    if (c1 != 0) Put(bm, bw, x, y, c1 | n * 16);
                    if (c2 == 15) break;
                    if (c2 != 0) Put(bm, bw, x + 2, y, c2 | n * 16);
                }
            }
        }
        return bm;
    }

    static void Put(ushort[] bm, int bw, int x, int y, int v)
    {
        for (int i = 0; i < 2; i++)
            if (x + i >= 0 && x + i < bw) bm[y * bw + x + i] = (ushort)v;
    }

    // width > 512 (WIDESCREEN, pantallas de titulo y de PUSH START): imagen de 'width' columnas con la de 512 centrada;
    // a los lados se repite la columna de tiles del borde del BG (con su scroll), sin FG ni sprites, para
    // que el fondo llene el ancho sin copiar el logotipo (el BG del titulo, $54A1, ocupa cols 6-28).
    // El FG tapa sus columnas 0-1 y 30-31 con el tile 1 (negro): en ancho se quitan (como WideView),
    // si no, esas franjas negras se verian y se copiarian a los lados.
    // barOnly (WIDESCREEN, pantallas de records y de recuento de puntos / bonus): solo la franja superior
    // llena el ancho (PLAYER1 a la izquierda, HIGH centrado, PLAYER2 a la derecha); el resto de la imagen
    // de 512 queda como ORIGINAL, centrada y con negro a los lados.
    // hudTop (WIDESCREEN, franjas y telon antes del nivel, WideView.WipeRoutine): las filas 0-1 (PLAYER1 /
    // HIGH / PLAYER2) van siempre repartidas a todo el ancho, aunque la franja negra no este entera.
    // bonus (WIDESCREEN, recuento de puntos / bonus del fin de ronda $199A): fondo a todo el ancho (como el
    // titulo) y leyendas como hudTop. Filas 2-3 (vidas $E884 y vitalidad) pegadas al borde izquierdo como en
    // el nivel (WideView: columna 2 en x = 0). Filas 4-27: el marco de tile 1 ($199A, columnas 2 y 29) va a
    // los bordes de la imagen (16 px cada uno) y el hueco entre el marco y la imagen centrada repite la
    // celda vecina (columna 3 a la izquierda, 28 a la derecha), asi el interior llena el ancho.
    // items (nivel y telon previo, no bonus): las filas 4-5 son el inventario (su fondo negro sigue BarOpacity).
    public static Image Render(byte[] pal, byte[] vram, byte[] spriteRam, byte[][] tiles, byte[] sprRom, byte[] lookupProm,
                               int width = Width, bool barOnly = false, bool hudTop = false, bool bonus = false,
                               bool items = false)
    {
        var fb = new FrameBuffer();
        Render(fb, pal, vram, spriteRam, tiles, sprRom, lookupProm, width, barOnly, hudTop, bonus, items);
        return fb.ToImage();
    }

    // Paginas BG / FG y sprites de Render: reservados una vez y reutilizados en cada fotograma.
    static readonly ushort[] bgBuf = new ushort[256 * 256], fgBuf = new ushort[256 * 256], sprBuf = new ushort[512 * 256];

    // Igual que Render, escribiendo los colores en fb (FrameBuffer.Upload los sube a la textura).
    public static void Render(FrameBuffer fb, byte[] pal, byte[] vram, byte[] spriteRam, byte[][] tiles, byte[] sprRom,
                              byte[] lookupProm, int width = Width, bool barOnly = false, bool hudTop = false,
                              bool bonus = false, bool items = false)
    {
        items &= !bonus;
        var bg = RenderPage(vram, 0, tiles, bgBuf);
        var fg = RenderPage(vram, 1, tiles, fgBuf);
        ushort[] spr;
        if (spriteRam != null && sprRom != null) spr = RenderSprites(spriteRam, sprRom, sprBuf);
        else { Array.Clear(sprBuf); spr = sprBuf; }
        int xscroll = ScrollX(vram), yscroll = ScrollY(vram);
        width = Math.Max(width, Width) & ~1;
        int ox = (width - Width) / 2 & ~1;                                  // par: pixel logico 2x1
        int bgL = ((0 - xscroll) & 0xFF) & ~7, bgR = ((255 - xscroll) & 0xFF) & ~7;   // tile BG del borde
        bool wideFg = width > Width;
        const int FgEdge = 32;                                              // 2 columnas FG = 32 px de imagen
        int bar = wideFg ? FgTopBar(fg) : 0;                                // franja negra superior a todo el ancho
        bonus &= wideFg;
        bool hud = wideFg && (bar >= HudTextH || hudTop || bonus);          // leyendas repartidas a lo ancho
        const int BonusHudH = 4 * 8;                                        // bonus: filas 2-3 (vidas)
        int barSide = hud ? Math.Max(bar, HudTextH) : bar;                  // lineas con los lados de la franja

        fb.Resize(width);
        var px = fb.Pixels;
        for (int y = 0; y < Height; y++)
        {
            int bgy = (y + yscroll) & 0xFF;
            for (int x = 0; x < width; x++)
            {
                int xs = x - ox, lx = xs >> 1;                                // lx < 0 o > 255: lado ancho
                bool inside = xs >= 0 && xs < Width;
                if (barOnly && !inside && y >= barSide) { px[y * width + x] = Color.Black; continue; }   // lados negros bajo la franja
                int fgpix, bgpix, sprpix;
                if (inside)
                {
                    // Ancha: sin las 2 columnas de cada borde del FG (tile 1 negro que tapa los lados).
                    fgpix = wideFg && !barOnly && (xs < FgEdge || xs >= Width - FgEdge) ? 0 : fg[y * 256 + lx];
                    bgpix = bg[bgy * 256 + ((lx - xscroll) & 0xFF)];
                    sprpix = spr[y * 512 + xs];
                }
                else
                {
                    bool left = xs < 0;
                    fgpix = 0;                                                // FG vacio: se ve el BG
                    bgpix = bg[bgy * 256 + (left ? bgL : bgR) + ((lx - xscroll) & 7)];
                    sprpix = 0;
                }
                if (y < bar && (xs < FgEdge || xs >= Width - FgEdge))
                    fgpix = FgBarPixel(fg, y, lx);                            // franja superior en los lados
                if (y < HudTextH && hud)                              // leyendas PLAYER1 / HIGH / PLAYER2
                    fgpix = HudTopPixel(fg, y, x, 0, width, ox);
                else if (bonus && y < BonusHudH)                      // vidas / vitalidad en el borde izquierdo
                {
                    int fx = y < HudTextH ? x + FgEdge                        // columna 2 en x = 0
                           : VitalityFx(x, 0, VitalityX(width, LivesW));      // vitalidad centrada
                    fgpix = fx >= FgEdge && fx < Width - FgEdge ? fg[y * 256 + (fx >> 1)]
                          : y < bar ? FgBarPixel(fg, y, x >> 1) : 0;
                }
                else if (bonus)                                       // filas 4-27: marco en los bordes
                {
                    int cell = x < 16 ? 2 * 8 + (x >> 1)
                             : x >= width - 16 ? 29 * 8 + ((x - width + 16) >> 1)
                             : xs < 3 * 16 ? 3 * 8 + ((xs >> 1) & 7)
                             : xs >= 29 * 16 ? 28 * 8 + ((xs >> 1) & 7)
                             : lx;
                    // HideCorners: las esquinas redondeadas (WideView.HiddenCorner) no se dibujan.
                    fgpix = WideView.HiddenCorner(vram, y >> 3, cell >> 3) ? 0 : fg[y * 256 + cell];
                }
                int idx = ((sprpix & 15) == 0 ? 1 : 0)
                        | ((fgpix & 7) == 0 ? 1 : 0) << 1
                        | ((fgpix >> 9) & 3) << 2
                        | ((bgpix & 7) == 0 ? 1 : 0) << 4
                        | ((bgpix >> 9) & 3) << 5;
                int sel = lookupProm[idx] & 3;
                int p = sel == 0 ? sprpix & 0x1FF : sel == 1 ? 0x200 | (fgpix & 0x1FF) : 0x400 | (bgpix & 0x1FF);
                // OpacityBars: fondo negro de las barras. ORIGINAL: sin las columnas 0-1 y 30-31 (tapan la costura).
                if (BarOpacity < 100 && sel == 1 && BarBlack(pal[p])
                    && !(inside && !wideFg && (lx < 16 || lx >= 240))
                    && (y < BarRowsH || (items && inside && y < ItemsH && ItemsCell(vram, y >> 3, lx >> 3))))
                {
                    int s2 = UnderSel(lookupProm, idx);
                    var under = s2 == 0 ? PalColor(pal[sprpix & 0x1FF]) : s2 == 1 ? Color.Black : PalColor(pal[0x400 | (bgpix & 0x1FF)]);
                    px[y * width + x] = BarBlend(under);
                    continue;
                }
                px[y * width + x] = PalColor(pal[p]);
            }
        }
    }

    // Porcentaje de pixeles con RGB igual (tolerancia 'tol' por canal) en el area comun.
    public static double Compare(Image a, Image b, int tol, int dx = 0, int dy = 0)
    {
        int w = Math.Min(a.Width, b.Width), h = Math.Min(a.Height, b.Height);
        long ok = 0, n = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int bx = x + dx, by = y + dy;
                if (bx < 0 || by < 0 || bx >= b.Width || by >= b.Height) continue;
                var ca = Raylib.GetImageColor(a, x, y);
                var cb = Raylib.GetImageColor(b, bx, by);
                n++;
                if (Math.Abs(ca.R - cb.R) <= tol && Math.Abs(ca.G - cb.G) <= tol && Math.Abs(ca.B - cb.B) <= tol) ok++;
            }
        return n == 0 ? 0 : 100.0 * ok / n;
    }

    // Pares (port -> referencia) de colores distintos, para calibrar la conversion de paleta.
    public static void DumpColorPairs(Image a, Image b, int max = 40)
    {
        var seen = new HashSet<(int, int)>();
        for (int y = 0; y < Math.Min(a.Height, b.Height); y++)
            for (int x = 0; x < Math.Min(a.Width, b.Width); x++)
            {
                var ca = Raylib.GetImageColor(a, x, y); var cb = Raylib.GetImageColor(b, x, y);
                int ka = ca.R << 16 | ca.G << 8 | ca.B, kb = cb.R << 16 | cb.G << 8 | cb.B;
                if (seen.Add((ka, kb)) && seen.Count <= max) Console.WriteLine($"  {ka:X6} -> {kb:X6}");
            }
    }
}
