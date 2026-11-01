using Raylib_cs;
using WonderBoy.Game;

namespace WonderBoy.Graphics;

// Vista panoramica (Fullscreen=WIDESCREEN, solo en niveles): imagen mas ancha que 512 con mas columnas
// del mapa a izquierda y derecha. Compone BG (anillo + cache + pasos futuros), sprites y FG (HUD centrado)
// con la misma regla de prioridad (PROM) que TitleRenderer.Render.
// El BG del original es un anillo de 32 columnas de 8 px (VRAM pagina 0, 256 px): TilemapFill ($22A2)
// escribe una columna por paso; columna de mundo W = entrada*16 + paso ($C30F-1, $C453-1), anillo = W & 31.
//   Izquierda: columnas que el juego ya escribio, guardadas al pasar por el anillo (cache).
//   Derecha:   pasos futuros de TilemapFill.Pass sobre una COPIA de la memoria (la del juego no se toca),
//              sin pasar del fin de nivel (tipo $0F en ($C44E)+n, el mismo que detiene la camara).
// Sin datos a un lado (inicio/fin de nivel) -> el mundo se desplaza para que no quede negro.
public sealed class WideView
{
    readonly Dictionary<int, byte[]> cache = new();   // columna de mundo -> 32 celdas (tile, atributo)
    int levelKey = -1, prevRaw = -1, wu;               // wu = ultima columna escrita, sin envolver
    // Vista congelada: $1B52 (vida perdida) cambia $C30F (punto de reinicio) sin escribir columnas ($C453
    // igual); la VRAM sigue con la vista anterior. rawOff mantiene la cuenta del anillo y aheadKeep las
    // columnas futuras del ultimo dibujo bueno hasta la siguiente carga ($1431) o Reset.
    readonly Dictionary<int, byte[]> aheadKeep = new();
    // Fila del anillo donde $2318 empezo cada columna ya escrita por el juego (columna de mundo -> $C45B).
    readonly Dictionary<int, int> tops = new();
    int prevC453 = -1, rawOff;
    bool frozen;
    // Vida perdida: $13E7 ($C07E=4, $0A73(1)) borra $C440-$C47F (camara y flujo, $C453 = 0) hasta la
    // carga $1431 ($C07E=6); el fondo de la VRAM sigue siendo el del nivel. En ese tramo (franja $1556)
    // la copia m recibe el ultimo $C440-$C47F bueno y el dibujo sigue a todo el ancho (vista congelada).
    readonly byte[] streamKeep = new byte[0x40];
    bool streamOk;
    // Copias de dibujo reservadas una vez (Render): memoria de 64 KB (entera en cada fotograma; Render la
    // modifica), paleta, VRAM, sprite RAM, pagina FG y sprites (este crece si el ancho es mayor).
    readonly byte[] mem = new byte[0x10000];
    readonly byte[] palBuf = new byte[0x800], vramBuf = new byte[0x1000], sramBuf = new byte[0x800];
    readonly ushort[] fgBuf = new ushort[256 * 256];
    ushort[] sprBuf = Array.Empty<ushort>();

    // true = en WIDESCREEN no se ven las 4 esquinas redondeadas (2 de arriba y 2 de abajo);
    // false = vuelve a como estaba (las 2 de arriba pegadas a los bordes de la pantalla ancha).
    public static bool HideCorners = true;

    // HideCorners fuera del nivel (TitleRenderer, pantalla de bonus y su telon): true = la celda (fila,
    // columna) del FG es una esquina redondeada, en cualquier sitio. Son siempre los mismos 4 tiles
    // (E1, E3, E4, E6, atributo $08): el telon $1585 los pone en las columnas 2 y 29 y el texto 6 del
    // bonus ($5429, $19DF) en las 3 y 28 (E1 6/3, E4 6/28, E3 27/3, E6 27/28). El grafico $543B del
    // bonus usa E0-E6 con atributo $01 (codigos $1Ex): no coincide.
    public static bool HiddenCorner(byte[] vram, int row, int col)
    {
        if (!HideCorners) return false;
        int code = FgCode(vram, row, col);
        return code == 0xE1 || code == 0xE3 || code == 0xE4 || code == 0xE6;
    }

