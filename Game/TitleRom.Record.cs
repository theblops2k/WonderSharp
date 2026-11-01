namespace WonderBoy.Game;

// Tabla de records tras fin de partida: $0EBC (orden, llamado desde $0E5C) y estado de juego 10
// $1C24 (tabla $4F20[10]) con subestados por $0ABB ($C07F): $1C36 $1C82 $1CEC $1D0A $1D5E $1E26.
//   $1C36 borrado $0A24($1F), paleta 5, texto $10, $C3D0 = filas a mostrar (7, $C3CF+3 o $14).
//   $1C82 cada 8 fotogramas ($C002 += 2) una fila $0F18 desde $E58E; la fila del record nuevo
//         guarda su VRAM en $C3D1 y arranca $C3E3=$EDCE (destino en la linea de edicion).
//   $1CEC 8 fotogramas de desplazamiento, $C3D9=$C3E3, $C013=1.
//   $1D0A copia la fila a la linea de edicion, atributos 2/8, cursor $7F, $C013=50 s.
//   $1D5E entrada: $C084 bit2/bit3 cambia la letra ($50B7, 32 car., repeticion $C3D6), bit4 la
//         fija en $C3D7 (3 letras); bit5 cambia mayusculas/minusculas ($1E0A). START o fin de
//         tiempo cierra. $1E26 espera 2 s y vuelve al planificador ($C032=0).
// Sin verificar: ram_rand no llega a la entrada de nombre (ni a $0EBC con record). Temporizacion de
// $0A24($1F) como Clear (ClearLost), no medida aqui. Sonidos $0440 ($C6, $00) en TitleRom.Sound.
public sealed partial class TitleRom
{
    // $0EBC: ordena las 21 entradas de $C102 (16 B; se intercambian 14 B: puntos y nombre).
    // La ROM siempre hace 20+19+..+1 = 210 comparaciones; no decide cuantas por fotograma: la corta la
    // interrupcion de video cuando se acaba el tiempo de CPU. Por eso se cuentan ciclos Z80 (T) igual
    // que las instrucciones de la ROM; el coste depende de los datos ($0DEE sale en el primer byte
    // distinto, $0F03 solo si hay intercambio). sortT = T gastados desde $0E5C; sortTBudget = T que
    // quedan en el fotograma; devuelve true al terminar. sortB = registro B del bucle ($0ED4), 0 = pasada nueva.
    int sortB;
    long sortT, sortTBudget = long.MaxValue;
    void RecordSortStart()
    {
        sortT += 13 + 7 + 5 + 10 + 11 + 16 + 13 + 17;  // $0EA7-$0EB7 (ret nc no salta, call)
        sortT += 7 + 13 + 10 + 16;                      // $0EBC-$0EC4
        m[0xC3DF] = 0x14;
        SetW(0xC3E1, 0xC102);
        sortB = 0;
    }
    // T de call $0DEE + rutina: sale en el byte k (1..6) distinto, o recorre los 6.
    int Compare6T(int hl, int de)
    {
        for (int k = 1; k <= 6; k++)
            if (m[hl + k - 1] != m[de + k - 1]) return 17 + 7 + k * 34 + (k - 1) * 18 + 11;
        return 17 + 7 + 6 * 34 + 6 * 5 + 5 * 13 + 8 + 4 + 10;
    }
    const int SwapT = 17 + 11 + 16 + 20 + 7 + 14 * 44 + 13 * 13 + 8 + 10 + 10;  // call $0F03 .. ret
    bool RecordSort()
    {
        do
        {
            if (sortB == 0)
            {
                if (sortT + 70 > sortTBudget) return false;      // $0EC7-$0ED4
                sortT += 70;
                SetW(0xC3E3, W(0xC3E1) + 0x10);
                sortB = m[0xC3DF];
            }
            for (; sortB > 0; sortB--)
            {
                bool swap = Compare6(W(0xC3E1), W(0xC3E3));     // $0EDD $0DEE carry -> $0F03
                // $0ED5 ld hl,ld de,push | $0DEE | pop, jr nc | $0F03 | $0EE6-$0EED ld ($C3E3),hl
                long t = 16 + 20 + 11 + Compare6T(W(0xC3E1), W(0xC3E3)) + 10
                       + (swap ? 7 + SwapT : 12) + 16 + 10 + 11 + 16;
                if (sortT + t > sortTBudget) return false;
                sortT += t + (sortB > 1 ? 13 : 8);              // djnz
                if (swap)
                {
                    int hl = W(0xC3E3), de = W(0xC3E1);
                    for (int i = 0; i < 0x0E; i++)
                        (m[hl + i], m[de + i]) = (m[de + i], m[hl + i]);
                }
                SetW(0xC3E3, W(0xC3E3) + 0x10);
            }
            sortT += 16 + 10 + 11 + 16 + 10 + 11 + (m[0xC3DF] > 1 ? 12 : 7);  // $0EF2-$0F00
            SetW(0xC3E1, W(0xC3E1) + 0x10);
        } while (--m[0xC3DF] != 0);
        return true;
    }

