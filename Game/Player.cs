namespace WonderBoy.Game;

// Jugador (objeto IX=$C840). Hito 1: solo movimiento horizontal.
// Traduccion de $252B/$2532 (velocidad objetivo + aceleracion), $26FA (tope izquierdo
// de camara) y $2725 (integracion de la posicion en punto fijo).
// Pendiente: colision con paredes ($297B), tile resbaladizo real ($0A92 sobre $C44A),
// fin de nivel ($C440 -> $2640) y animacion ($2631).
// Hito 2: estados 2 (suelo, $2889), 3 (sube, $2991) y 4 (cae, $2A15), integracion
// vertical $43B2, suelo $28FB/$2660 y pared $297B. Pendiente: animacion $1FC0 (Height fijo),
// estados 1 (muerte), 5 (rebote), 0 y $2817, plataformas ($C466/$C467).
public sealed class Player
{
    public byte State = 0x82; // $C85F (ix+$1F): bit7 = estado ya inicializado, bits 0-3 estado
    public ushort Y;          // $C852/$C853 (ix+$12/$13): Y de mundo, crece hacia arriba
    public byte YFrac;        // $C856 (ix+$16)
    public sbyte VelY;        // $C857 (ix+$17): 1/16 px por fotograma
    public byte Height = 0x20;// $C848 (ix+$08): alto del frame de animacion ($1FC0)
    public bool JumpLatch;    // $C084 bit5: salto pendiente (lo pone $00A4 al pulsar)
    public bool JumpHeld;     // $C085 bit0: boton de salto pulsado el fotograma anterior
    public Terrain? Terrain;

    public ushort X;          // $C850/$C851 (ix+$10/$11): X de mundo, parte entera
    public byte XFrac;        // $C855 (ix+$15): fraccion de X (1/256 px)
    public sbyte VelX;        // $C854 (ix+$14): velocidad, 1/16 px por fotograma
    public byte BaseSpeed = 0x30; // $C858 (ix+$18): velocidad de crucero
    public bool FacingLeft;   // ix+$1E bit 4

    // $2532: calcula la velocidad objetivo segun $C084 (bit2=derecha, bit3=izquierda)
    // y $C086 bit1 (correr, +$18); luego acelera ix+$14 hacia el objetivo.
    // onGround = (ix+$1F & $0F) == 2; slippery = tile tipo $50 bajo el jugador.
    public void UpdateVelocity(bool left, bool right, bool run, bool onGround,
                               bool skateboard = false, bool slippery = false)
    {
        if (skateboard)
        {
            // $2539: con monopatin la velocidad se fija sin aceleracion.
            if (!left && !right) { VelX = (sbyte)BaseSpeed; return; }
            byte extra = (byte)(run ? 0x08 : 0x00);
            if (right) { FacingLeft = false; VelX = (sbyte)(byte)(BaseSpeed + 0x18 + extra); }
            else       { FacingLeft = true;  VelX = (sbyte)(byte)(BaseSpeed - 0x18 - extra); }
            return;
        }

        // $2593: objetivo C (derecha tiene prioridad si ambas estan pulsadas).
        byte target = 0;
        if (left || right)
        {
            byte c = BaseSpeed;
            if (run) c = (byte)(c + 0x18);
            if (right) FacingLeft = false;
            else { FacingLeft = true; c = (byte)-c; }
            target = c;
        }

        // $25F5: paso 3 en suelo (1 si el tile es tipo $50), 1 en el aire.
        byte step = (byte)(onGround && !slippery ? 3 : 1);

        // $2613: comparacion con sesgo $80 (con signo) y saturacion al objetivo.
        byte cb = (byte)(target + 0x80);
        byte a = (byte)((byte)VelX + 0x80);
        if (a == cb) return;
        if (a < cb) { a = (byte)(a + step); if (a >= cb) a = cb; }
        else        { a = (byte)(a - step); if (a < cb) a = cb; }
        VelX = (sbyte)(byte)(a - 0x80);
    }

    // $26FA: si se mueve a la izquierda y (camara - X) & $FF >= $C0 (X a <= $40 px del
    // borde izquierdo de la camara $C442), anula la velocidad y no mueve.
    public void ApplyVelocity(ushort cameraX)
    {
        if (VelX < 0)
        {
            byte l = (byte)(cameraX - X);
            if (l >= 0xC0) { VelX = 0; return; }
        }
        Integrate();
    }

    // $2725: X(16.8) += vel(4.4 con signo) * 16.
    public void Integrate()
    {
        byte v = (byte)VelX;
        int whole = VelX >> 4;                 // hl: desplazamiento aritmetico
        int frac = ((v << 4) & 0xFF) + XFrac;  // add a,(ix+$15)
        XFrac = (byte)frac;
        int carry = frac >> 8;                 // adc hl,de
        X = (ushort)(X + whole + carry);
    }

    // $00A4 (ISR): bit5 de $C084 se pone al pulsar el salto ($C086 bit2) y se borra al
    // soltarlo; el estado 2 lo consume. Se llama antes de Step en cada fotograma.
    public void ReadJumpButton(bool pressed)
    {
        if (JumpHeld) { if (!pressed) { JumpHeld = false; JumpLatch = false; } }
        else if (pressed) { JumpHeld = true; JumpLatch = true; }
    }