    // Rutinas ($C081) de las franjas y del telon antes de cada nivel: $13D9 (franja $1556 tras $13E7),
    // $152D (franja de $1635) y $1651 (carga $1431 y telon $1585). Si no hay dibujo ancho del nivel,
    // en WIDESCREEN se dibujan con las leyendas PLAYER1 / HIGH / PLAYER2 a todo el ancho.
    public static bool WipeRoutine(int routine) => routine == 0x13D9 || routine == 0x152D || routine == 0x1651;

    public void Reset()
    {
        cache.Clear(); aheadKeep.Clear(); tops.Clear(); levelKey = -1; prevRaw = -1; prevC453 = -1; rawOff = 0; frozen = false;
        streamOk = false;
        ScreenBounds.SetOriginal();
    }

    static int Rw(byte[] m, int a) => m[a & 0xFFFF] | m[(a + 1) & 0xFFFF] << 8;

    // Codigo de tile (12 bits, como RenderPage) de la celda fila/columna del FG (pagina 1).
    internal static int FgCode(byte[] vram, int row, int col)
    {
        int o = 0x800 + (row * 32 + col) * 2, td = vram[o] | vram[o + 1] << 8;
        return ((td >> 4) & 0x800) | (td & 0x7FF);
    }

    // Columna r del anillo (pagina 0 en src+off): 32 filas x 2 bytes.
    static byte[] Column(byte[] src, int off, int r)
    {
        var c = new byte[64];
        for (int row = 0; row < 32; row++)
        {
            int o = off + (row * 32 + r) * 2;
            c[row * 2] = src[o]; c[row * 2 + 1] = src[o + 1];
        }
        return c;
    }

    // Solo dibujo: misma cuenta que WorldToScreen.Slot ($23CF-$24EA) para las 17 ranuras, pero sin el
    // limite X de la pantalla original (x < 12 recorta / x >= $1FC descarta, $2406/$2452). Para cada
    // ranura activa que el juego dejo fuera (bit5 = 0) o recortada por la izquierda (bit5 = 1 y x < 12,
    // $2406: sin esto desaparece en parte al cruzar el borde ORIGINAL) y que cae en [xMin, xMax)
    // (ScreenBounds.Left/Right, X de sprite 512 px) escribe la entrada n de la sprite RAM (copia) sin
    // recorte y devuelve su X con signo; resto int.MinValue.
    static int[] WideSprites(byte[] m, byte[] sram, int xMin, int xMax)
    {
        var xo = new int[32];
        Array.Fill(xo, int.MinValue);
        int cy = Rw(m, 0xC444);
        for (int n = 0; n < WorldToScreen.SlotCount; n++)
        {
            int ix = WorldToScreen.Slots + n * WorldToScreen.SlotSize;
            if (m[ix + 0x1F] == 0) continue;
            int x = ScreenBounds.ScreenX(m, ix);                                   // $23CF-$23EC
            // Roca $04 en espera ($3B80 estado 1 / $3BD6 estado 4, bit5 = 0): igual que ORIGINAL, no se ve
            // hasta que arranca (con NewLogicWide=T al tocar el borde derecho ancho, Enemies.H3B6F $3BD6;
            // con F al entrar en la ventana original, $2452 pone bit5).
            if (ix >= 0xC8A0 && m[ix + 0x18] == 0x04 && (m[ix + 0x1F] == 0x01 || m[ix + 0x1F] == 0x04)
                && (m[ix + 0x1E] & 0x20) == 0) continue;
            if ((m[ix + 0x1E] & 0x20) != 0 && x >= 12) continue;                   // el original ya la dibuja entera
            int yw = Rw(m, ix + 0x12);
            int c4 = m[ix + 0x04];
            if (x >= xMax || x < xMin - 1024) continue;
            // $2475: Y igual que el original (sin cambios de la ventana vertical).
            int ysum = (yw + (m[ix + 0x08] >> 1)) & 0xFFFF;
            if (ysum >= cy) continue;
            int y = (ysum - cy) & 0xFFFF;
            if ((y >> 8) != 0xFF) continue;
            int top = (-(y & 0xFF)) & 0xFF;
            if (top >= 0xE0) continue;
            int src = Rw(m, ix + 0x1C), s = top + m[ix + 0x08], bottom;
            if ((c4 & 0x80) == 0) bottom = s > 0xFF ? 0xFF : s;                    // $24A4
            else if (s <= 0xFF && s < 0xDE) bottom = s;                            // $24AF
            else                                                                   // $24BC
            {
                src = (src - ((((c4 - 1) & 0xFF) * ((s - 0xDD) & 0xFF)) & 0xFFFF)) & 0xFFFF;
                bottom = 0xDF;
            }
            int o = n * 16;                                                        // SpriteCopy
            sram[o] = (byte)top; sram[o + 1] = (byte)bottom;
            sram[o + 2] = 0; sram[o + 3] = (byte)(m[ix + 0x1D] & 0x80);            // banco: bit7 de IX+7 ($2464)
            sram[o + 4] = m[ix + 0x04]; sram[o + 5] = m[ix + 0x05];
            sram[o + 6] = (byte)src;
            sram[o + 7] = (byte)((src >> 8 & 0x7F) | (m[ix + 0x09] & 0x80));
            xo[n] = x;
        }
        return xo;
    }

