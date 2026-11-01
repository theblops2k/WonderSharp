namespace WonderBoy.Game;

// Modo partida (--play): entradas de teclado sobre el modelo de attract + fin de partida.
// Solo activo con PlayMode (StartPlay); --attract, --title-rom y --check-* no lo usan.
//   arranque  $038A-$038F: $C003=1, $C005=1 (attract; la IRQ no copia el mando a $C086).
//   IRQ       $0625: $C00C = ~IN0 & $F6 (bit7 LEFT, bit6 RIGHT, bit2 BUTTON2, bit1 BUTTON1),
//                    $C00E = ~IN2 & $3F (bit0 COIN1, bit4 START1, bit5 START2).
//             $0071-$0086: con $C005=0, $C086 = $C00C ($C00D si $C008 bit7) y $00A4.
//             $047A ($C00A=0): monedero. $C017 enclavamientos (bit3 servicio, bit0/bit6 ranura 1,
//                    bit1/bit7 ranura 2); al soltar la moneda $0501 suma creditos a $C016 (max 9)
//                    segun $C018/$C01B (tabla $05C3) y $058F pulsa el contador ($C01E/$C020, $C008).
//   bucle     $0195-$01AB: en attract ($C005!=0) con $C016!=0: $C005=0, $C07E=0, tabla $4F20,
//             $01E4 ($C032=1, $C07E=1, $C081=$4F20[1]) y despacho $01F7.
// Portado: $1283 creditos y $132C espera START (TitleRom.Game.cs), $1388/$13D9 inicio de partida y
// carga de nivel (TitleRom.Level.cs), $1651 presentacion de ronda (TitleRom.Round.cs).
// No portado: $18B7 bucle de juego, $1B52 perdida de vida: el despacho se marca en Unported
// y el bucle principal se detiene (la IRQ sigue). Sonido $0440. $0501 con indice >= $14 (tabla $05D7)
// portado sin verificar.
public sealed partial class TitleRom
{
    bool playMode;
    bool fromSnapshot; // FromRam(play): partida reanudada de un volcado (ram_fin_juego), no desde StartPlay
    public bool PlayMode => playMode;

    // $C00C (mando 1, ya invertido y enmascarado como lo deja $0625).
    public byte Joystick { set => m[0xC00C] = (byte)(value & 0xF6); }

    public static TitleRom StartPlay(byte[] mainRom, int titleStart = 29, int irqStart = DefaultIrqStart)
    {
        var t = Start(mainRom, titleStart, irqStart);
        t.playMode = true;
        t.m[0xC003] = 1; t.m[0xC005] = 1;            // $038A-$038F
        t.m[0xC07E] = (byte)t.state;                  // $01E4 de f28 (Start no lo escribe)
        t.SetW(0xC081, t.Table(0x4F12, t.state));
        return t;
    }

    // Final de la IRQ en modo partida ($0071-$0098).
    void PlayIrq()
    {
        if (m[0xC005] == 0)
        {
            m[0xC086] = m[(m[0xC008] & 0x80) != 0 ? 0xC00D : 0xC00C];
            PlayerRoutine.ReadInputs(m);              // $00A4
        }
        if (m[0xC00A] == 0) CoinIrq();                // $047A
    }

    void CoinIrq() // $047A
    {
        int b = ~m[0xC00E] & 0xFF;                    // IN2 crudo (activo a 0)
        int c = m[0xC017];
        if ((b & 0x08) == 0)                          // servicio: credito directo
        {
            if ((c & 0x08) == 0) { c |= 0x08; if (m[0xC016] < 9) m[0xC016]++; }
        }
        else c &= ~0x08;                              // $0497

        int hl = 0xC018, de = 0xC01E;                 // ranura 1
        if ((b & 0x01) == 0)
        {
            if ((c & 0x01) == 0) c |= 0x01;           // $04AF
            else c |= 0x40;                           // $04AB (bit6 ya puesto: igual)
        }
        else
        {
            if ((c & 0x40) != 0) { CoinCredit(hl, de); c &= ~0x40; }
            c &= ~0x01;                               // $04BC
        }
        if (m[0xC01B] != m[hl]) { hl = 0xC01B; de = 0xC020; } // $04BE
        if ((b & 0x02) == 0)                          // ranura 2
        {
            if ((c & 0x02) == 0) c |= 0x02;
            else c |= 0x80;
        }
        else
        {
            if ((c & 0x80) != 0) { CoinCredit(hl, de); c &= ~0x80; }
            c &= ~0x02;                               // $04EB
        }
        m[0xC017] = (byte)c;
        CoinMeter(0xC01E, 0x01);                      // $058F
        CoinMeter(0xC020, 0x02);
    }

