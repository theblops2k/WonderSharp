namespace WonderBoy.Game;

// Fin de partida y vuelta a attract: estados de juego $C07E 9 -> 10 -> 11 -> attract 1 -> 2
// (tabla $4F20: [9]=$1BCC, [11]=$1E36; tabla $4F12: [1]=$11A9, [2]=$0F52). attract_flow.md 4.1.
// Se semilla con la RAM ($C000-$CFFF) de un volcado y avanza con Step(); el planificador $018E
// y el despacho $01F7 se leen de $C005/$C030/$C032/$C07E/$C081 en la propia RAM.
//   $1BCC sub 0 $1BD6: $0C2E(1 o 4), $E001.. res 3 (1024 celdas), $09DB, sonido $C0, $C013=4.
//         sub 1 $1C05: START ($C00E & $30) o fin de 4 s + $0E5C sin record -> $C07E++ (10); con
//               record ($0E5C carry) $C07E queda en 9 y el siguiente es 10 = $1C24 (nombre).
//   $1E36 sub 0 $1E46: con $C02D=0 o ronda $C301>=$18 -> sub 2. Si no, pantalla de continuar:
//               $0A24($1F), paleta 9, texto $11, digito 9 en $ED5E ($14FA), $0417, $C3C9=60, $C3DF=9.
//         sub 1 $1E95: cuenta atras 9..0 (60 fotogramas por digito); START1 con creditos ->
//               continuar (sub 3); START2 ($C00E bit5) o 0 -> texto $12, $C013=1, sub 2.
//         sub 2 $1F42: espera 1 s, $C3C5++; si quedan jugadores $C07E=3, si no $C030=1.
//         sub 3 $1F87: espera 1 s y $C07E=3 (vuelve a la partida).
//   $018E con $C030=1: $C005=1, $C07E=0 y carga $4F12[1] = $11A9; al siguiente, $4F12[2] = titulo,
//   que pasa al modelo de attract de TitleRom (MainFrame/TitleState).
// $1C24 (estado 10, entrada de nombre) y $0EBC (orden de la tabla de records): TitleRom.Record.cs.
// Sonido $0440 ($C0 en $1BF6, $00 en $1C18): TitleRom.Sound.
public sealed partial class TitleRom
{
    bool gameMode;
    bool resumeNoDec; // reanudacion de $0F66 tras $0417 sin $C010-- (solo vuelta a attract)
    // Continuaciones de un subestado largo: una por fotograma; tras la ultima se ejecuta la vuelta
    // normal del bucle ($03CE ya tiene $C00B=1). Una accion vacia = fotograma sin bucle principal.
    readonly Queue<Action> pending = new();

    public bool GameMode => gameMode;
    public string? Unported { get; private set; }

    // Solo --walk-all (WalkAll.cs): tras quitar la ranura de enemigo sin portar, deja seguir el juego.
    internal void ResumeAfterUnported() => Unported = null;

    // Entradas ($C00E: bit4 START1, bit5 START2), las escribe la IRQ en el original.
    public byte Inputs { set => m[0xC00E] = value; }

    // Fotogramas sin bucle principal en $1BCC sub 0 y $1E46 (medidos en ram_rand f4515 y f4757).
    public const int GameOverSub0Lost = 1, ContinueClearLost = 1;
    // Fotogramas solo IRQ tras el primero de $0A24($1F) al volver al titulo (pc_trace f5361-f5362).
    public const int TitleClearLost = 2;
    // Fotogramas extra del borrado $0A24($1F) en $1C36 (ram_fin_juego f1760-f1762).
    public const int NameEntryClearLost = 1;

    // RAM $C000-$CFFF del volcado (tras el fotograma N); el siguiente Step() es el fotograma N+1.
    // play: IRQ de partida (PlayIrq: $C00C -> $C086, $00A4) como en StartPlay.
    public static TitleRom FromRam(byte[] mainRom, byte[] ram, bool play = false)
    {
        var t = new TitleRom(mainRom);
        Array.Copy(ram, 0, t.m, 0xC000, Math.Min(ram.Length, 0x1000));
        t.gameMode = true;
        t.playMode = play;
        t.fromSnapshot = play;
        t.state = t.m[0xC07E];
        t.sub = t.m[0xC07F];
        return t;
    }