    public void Render(FrameBuffer fb, TitleRom game, byte[][] tiles, byte[] sprRom, byte[] prom, int width)
    {
        var m = mem;
        game.CopyMemory(m);
        var pal = palBuf;
        Array.Copy(m, 0xD800, pal, 0, 0x800);
        var vram = vramBuf;
        Array.Copy(m, 0xE000, vram, 0, 0x1000);
        // $13D9 tras $13E7 (vida perdida, $C453 = 0): ultimo $C440-$C47F bueno (streamKeep) en la copia.
        // Telon $199A del fin de ronda (RoundEndCurtain, $C07E=7): tambien con el ultimo $C440-$C47F bueno.
        bool roundCurtain = game.RoundEndCurtain;
        if (roundCurtain && streamOk) Array.Copy(streamKeep, 0, m, 0xC440, 0x40);
        else if (m[0xC453] == 0 && streamOk && m[0xC07E] == 4) Array.Copy(streamKeep, 0, m, 0xC440, 0x40);
        int c453 = m[0xC453];
        ScreenBounds.SetOriginal();
        if (c453 == 0 || c453 > 0x10 || width <= TitleRenderer.Width)
        {
            // Franja $1556 de $152D sin flujo ($C453 = 0 desde $13E7): leyendas a todo el ancho (barOnly).
            Array.Copy(m, 0xD000, sramBuf, 0, 0x800);
            if (width > TitleRenderer.Width && WipeRoutine(m[0xC081] | m[0xC082] << 8))
                TitleRenderer.Render(fb, pal, vram, sramBuf, tiles, sprRom, prom, width, true, true, items: true);
            else
                TitleRenderer.Render(fb, pal, vram, sramBuf, tiles, sprRom, prom, items: true);
            return;
        }
        if (!game.LevelLoading && !roundCurtain) { Array.Copy(m, 0xC440, streamKeep, 0, 0x40); streamOk = true; }

        // Ultima columna escrita y desenvuelta (la entrada $C30F & $7F envuelve cada 2048 columnas).
        int raw = (((m[0xC30F] - 1) & 0x7F) << 4) + c453 - 1;
        int key = Rw(m, 0xC454);
        // Carga de ronda a medias ($1431): las ranuras sin escribir son restos y no deben quedar en la
        // cache como mapa a la izquierda; se rehace entera en el primer fotograma con la carga completa.
        if (game.LevelLoading) prevRaw = -1;
        if (key != levelKey || prevRaw < 0)
        {
            cache.Clear(); aheadKeep.Clear(); tops.Clear(); levelKey = key; wu = raw; rawOff = 0; frozen = false;
        }
        else if (c453 == prevC453 && raw != prevRaw)
        {
            rawOff += prevRaw - raw;   // $C30F cambio sin pasada de $22A2: misma vista (congelada)
            frozen = true;
        }
        else
        {
            int d = (raw - prevRaw) & 2047;
            if (d >= 1024) { cache.Clear(); aheadKeep.Clear(); tops.Clear(); wu = raw; rawOff = 0; frozen = false; }   // vuelta atras: reinicio
            else wu += d;
        }
        // Durante la carga y en el primer fotograma tras ella se rehace la cache: las columnas de los
        // fotogramas de carga eran restos (basura a la izquierda al continuar tras GAME OVER).
        prevRaw = game.LevelLoading ? -1 : raw;
        prevC453 = c453;
        raw += rawOff;                 // cuenta efectiva del anillo (igual a la del juego si no esta congelada)
        for (int r = 0; r < 32; r++) cache[wu - ((raw - r) & 31)] = Column(vram, 0, r);
        // Filas de inicio de las columnas de la entrada actual ($C453 pasos): el paso i uso
        // $C45B - $C45A * ($C453/4 - i/4) ($236E suma $C45A cada 4 pasos). Las del borde derecho del
        // original (tapadas alli por el FG) se ven en WIDESCREEN y en una subida darian la vuelta al anillo.
        if (!frozen && rawOff == 0)
            for (int i = 0; i < c453; i++)
                tops[wu - (c453 - 1 - i)] = (m[0xC45B] - m[0xC45A] * ((c453 >> 2) - (i >> 2))) & 31;
        if (cache.Count > 4096)
            foreach (int k in cache.Keys.Where(k => k < wu - 1024).ToList()) { cache.Remove(k); tops.Remove(k); }

        // Posicion de mundo del pixel central (sx = 128, espacio 256): misma regla que Render.
        int xs = TitleRenderer.ScrollX(vram), ys = TitleRenderer.ScrollY(vram);
        int bc = (128 - xs) & 0xFF;
        int wpC = (wu - ((raw - (bc >> 3)) & 31)) * 8 + (bc & 7);
        int off = (width - TitleRenderer.Width) / 2 & ~1;
        int needR = (wpC - 128 + width / 2 + 8) >> 3;   // peor caso: mundo desplazado del todo a la izquierda

        // Columnas futuras en la copia, hasta el fin de nivel. Vista congelada: las del ultimo dibujo bueno
        // (la copia ya apunta al punto de reinicio y daria columnas de otra parte del nivel).
        var ahead = aheadKeep;
        if (!frozen)
        {
            int limit = int.MaxValue;
            if (m[0xC451] != 0) limit = wu;
            else
            {
                int typ = Rw(m, 0xC44E), c443 = m[0xC443];
                for (int n = c443; n < 256 && n < c443 + 16; n++)
                    if ((m[(typ + n) & 0xFFFF] & 0x0F) == 0x0F) { limit = wu + n * 16 - (Rw(m, 0xC442) >> 4); break; }
            }
            ahead.Clear();
            for (int w = wu + 1; w <= needR && w <= limit; w++)
            {
                TilemapFill.Pass(m);
                // Byte 64 = fila del anillo donde $2318 empezo esta columna ($C45B; $236E ya la movio si
                // $C453 & 3 == 0). En una subida la columna anticipada empieza mas arriba que la vista actual
                // y su parte de abajo daria la vuelta al anillo (cielo azul abajo a la derecha).
                var c = new byte[65];
                Column(m, 0xE000, w & 31).CopyTo(c, 0);
                c[64] = (byte)((m[0xC45B] - ((m[0xC453] & 3) == 0 ? m[0xC45A] : 0)) & 31);
                ahead[w] = c;
            }
        }

        // Mundo (BG + sprites) en offW; FG (pagina 1 fija) centrado en off salvo el HUD (borde izquierdo).
        // Si a un lado no hay mapa (inicio/fin de nivel), el mundo se desplaza para llenar el ancho
        // sin barras negras (como la camara de los ports modernos); la logica del juego no cambia.
        int minCol = wu, maxCol = wu;
        foreach (int k in cache.Keys) if (k < minCol) minCol = k;
        foreach (int k in ahead.Keys) if (k > maxCol) maxCol = k;
        int lo = width - 2 * ((maxCol + 1) * 8 - wpC + 128) + 1;   // borde derecho dentro del mapa
        int hi = 2 * (wpC - 128 - minCol * 8);                     // borde izquierdo dentro del mapa
        int offW = lo > hi ? (lo + hi) / 2 : Math.Clamp(off, lo, hi);
        offW = Math.Clamp(offW, 0, width - TitleRenderer.Width) & ~1;   // la pantalla original siempre entera
        ScreenBounds.SetWide(offW, width);                          // bordes del mundo visible (WIDESCREEN)

        var fg = TitleRenderer.RenderPage(vram, 1, tiles, fgBuf);
        // Sprites en todo el ancho: misma lista que el original, X desplazada offW, sin recorte a 512.
        // Objetos activos fuera de la ventana original (IX+$1E bit5 = 0) pero dentro de la ancha: se
        // anaden al dibujo con la X sin recortar; la memoria del juego no se toca (copia m).
        // Ranuras y camara del mismo paso que la sprite RAM (TitleRom.ApplySpriteSnapshot; m es copia).
        var sram = sramBuf;
        Array.Copy(m, 0xD000, sram, 0, 0x800);
        game.ApplySpriteSnapshot(m);
        var xo = WideSprites(m, sram, ScreenBounds.Left, ScreenBounds.Right);
        // Ranuras libres (IX+$1F = 0) con sprite: dibujo fijo de pantalla, no del mundo (GAME OVER de
        // $0C2E en $C800, X = $98). Van centradas (off) aunque el mundo este desplazado (offW).
        for (int n = 0; n < WorldToScreen.SlotCount; n++)
        {
            int o = n * 16;
            if (sram[o] == 0xFF) break;
            if (xo[n] != int.MinValue || m[WorldToScreen.Slots + n * WorldToScreen.SlotSize + 0x1F] != 0) continue;
            xo[n] = ((sram[o + 2] | sram[o + 3] << 8) & 0x1FF) + off - offW;
        }
        if (sprBuf.Length < width * 256) sprBuf = new ushort[width * 256];
        var spr = TitleRenderer.RenderSprites(sram, sprRom, sprBuf, width, offW, xo);

        // HUD (filas 0-5 del FG): su primera columna visible (columna 2; las 0-1 van tapadas, fx < FgEdge)
        // cae en ScreenBounds.Left, el unico margen izquierdo (el mismo tope del jugador y de los objetos).
        // hudX = x de la imagen donde cae Left (sprite X + offW, como RenderSprites).
        // Filas 4-5 = inventario (el cuadro de objetos). Tambien durante el telon de la presentacion
        // ($1651, $C07F 0-1): $1585 escribe ahi las letras del cuadro (columnas 2-11) y el resto de
        // esas filas a 0, asi que van siempre con el HUD (pegadas a Left) desde el primer fotograma.
        const int FgEdge = 32;                                 // 2 columnas tapadas de cada borde del FG
        int hudX = ScreenBounds.Left + offW;
        const int hudH = 6 * 8;                                // vidas e inventario (filas 0-5)
        // Filas 0-1 (leyendas, TitleRenderer.HudTopPixel): PLAYER1 y su puntuacion pegados a Left, PLAYER2
        // y la suya a Right, HIGH centrado (off); su fondo negro llena todo el ancho. Filas 2-5 pegadas a Left (salvo la vitalidad, centrada).
        const int textH = TitleRenderer.HudTextH;
        int hudR = ScreenBounds.Right + offW;                  // x de la imagen donde cae Right
        // Barra de vitalidad (filas 2-3): su fondo negro (FG opaco en las columnas 2-29, TitleRenderer.FgTopBar)
        // se extiende a todo el ancho fuera de las 28 columnas del HUD con el tile negro de la columna 0.
        int bar = TitleRenderer.FgTopBar(fg, hudH);
        // Filas 2-3: vidas pegadas a Left y barra de vitalidad (columnas 14-29) centrada en el ancho de la imagen.
        int vitX = TitleRenderer.VitalityX(width, hudX + TitleRenderer.LivesW);
        // Esquinas redondeadas del campo de juego (FG, las pone el telon $1585): E1 en fila 6 columna 2
        // (arriba a la izquierda, ultimo paso) y E4 en fila 4 columna 29 (arriba a la derecha, primer paso).
        // WIDESCREEN: E1 se mueve solo en X hasta ScreenBounds.Left (misma fila 6); E4 va a
        // ScreenBounds.Right y a la fila 0 (lo mas arriba, el HUD no llega a ese borde). Su sitio original
        // queda vacio. 16 px de imagen = 8 px del FG (una celda).
        bool cornerL = FgCode(vram, 6, 2) == 0xE1, cornerR = FgCode(vram, 4, 29) == 0xE4;
        int crX = ScreenBounds.Right + offW - 16;              // x de la imagen de la esquina derecha
        // HideCorners: E1 (fila 6) y E4 (fila 4) arriba, E3 y E6 (fila 27, la de abajo, telon $1585) abajo
        // no se dibujan: su celda (vista a traves del FG/HUD) queda vacia y no se pintan en otro sitio.
        bool hide = HideCorners;
        bool cornerBL = hide && FgCode(vram, 27, 2) == 0xE3, cornerBR = hide && FgCode(vram, 27, 29) == 0xE6;
        // Telon a todo el ancho (WIDESCREEN): el telon $1585 ($C07E=6, $C07F 0-1) y la franja de fin de ronda
        // $1556 ($C07E=5, $C07F>=1; tras vida perdida en $13D9: $C07E=4, $C07F>=1, tabla $4F20[4]) tapan
        // con el tile 1 las columnas 2-29 del FG. En las filas 4-27 la
        // franja ancha [Left, Right) se reparte en esas 28 columnas (columna virtual vc): si la celda vc es
        // tile 1, la x va tapada; si no, la celda en su sitio normal se dibuja salvo que sea tile 1. Asi el
        // borde del telon recorre todo el ancho al mismo ritmo que el original (una columna por paso).
        int st = m[0xC07E], sub = m[0xC07F];
        // Tambien el telon de fin de ronda $199A ($19A0: filas 4-27, columnas 2-29, tile 1, una fila por paso).
        bool curtain = (st == 6 && sub <= 1) || ((st == 5 || st == 4) && sub >= 1) || roundCurtain;
        int curW = ScreenBounds.Right - ScreenBounds.Left;     // ancho visible (imagen)
        fb.Resize(width);
        var pix = fb.Pixels;
        for (int x = 0; x < width; x++)
        {
            int xw = x - offW, xf = x - off;
            int wp = wpC + (xw >> 1) - 128;
            int col = wp >> 3, px = wp & 7;
            byte[]? data = col > wu ? ahead.GetValueOrDefault(col) : cache.GetValueOrDefault(col);
            int colTop = data == null ? -1 : data.Length > 64 ? data[64] : tops.GetValueOrDefault(col, -1);
            for (int y = 0; y < TitleRenderer.Height; y++)
            {
                int fx = y < textH || y >= hudH ? xf
                       : y < TitleRenderer.BarRowsH ? TitleRenderer.VitalityFx(x, hudX, vitX)   // vidas / vitalidad
                       : x - hudX + FgEdge;                                                    // inventario
                // FG sin las 2 columnas de cada borde: alli el original tapa la costura del anillo en negro.
                bool fgOn = fx >= FgEdge && fx < TitleRenderer.Width - FgEdge;
                int fgpix = fgOn ? fg[y * 256 + (fx >> 1)] : 0;
                int frow = y >> 3, fcol = fx >> 4;
                // OpacityBars: filas 0-3 (puntuacion, vidas y vitalidad) y celdas del inventario (filas 4-5);
                // las esquinas y el telon que se pongan encima quedan opacos.
                bool barPix = y < TitleRenderer.BarRowsH
                           || (y < hudH && fgOn && TitleRenderer.ItemsCell(vram, frow, fcol));
                if (y < textH) fgpix = TitleRenderer.HudTopPixel(fg, y, x, hudX, hudR, off);   // leyendas y franja
                else if (y < bar && !fgOn) fgpix = TitleRenderer.FgBarPixel(fg, y, (x - hudX) >> 1);   // fondo de la vitalidad
                if (hide)                                                          // las 4 esquinas ocultas
                {
                    if (fgOn && ((cornerL && frow == 6 && fcol == 2) || (cornerR && frow == 4 && fcol == 29)
                              || (cornerBL && frow == 27 && fcol == 2) || (cornerBR && frow == 27 && fcol == 29)))
                        fgpix = 0;
                }
                else
                {
                    if (fgOn && ((cornerL && frow == 6 && fcol == 2) || (cornerR && frow == 4 && fcol == 29)))
                        fgpix = 0;                                                 // esquina fuera de su sitio
                    if (cornerL && frow == 6 && x >= hudX && x < hudX + 16)        // E1 en Left
                    {
                        int c = fg[y * 256 + 2 * 8 + ((x - hudX) >> 1)];
                        if ((c & 7) != 0) { fgpix = c; barPix = false; }
                    }
                    if (cornerR && y < 8 && x >= crX)                              // E4 en Right, fila 0
                    {
                        int c = fg[(4 * 8 + y) * 256 + 29 * 8 + ((x - crX) >> 1)];
                        if ((c & 7) != 0) { fgpix = c; barPix = false; }
                    }
                }
                if (curtain && frow >= 4)                                          // telon a todo el ancho
                {
                    int cx = x - hudX, vc = 2 + cx * 28 / curW;
                    if (FgCode(vram, frow, vc) == 1) { fgpix = fg[y * 256 + vc * 8 + ((cx >> 1) & 7)]; barPix = false; }
                    else if (fgOn && FgCode(vram, frow, fcol) == 1) fgpix = 0;
                }
                int sprpix = spr[y * width + x];
                int bgpix = 0;
                if (data != null)
                {
                    int bgy = (y + ys) & 0xFF, row = bgy >> 3;
                    if (colTop >= 0)   // fila de inicio conocida: sin dar la vuelta al anillo de 32 filas
                    {
                        int top = (colTop * 8 - ys) & 0xFF;        // y de pantalla de su primera celda
                        if (top > TitleRenderer.Height / 2) top -= 256;   // columna centrada respecto a la vista
                        int k = (y - top) >> 3;                    // celda de la columna (0-31 dentro)
                        if (k >= 32) k = 28 + (k & 3);             // debajo: repite las 4 ultimas (suelo)
                        else if (k < 0) k &= 3;                    // encima: repite las 4 primeras (cielo)
                        row = (colTop + k) & 31;
                    }
                    int td = data[row * 2] | data[row * 2 + 1] << 8;
                    int code = (((td >> 4) & 0x800) | (td & 0x7FF)) % tiles.Length;
                    bgpix = ((td >> 5) & 0xFF) * 8 + tiles[code][(bgy & 7) * 8 + px];
                }
                int idx = ((sprpix & 15) == 0 ? 1 : 0)
                        | ((fgpix & 7) == 0 ? 1 : 0) << 1
                        | ((fgpix >> 9) & 3) << 2
                        | ((bgpix & 7) == 0 ? 1 : 0) << 4
                        | ((bgpix >> 9) & 3) << 5;
                int sel = prom[idx] & 3;
                if (sel >= 2 && data == null) { pix[y * width + x] = Color.Black; continue; }   // sin mapa: negro
                int p = sel == 0 ? sprpix & 0x1FF : sel == 1 ? 0x200 | (fgpix & 0x1FF) : 0x400 | (bgpix & 0x1FF);
                if (barPix && TitleRenderer.BarOpacity < 100 && sel == 1 && TitleRenderer.BarBlack(pal[p]))   // fondo negro de barra
                {
                    int s2 = TitleRenderer.UnderSel(prom, idx);
                    var under = s2 == 0 ? TitleRenderer.PalColor(pal[sprpix & 0x1FF])
                              : s2 == 1 || data == null ? Color.Black : TitleRenderer.PalColor(pal[0x400 | (bgpix & 0x1FF)]);
                    pix[y * width + x] = TitleRenderer.BarBlend(under);
                    continue;
                }
                pix[y * width + x] = TitleRenderer.PalColor(pal[p]);
            }
        }
    }
}