    // $1C24 (tabla de juego [10]).
    void NameEntryState()
    {
        switch (m[0xC07F])
        {
            case 0: // $1C36
            {
                Clear(0x1F);                                   // $0A24($1F)
                // El borrado ocupa 3 fotogramas (ram_fin_juego f1760-f1762, $C07F 0 -> 1 en f1763).
                for (int i = 0; i < NameEntryClearLost; i++) pending.Enqueue(() => { });
                pending.Enqueue(() =>
                {
                    m[0xDC00] = 0; m[0xC002] = 0; SetW(0xC000, 0);
                    QueuePalette(0x05);                        // $0736
                    Text(0x50D9, 0x10);                        // $0B7A
                    Sound(m, 0xC6);                            // $1C52 $0440
                    SetW(0xC3E1, 0xE58E);
                    int a = m[0xC3CF], b = 0x07;
                    if (a >= 0x05) b = a < 0x11 ? a + 3 : 0x14;
                    m[0xC3D0] = (byte)b;
                    m[0xC3DF] = 1;
                    m[0xC002] = 0xFE;
                    m[0xC07F]++;
                });
                break;
            }
            case 1: // $1C82
            {
                m[0xC002] += 2;
                if ((m[0xC002] & 0x0F) != 0) return;
                RecordEntry(m[0xC3DF], W(0xC3E1));             // $0F18
                if (m[0xC3DF] == m[0xC3CF])
                {
                    SetW(0xC3D1, W(0xC3E1));
                    SetW(0xC3E3, 0xEDCE);
                }
                int hl = (W(0xC3E1) + 0xFC80) & 0xFFFF;        // $1CAA
                hl = (hl & 0x00FF) | (((hl >> 8) & 0x07) | 0xE0) << 8;
                FillAlt(hl, 0x14, 0);                          // $0A16 bc=$0114
                SetW(0xC3E1, (W(0xC3E1) + 0x80) & 0xF7FF);     // res 3,h
                SetW(0xC3E3, ((W(0xC3E3) - 0x80) & 0xFFFF) | 0x0800); // set 3,h
                m[0xC3DF]++;
                if (m[0xC3D0] >= m[0xC3DF]) return;            // $1CE2 ret nc
                m[0xC3DF] = 0;
                m[0xC07F]++;
                break;
            }
            case 2: // $1CEC
                m[0xC002]++;
                if (++m[0xC3DF] < 8) return;
                SetW(0xC3D9, W(0xC3E3));
                m[0xC013] = 1;
                m[0xC07F]++;
                break;
            case 3: // $1D0A
            {
                if (!WaitSeconds()) return;                    // $0AA6
                int src = W(0xC3D1), dst = W(0xC3D9);
                for (int i = 0; i < 0x14; i++) m[(dst + i) & 0xFFFF] = m[(src + i) & 0xFFFF]; // ldir
                FillAlt(src, 0x10, 0x02);                      // $0A16 bc=$0110
                FillAlt(dst + 1, 0x10, 0x08);
                m[0xC087] = 4; m[0xC0BD] = 0;
                int hl = (dst + 0x18) & 0xFFFF;
                SetW(0xC3D1, hl);
                m[0xC3D3] = 0x7F; m[hl] = 0x7F;
                m[0xC3D4] = 0; m[0xC3D5] = 0;
                m[0xC013] = 0x32;
                m[0xC3DB] = 0;
                m[0xC07F]++;
                break;
            }
            case 4: // $1D5E
                if (NameEntryInput()) return;
                break;
            case 5: // $1E26
                if (!WaitSeconds()) return;
                m[0xC088] = 0; m[0xC07F] = 0; m[0xC032] = 0;
                Sound(m, 0x00);                                // $1E33 jp $0440 (a = 0 de $1E29)
                break;
        }
    }