    void GameFrame()
    {
        if (pending.Count > 0)
        {
            pending.Dequeue()();
            if (pending.Count > 0 || !gameMode) return; // !gameMode: espera $0417 del titulo
        }
        if (Unported != null) return;
        m[0xC010]--;
        Effect();                                      // $01FB
        if (!Scheduler()) return;
        int hl = W(0xC081);                            // $01F7
        switch (hl)
        {
            case 0x1BCC: GameOverState(); break;
            case 0x1C24: NameEntryState(); break;     // entrada de nombre (TitleRom.Record.cs)
            case 0x1E36: ContinueState(); break;
            case 0x11A9:                               // $11A9
                if (!WaitSeconds()) return;
                m[0xC088] = 0; m[0xC032] = 0;
                break;
            // Titulo ($0F66, mismo codigo que en el arranque). El borrado $0A24($1F) dura aqui 3 fotogramas
            // (traza pc_trace: PC en $0A15-$0A20 en f5360-f5362, $041C en f5363) y no 1 como ClearLost:
            // $0C2E encola su paleta ($C0AB=1) en f5363 y $0736($C8-$CC) va en f5364. Temporizacion por
            // ciclos medida, no codigo (attract_flow.md 4.1); el arranque sigue con ClearLost.
            case 0x0F52:
                m[0xC07F] = 0;
                Sound(m, 0x00);                          // $0F66 $0440
                Clear(0x1F); lostFrames = 0;
                // Con IRQ de partida (playMode) el borrado dura 1 fotograma menos: ram_fin_juego f5628 $0F52,
                // $C010 parado en f5629-f5630, $C0AB=1 en f5630, $C07F=2 en f5631 (ram_rand: f5361-f5363).
                // Medido en las grabaciones, causa (ciclos por fotograma) sin derivar. Solo con partida
                // reanudada de volcado (FromRam play): desde StartPlay (ram_rand) son 2 (f5361-f5362).
                int titleLost = fromSnapshot ? TitleClearLost - 1 : TitleClearLost;
                for (int i = 0; i < titleLost; i++) pending.Enqueue(() => { });
                pending.Enqueue(() =>
                {   // fin de $0A24 hasta la espera $0417; f5364 sigue en TitleState (subWait)
                    SetW(0xC000, 0); m[0xC002] = 0;
                    DrawPacked(0x543B, 0xEA8E, 0x09);  // $0C15
                    Text(0x50D9, 0x0A);                 // $0B7A
                    SpriteSet(0);                        // $0C2E
                    gameMode = false; levelTiles = roundCurtain = false;
                    state = StateTitle; sub = 0; subWait = true;
                    resumeNoDec = true;                 // f5364 reanuda sin $018E
                });
                break;
            case 0x1283: CreditsState(); break;        // creditos (TitleRom.Game.cs)
            case 0x132C: StartWaitState(); break;      // espera START (TitleRom.Game.cs)
            case 0x1388: LevelStartState(); break;     // inicio de nivel (TitleRom.Level.cs)
            case 0x13D9: LevelSubState(); break;       // carga de nivel por subestados (TitleRom.Level.cs)
            case 0x152D: RoundWipeState(); break;      // barrido de fin de ronda (TitleRom.Wipe.cs)
            case 0x1651: RoundState(); break;        // presentacion de ronda (TitleRom.Round.cs)
            case 0x18B7: GameLoopState(); break;       // bucle de juego (TitleRom.Loop.cs)
            case 0x1B52: LifeLostState(); break;       // perdida de vida (TitleRom.Loop.cs)
            default: Unported = $"${hl:X4}"; break;
        }
    }

    // $018E-$01F6. false = no se despacha (rutina sin portar).
    bool Scheduler()
    {
        int table;
        if (m[0xC005] != 0)
        {
            if (m[0xC016] != 0) { m[0xC005] = 0; m[0xC07E] = 0; table = 0x4F20; }
            else
            {
                m[0xC003] = 1; m[0xC005] = 1;
                if (m[0xC032] != 0) return true;
                table = 0x4F12;
            }
        }
        else if (m[0xC030] != 0)
        {
            m[0xC030] = 0; m[0xC07E] = 0; m[0xC005] = 1; table = 0x4F12;
        }
        else
        {
            m[0xC005] = 0;
            ScoreUpdate();                             // $01D9 $0D28 (TitleRom.Score.cs)
            if (m[0xC032] != 0) return true;
            table = 0x4F20;
        }
        m[0xC032] = 1;                                 // $01E4
        m[0xC07E]++;
        SetW(0xC081, Table(table, m[0xC07E]));
        return true;
    }