    // $274D/$2766: un fotograma del jugador en los estados 2/3/4.
    // cameraY = $C444/$C445 (altura de camara, entrada hasta que exista el modulo).
    public void Step(bool left, bool right, bool run, ushort cameraX, ushort cameraY,
                     bool skateboard = false, bool slippery = false)
    {
        switch (State & 0x0F)
        {
            case 2: StepGround(left, right, run, cameraX, skateboard, slippery); break;
            case 3: StepRise(left, right, run, cameraX, skateboard, slippery); break;
            case 4: StepFall(left, right, run, cameraX, cameraY, skateboard, slippery); break;
        }
    }

    // Comun a 2/3/4: $1FC0 (animacion, no portada), $252B, $297B, $26FA.
    void MoveX(bool left, bool right, bool run, bool onGround, ushort cameraX,
               bool skateboard, bool slippery)
    {
        UpdateVelocity(left, right, run, onGround, skateboard, slippery);
        WallCheck();
        ApplyVelocity(cameraX);
    }

    // $2889 estado 2: salto pendiente -> 3; si |Y - suelo| >= 10 -> 4; si no, Y = suelo.
    void StepGround(bool left, bool right, bool run, ushort cameraX, bool skate, bool slip)
    {
        State |= 0x80;
        MoveX(left, right, run, true, cameraX, skate, slip);
        if (JumpLatch) { JumpLatch = false; State = 0x03; return; }
        ushort g = Ground();
        int d = Math.Abs((short)(ushort)(Y - g)); // $28DF
        if (d >= 0x0A) { State = 0x04; return; }
        Y = g;
    }

    // $2991 estado 3: al entrar v0 = $55 si hay direccion o correr ($C086 & $C2;
    // con monopatin $C086 & $42), si no $40. Luego Y += v, v -= 4; al pasar de 0 -> $84.
    void StepRise(bool left, bool right, bool run, ushort cameraX, bool skate, bool slip)
    {
        if ((State & 0x80) == 0)
        {
            State |= 0x80;
            bool fast = skate ? (run || right) : (run || left || right);
            VelY = (sbyte)(fast ? 0x55 : 0x40);
            YFrac = 0;
        }
        MoveX(left, right, run, false, cameraX, skate, slip);
        IntegrateY();
        int v = (byte)VelY - 4;
        if (v >= 0) { VelY = (sbyte)v; return; }
        VelY = 0;
        State = 0x84; // cae sin reiniciar v ni fraccion
    }

    // $2A15 estado 4: v -= 4 con tope (no baja de $A1..$A4); aterriza si Y queda entre
    // 1 y 16 px por debajo del suelo ($2A6B..$2A98).
    void StepFall(bool left, bool right, bool run, ushort cameraX, ushort cameraY,
                  bool skate, bool slip)
    {
        if ((State & 0x80) == 0) { State |= 0x80; YFrac = 0; VelY = 0; }
        MoveX(left, right, run, false, cameraX, skate, slip);
        IntegrateY();
        byte a = (byte)((byte)VelY - 4);
        if ((a & 0x80) == 0 || a >= 0xA1) VelY = (sbyte)a;

        // $2A51: por debajo de la camara ($C444 - $100) -> $287B ($2817 o estado 0).
        if ((ushort)(cameraY - 0x100) >= Y) { State = 0x00; return; }

        ushort g = Ground();
        int diff = (ushort)(Y - g);
        if (Y >= g) return;                 // aun por encima del suelo
        if ((diff >> 8) != 0xFF || (diff & 0xFF) < 0xF0) return; // mas de 16 px por debajo
        Y = g;
        byte c = (byte)(((sbyte)VelY >> 4) - 2);
        if ((byte)diff >= c) { State = 0x02; return; }
        State = 0x05; // $2A9D: rebote (sin plataforma); estado 5 no portado
    }

    // $43B2: Y(16.8) += vel(4.4 con signo) * 16, fraccion en ix+$16.
    public void IntegrateY()
    {
        int whole = VelY >> 4;
        int frac = (((byte)VelY << 4) & 0xFF) + YFrac;
        YFrac = (byte)frac;
        Y = (ushort)(Y + whole + (frac >> 8));
    }

    // $28FB: suelo bajo el jugador = max(altura(X-10), altura(X+10)) + alto/2.
    public ushort Ground()
    {
        if (Terrain == null) return Y;
        ushort h1 = Terrain.Height((ushort)(X - 0x0A));
        ushort h2 = Terrain.Height((ushort)(X + 0x0A));
        return (ushort)(Math.Max(h1, h2) + (Height >> 1));
    }

    // $297B/$2957: si el suelo a 20 px en la direccion de la velocidad queda por encima
    // de Y+8, la velocidad horizontal se anula (pared).
    void WallCheck()
    {
        if (Terrain == null) return;
        ushort ahead = (ushort)(X + (VelX < 0 ? -0x14 : 0x14));
        ushort g = (ushort)(Terrain.Height(ahead) + (Height >> 1));
        if ((ushort)(Y + 8) < g) VelX = 0;
    }
}