    // $1D5E. true = salida por $0AA6 (sigue en el subestado).
    bool NameEntryInput()
    {
        m[0xC3DB]++;
        int a = m[0xC084] & 0x1C;
        bool close = false;                                    // true = salto directo a $1DF7
        if (a == 0)
        {   // $1DCD: sin mando, el cursor parpadea (bit3 de $C3DB)
            m[0xC3D6] = 0;
            byte b = 0x20;
            if ((m[0xC3DB] & 0x08) != 0) { m[0xC3D3] = CaseToggle(m[0xC3D3]); b = m[0xC3D3]; }
            m[W(0xC3D1)] = b;
        }
        else if ((a & 0x10) != 0)
        {   // $1D6D: fija la letra
            byte c = m[0xC3D3];
            int hl = W(0xC3D1);
            m[hl] = c;
            SetW(0xC3D1, hl + 2);
            hl = W(0xC3D7);
            m[hl] = c;
            SetW(0xC3D7, hl + 1);
            if (++m[0xC3D4] > 2) close = true;                 // $1D89 jr c,$1DF7
            else
            {
                m[W(0xC3D1)] = c;
                m[0xC3DB] = 0;
            }
        }
        else
        {   // $1D98: cambia la letra con repeticion
            int r = ++m[0xC3D6];
            bool step = r == 1;
            if (!step && r >= 0x1F) { m[0xC3D6] = 0x17; step = true; }
            if (step)
            {   // $1DA7
                int d = (m[0xC084] & 0x04) != 0 ? 1 : -1;
                m[0xC3D5] = (byte)((m[0xC3D5] + d) & 0x1F);
                byte ch = CaseToggle(m[0x50B7 + m[0xC3D5]]);   // $0A92 / $1E0A
                m[0xC3D3] = ch;
                m[W(0xC3D1)] = ch;
            }
        }
        if (!close)
        {   // $1DE8
            if ((m[0xC00E] & 0x30) == 0 && !WaitSeconds()) return true;
            m[W(0xC3D1)] = 0x20;
        }
        m[0xC013] = 2;                                         // $1DF7
        Sound(m, 0x00);                                        // $1DFC $0440
        QueuePalette(0x0A);                                    // $0736
        m[0xC07F]++;
        return false;
    }

    // $1E0A: con $C084 bit5 pasa A-Z a minusculas; sin el, a-z a mayusculas.
    byte CaseToggle(byte a)
    {
        bool bit5 = (m[0xC084] & 0x20) != 0;
        if (a < 0x41) return a;
        if (a < 0x5B) return bit5 ? (byte)(a + 0x20) : a;
        if (a < 0x61 || a >= 0x7B) return a;
        return bit5 ? a : (byte)(a - 0x20);
    }
}
