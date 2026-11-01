namespace WonderBoy.Game;

// Limites X del mundo visible segun el modo de pantalla (Fullscreen=NO / ORIGINAL / WIDESCREEN).
// Unico lugar con los bordes donde los objetos desaparecen y donde se detiene el jugador:
//   Left  (PosIzquierdaDesaparece) y Right (PosDerechaDesaparece), X de pantalla en espacio de sprite
//   (512 px), la misma X que calcula $23EC: X - de - ($C442 - $100).
//   NO / ORIGINAL : [0, 512), la ventana original. Visibilidad = IX+$1E bit5 ($23CF-$2464) y retencion del
//                   jugador = $26FA tal cual: identico a la ROM.
//   WIDESCREEN    : en nivel, [-offW, ancho - offW) del ultimo dibujo ancho (WideView.Render lo fija).
// Left es la UNICA variable del margen izquierdo en WIDESCREEN. La leen: $2D7F (OffScreen) y los borrados
// por "fuera de pantalla" (Enemies, Weapon, PlayerRoutine: Visible = bit5, OnScreen = bit1), el dibujo
// de sprites anchos (WideView.WideSprites), el HUD (WideView.Render) y el tope del jugador hacia atras
// ($26FA: el jugador se detiene con su X de pantalla en Left).
// La activacion de objetos al entrar por la derecha (bit5 = 1) no cambia en ningun modo.
public static class ScreenBounds
{
    public const int OriginalLeft = 0, OriginalRight = 512;

    // true solo en WIDESCREEN dentro de un nivel (tras un dibujo ancho); false = ORIGINAL / NO.
    public static bool Wide { get; private set; }
    // Borde izquierdo y derecho (X de sprite, espacio 512) del mundo visible.
    public static int Left { get; private set; } = OriginalLeft;
    public static int Right { get; private set; } = OriginalRight;

    // Automatico (sin configuracion): true en WIDESCREEN y false en ORIGINAL / NO. En WIDESCREEN la roca en espera arranca al
    // tocar el borde derecho ancho (Enemies.H3B6F $3BD6) y el enemigo en espera de $4844 (tipos $20/$21) sale al
    // quedar detras del borde izquierdo ancho (BehindLeft). false = logica original ($34F7/$4844 tal cual).
    public static bool NewLogicWide => Wide;   // automatico: true en WIDESCREEN, false en ORIGINAL / NO

    public static void SetOriginal() { Wide = false; Left = OriginalLeft; Right = OriginalRight; }

    // offW = margen extra a la izquierda, width = ancho total (espacio 512) del dibujo ancho.
    public static void SetWide(int offW, int width) { Wide = true; Left = -offW; Right = width - offW; }

    static int Rw(byte[] m, int a) => m[a & 0xFFFF] | m[(a + 1) & 0xFFFF] << 8;

    // |IX+4| (bytes por linea del sprite; 4 px de ancho cada uno en espacio 512).
    public static int SpriteBytes(byte[] m, int ix)
    {
        int c4 = m[ix + 0x04];
        return (c4 & 0x80) != 0 ? (-c4) & 0xFF : c4;
    }

    // $23CF-$23EC: X de pantalla con signo (sin recorte) de la ranura IX.
    public static int ScreenX(byte[] m, int ix)
    {
        int de = (SpriteBytes(m, ix) * 2) & 0xFF;                    // $23CF
        int a9 = m[ix + 0x09] & 0x7F;
        if ((a9 & 0x40) != 0) a9 |= 0xFF80;
        de = (de + a9) & 0xFFFF;
        int cam = (Rw(m, 0xC442) - 0x100) & 0xFFFF;
        return (short)((Rw(m, ix + 0x10) - de - cam) & 0xFFFF);      // $23EC
    }

    // WIDESCREEN: true si algo del objeto cae en la franja ancha [Left, Right). ORIGINAL / NO: false.
    public static bool InWideView(byte[] m, int ix)
    {
        if (!Wide) return false;
        int x = ScreenX(m, ix);
        return x + SpriteBytes(m, ix) * 4 > Left && x < Right;
    }

    // WIDESCREEN + NewLogicWide: true si la X de mundo x (sin ancho de sprite, como $4844) quedo en o detras de
    // Left. Es la cuenta de $4844 ($C442 - $100 >= x) con el borde 0 cambiado por Left.
    public static bool BehindLeft(byte[] m, int x) => (short)((x - Rw(m, 0xC442) + 0x100) & 0xFFFF) <= Left;

    // Visible para los borrados por "fuera de pantalla": IX+$1E bit5 (ventana original) o franja ancha.
    public static bool Visible(byte[] m, int ix) => (m[ix + 0x1E] & 0x20) != 0 || InWideView(m, ix);

    // $2475-$2493: la Y cae en la ventana vertical (la misma cuenta que pone IX+$1E bit1).
    public static bool YInView(byte[] m, int ix)
    {
        int ysum = (Rw(m, ix + 0x12) + (m[ix + 0x08] >> 1)) & 0xFFFF;
        int cy = Rw(m, 0xC444);
        if (ysum >= cy) return false;                                // $248B ret nc
        int y = (ysum - cy) & 0xFFFF;
        return (y >> 8) == 0xFF && ((-(y & 0xFF)) & 0xFF) < 0xE0;   // $248C / $2493
    }

    // En pantalla (X e Y) para los borrados por IX+$1E bit1: bit1 (ventana original) o franja ancha
    // con la Y de $2475. ORIGINAL / NO: solo bit1, igual que la ROM.
    public static bool OnScreen(byte[] m, int ix) => (m[ix + 0x1E] & 0x02) != 0 || (InWideView(m, ix) && YInView(m, ix));

    // $26FA: true = el jugador (ranura IX, velocidad negativa) no puede retroceder mas.
    //   ORIGINAL / NO: la ROM, d = ($C442 - X jugador) y solo el byte bajo (ld a,l / cp $C0).
    //   WIDESCREEN: el unico margen izquierdo es Left; el jugador se detiene con su X de pantalla en Left.
    public static bool PlayerBackLimit(byte[] m, int ix)
    {
        if (!Wide) return ((Rw(m, 0xC442) - Rw(m, ix + 0x10)) & 0xFF) >= 0xC0;   // $270C-$270F
        return ScreenX(m, ix) <= Left;
    }

    // WIDESCREEN: tras mover hacia atras, si el jugador paso de Left lo deja justo en Left
    // (la velocidad mueve varios px por cuadro). ORIGINAL / NO: nada (la ROM).
    public static void ClampPlayerLeft(byte[] m, int ix)
    {
        if (!Wide) return;
        int x = ScreenX(m, ix);
        if (x >= Left) return;
        int w = (Rw(m, ix + 0x10) + (Left - x)) & 0xFFFF;
        m[ix + 0x10] = (byte)w; m[ix + 0x11] = (byte)(w >> 8);
    }
}