    // $0501: hl = configuracion de la ranura, de = contador de pulsos ($C01E/$C020).
    void CoinCredit(int hl, int de)
    {
        int a = m[hl];
        if (a == 0x0F) a = 0;
        a = ((a << 1) | (a >> 7)) & 0xFF;             // rlca
        if (a >= 0x14)
        {   // $0555: tabla $05D7[(a-$14)/2] -> T (T[0] = monedas del ciclo, T[n] = creditos de la
            // moneda n); contador en hl+2. Sin verificar: ram_rand no usa estos ajustes de monedero.
            int cnt2 = hl + 2, t = W(0x05D7 + a - 0x14);
            int c = (m[cnt2] + 1) & 0xFF;
            m[cnt2] = (byte)(c >= m[t] ? 0 : c);      // $0568 cp (hl) / $056B sub a
            if (m[(t + c) & 0xFFFF] != 0) AddCredits(m[(t + c) & 0xFFFF]); // $0570 call nz,$057C
            m[de]++;                                  // $0578
            return;
        }
        int cnt = hl + 1;
        switch (W(0x05C3 + a))
        {
            case int t when t >= 0x051E && t <= 0x0523: AddCredits(0x0524 - t); break; // 1..6 creditos
            case 0x0529: CoinsFor(cnt, 1, 1); break;   // 2 monedas 1 credito
            case 0x0530: CoinsFor(cnt, 2, 1); break;   // 3 monedas 1 credito
            case 0x0537: CoinsFor(cnt, 3, 1); break;   // 4 monedas 1 credito
            case 0x053E: CoinsFor(cnt, 1, 3); break;   // 2 monedas 3 creditos ($054F)
            default: Unported ??= $"${W(0x05C3 + a):X4}"; break;
        }
        m[de]++;                                      // $0578
    }

    void CoinsFor(int cnt, int need, int credits) // $0529-$054F
    {
        if (m[cnt] >= need) { m[cnt] = 0; AddCredits(credits); }
        else m[cnt]++;                                // $0543
    }

    void AddCredits(int d) // $057C
    {
        int a = (m[0xC016] + d) & 0xFF;               // add a,d (8 bits)
        m[0xC016] = (byte)(a >= 9 ? 9 : a);
        Sound(m, 0x08);                               // $058A jp $0440
    }

    void CoinMeter(int hl, int c) // $058F
    {
        int a = m[hl];
        if (a == 0) return;
        if ((a & 0x80) != 0)                          // $05B9
        {
            if (--m[hl + 1] != 0) return;
            m[hl] = (byte)((m[hl] & 0x7F) - 1);
            return;
        }
        if (m[hl + 1] == 0) { m[0xC008] |= (byte)c; m[hl + 1] = 3; return; }
        if (--m[hl + 1] != 0) return;
        m[0xC008] &= (byte)~c;
        m[hl + 1] = 3;
        m[hl] |= 0x80;                                // $05B5
    }

    // $0195-$01AB en attract: con creditos pasa a la tabla de juego $4F20. true = fotograma consumido.
    bool PlayCredit()
    {
        if (m[0xC005] == 0 || m[0xC016] == 0) return false;
        m[0xC005] = 0; m[0xC07E] = 0;
        m[0xC032] = 1; m[0xC07E]++;                   // $01E4
        SetW(0xC081, Table(0x4F20, m[0xC07E]));
        gameMode = true; levelTiles = roundCurtain = false;
        state = m[0xC07E]; sub = m[0xC07F];
        return true;                                  // $01F7: GameFrame despacha (Unported si no esta portado)
    }
}