    // $1BCC (tabla de juego [9]).
    void GameOverState()
    {
        switch (m[0xC07F])
        {
            case 0: // $1BD6
                SpriteSet((m[0xC008] & 0x80) != 0 ? 4 : 1);   // $0C2E
                for (int i = 0; i < 0x400; i++) m[0xE001 + 2 * i] &= 0xF7;
                for (int i = 0; i < GameOverSub0Lost; i++) pending.Enqueue(() => { });
                pending.Enqueue(() =>
                {
                    FillRows(0xE986, 0); FillRows(0xE987, 0);  // $09DB
                    Sound(m, 0xC0);                            // $1BF6 $0440
                    m[0xC013] = 4;
                    m[0xC07F]++;
                });
                break;
            case 1: // $1C05
                if ((m[0xC00E] & 0x30) == 0)
                {
                    if (!WaitSeconds()) return;
                    if (HighScore())                   // $1C12 jr c: $C07E sigue en 9 -> 10 = $1C24
                    {
                        // $0EBC no cabe en el fotograma (ram_fin_juego f1759: $C07F 1, $C3DF $0B):
                        // $1C18 y la vuelta del bucle ocurren en el siguiente.
                        pending.Enqueue(() =>
                        {
                            sortTBudget = long.MaxValue;
                            RecordSort();                  // fin de $0EBC
                            Sound(m, 0x00);                // $1C18 $0440
                            m[0xC07F] = 0; m[0xC032] = 0;
                        });
                        break;
                    }
                }
                m[0xC07E]++;
                Sound(m, 0x00);                            // $1C18 $0440
                m[0xC07F] = 0; m[0xC032] = 0;
                break;
        }
    }

    // $09E3/$09F9: 22 filas x 26 celdas, paso $40.
    void FillRows(int addr, byte a)
    {
        for (int r = 0; r < 0x16; r++, addr += 0x40)
            for (int c = 0; c < 0x1A; c++) m[addr + 2 * c] = a;
    }

    // $0E5C: puntuacion $C303-$C305 (BCD) a texto en $C242 y busqueda en la tabla $C102 (16 B por
    // entrada) con $0DEE; $C3DF = posicion 1..; $C242 es la entrada 21, asi que siempre termina.
    // true (carry) = entra en la tabla (posicion < 21).
    // Ciclos Z80 (T) que quedan en el fotograma al entrar en $0E5C; $0E5C-$0EBC se cuentan en sortT.
    // ram_fin_juego f1759 (posicion 7, corte tras la comparacion 145) lo situa en [52706, 53014);
    // se usa el centro. De 66667 T por fotograma (4 MHz / 60 Hz) quedan ~13800 T para lo anterior.
    const long HighScoreFrameT = 52860;
    bool HighScore()
    {
        sortT = 30;                                    // $0E5C-$0E62
        int de = 0xC242; byte c = 0x20;
        for (int i = 0; i < 3; i++)
        {
            byte a = m[0xC303 + i];
            sortT += 84 + ((a & 0xF0) != 0 ? 14 : 12) + ((a & 0x0F) != 0 ? 14 : 12) + (i < 2 ? 13 : 8);
            if ((a & 0xF0) != 0) c = 0x30;
            m[de++] = (byte)((a >> 4) + c);
            if ((a & 0x0F) != 0) c = 0x30;
            m[de++] = (byte)((a & 0x0F) + c);
        }
        for (int i = 0; i < 8; i++) m[de++] = 0x20;
        sortT += 217 + 30;                             // $0E80-$0E8D
        m[0xC3DF] = 1;
        int hl = 0xC102;
        for (;;)                                       // $0E90-$0EA5
        {
            sortT += 10 + 11 + Compare6T(hl, 0xC242) + 10;
            if (Compare6(hl, 0xC242)) { sortT += 12; break; }
            sortT += 7 + 63;
            hl += 0x10; m[0xC3DF]++;
        }
        if (m[0xC3DF] >= 0x15) return false;
        SetW(0xC3D7, hl + 6);
        m[0xC3CF] = m[0xC3DF];
        sortTBudget = HighScoreFrameT;
        RecordSortStart();                             // $0EB7 call $0EBC (TitleRom.Record.cs)
        RecordSort();                                  // se corta al acabarse el fotograma; resto en $1C05
        return true;
    }

    // $0DEE: carry si la entrada (hl) es menor o igual que (de).
    bool Compare6(int hl, int de)
    {
        for (int i = 0; i < 6; i++)
            if (m[hl + i] != m[de + i]) return m[hl + i] < m[de + i];
        return true;
    }

    // $1E36 (tabla de juego [11]).
    void ContinueState()
    {
        switch (m[0xC07F] & 3)
        {
            case 0: // $1E46
                if (m[0xC02D] == 0 || m[0xC301] >= 0x18) { m[0xC07F] = 2; return; }
                SetW(0xC000, 0); m[0xC002] = 0;
                for (int i = 0; i < ContinueClearLost; i++) pending.Enqueue(() => { });
                pending.Enqueue(() =>
                {   // fin de $0A24($1F) hasta la espera $0417
                    Clear(0x1F); lostFrames = 0;
                    QueuePalette(0x09);
                    Text(0x50D9, 0x11);
                    Digit(9, 0xED5E);
                });
                pending.Enqueue(() =>
                {
                    m[0xDC00] = m[0xDC10] = 0xAD;
                    m[0xC3C9] = 0x3C; m[0xC3DF] = 9;
                    m[0xC07F]++;
                });
                break;
            case 1: // $1E95
                if (--m[0xC3C9] == 0)
                {
                    m[0xC3C9] = 0x3C;
                    m[0xC3DF]--;
                    Digit(m[0xC3DF], 0xED5E);
                    if (m[0xC3DF] == 0) { ContinueEnd(); return; }
                }
                if ((m[0xC00E] & 0x20) != 0) { ContinueEnd(); return; }
                m[0xEEF8] = (byte)(m[0xC016] | 0x30);
                if (m[0xC016] == 0 || (m[0xC00E] & 0x10) == 0) return;
                m[0xC013] = 1;                                     // $1ED0: continuar
                m[0xC016]--;
                Text(0x50D9, 0x13);
                int ix = (m[0xC3C3] ^ m[0xC083]) == 0 ? 0xC340 : 0xC380;
                m[ix + 2] = m[0xC027];
                m[ix + 3] = m[ix + 4] = m[ix + 5] = m[ix + 6] = 0;
                m[ix + 7] = m[0xC028];
                m[ix + 8] = m[ix + 9] = 0;
                Array.Copy(m, 0x1F3B, m, 0xC077, 7);
                for (int i = 0, d = W(0xC3C7); i < 7; i++, d += 2) m[d & 0xFFFF] = m[0xC077 + i]; // $0DCF
                m[0xC07F] = 3;
                break;
            case 2: // $1F42
                if ((m[0xC010] & 3) == 0) QueuePalette(0x0B);
                if (!WaitSeconds()) return;
                m[0xDC00] = m[0xDC10] = 0;
                m[0xC3C5]++;
                if (m[0xC3C5] < m[0xC3C4])
                {   // turno del otro jugador
                    m[0xC083] = 0; m[0xC07E] = 3; m[0xC07F] = 0; m[0xC032] = 0;
                    return;
                }
                m[0xC07F] = 0; m[0xC013] = 0; m[0xC3C3] = 0;   // $1F77
                m[0xC030] = 1;
                break;
            case 3: // $1F87
                if ((m[0xC010] & 3) == 0) QueuePalette(0x0B);
                if (!WaitSeconds()) return;
                m[0xC07E] = 3; m[0xC07F] = 0; m[0xC032] = 0;
                break;
        }
    }

    void ContinueEnd() // $1F2C
    {
        m[0xC013] = 1;
        Text(0x50D9, 0x12);
        m[0xC07F]++;
    }

    // $14FA: digito a (0-9) de 2x2 celdas desde $5681 + 4a, atributo $0E.
    void Digit(int a, int de)
    {
        if (a >= 10) return;
        int hl = 0x5681 + 4 * a;
        m[de] = m[hl]; m[de + 2] = m[hl + 1]; m[de + 0x40] = m[hl + 2]; m[de + 0x42] = m[hl + 3];
        m[de + 1] = m[de + 3] = m[de + 0x41] = m[de + 0x43] = 0x0E;
    }
}
