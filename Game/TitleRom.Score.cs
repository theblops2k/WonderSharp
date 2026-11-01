namespace WonderBoy.Game;

// Puntuacion: $0D28 (lo llama el planificador $01D9 en modo juego antes de mirar $C032).
//   $0D35  con $C033 != 0: $C033 = 0, $C034 = 1, vacia la cola $C037.. (hasta $FF, la deja a $FF y
//          $C035 = $C037); cada entrada & $1F indexa la tabla de puntos $4F57 (palabra BCD) y se suma
//          ($0D1A) a $C303-$C305 (BCD de 6 cifras, $C305 el byte bajo).
//   $0D71  con $C034 != 0: $C034 = 0, cifras en $C077 ($0D9F, ceros a la izquierda = $01 en 5 cifras)
//          y celdas en ($C3C7) ($0DCF, 7 cifras paso 2); record $C3C0 si $C303 >= $C3C0 ($0DDC):
//          celdas en $E858 y copia; vida extra $0DFB.
//   $0DFB  si $C309 = 0 o $C029 != 0, y el umbral $C306 <= $C303: $C309++ (< 4: $0E2E vida extra,
//          $C302++, sonido 9, $14D0) y umbral $C306/$C307 += $C029 (BCD).
// Sin verificar (VRAM fuera de ram_rand): las celdas de ($C3C7) y $E858.
public sealed partial class TitleRom
{
    void ScoreUpdate() // $0D28
    {
        if (m[0xC033] != 0)
        {
            m[0xC033] = 0;                                 // $0D35
            m[0xC034] = 1;
            SetW(0xC035, 0xC037);
            int sum = 0;                                   // c:d:e (BCD)
            for (int hl = 0xC037; m[hl] != 0xFF; hl++)
            {
                int a = m[hl] & 0x1F;
                m[hl] = 0xFF;
                sum = BcdAdd24(sum, Table(0x4F57, a));     // $0D1A
            }
            int score = m[0xC303] << 16 | m[0xC304] << 8 | m[0xC305];
            score = BcdAdd24(score, sum);                  // $0D60
            m[0xC303] = (byte)(score >> 16); m[0xC304] = (byte)(score >> 8); m[0xC305] = (byte)score;
        }
        else if (m[0xC034] == 0) return;
        m[0xC034] = 0;                                     // $0D71
        ScoreDigits(0xC303);                               // $0D9F
        ScoreCells(W(0xC3C7));                             // $0DCF
        if (BcdLessEq(0xC3C0, 0xC303))                     // $0DDC
        {
            ScoreCells(0xE858);
            Array.Copy(m, 0xC303, m, 0xC3C0, 3);           // $0D96 ldir
        }
        ExtraLife();                                       // $0DFB
    }

    // Suma BCD de 6 cifras (3 bytes con add/adc + daa); el acarreo final se pierde.
    static int BcdAdd24(int x, int y)
    {
        int r = 0, carry = 0;
        for (int sh = 0; sh < 24; sh += 8)
        {
            int a = (x >> sh) & 0xFF, b = (y >> sh) & 0xFF;
            int lo = (a & 0x0F) + (b & 0x0F) + carry;
            int hi = (a >> 4) + (b >> 4);
            if (lo > 9) { lo -= 10; hi++; }
            carry = 0;
            if (hi > 9) { hi -= 10; carry = 1; }
            r |= ((hi << 4) | lo) << sh;
        }
        return r;
    }

    // $0DDC: carry = (hl) <= (de), 3 bytes big-endian.
    bool BcdLessEq(int hl, int de)
    {
        for (int i = 0; i < 3; i++)
            if (m[hl + i] != m[de + i]) return m[hl + i] < m[de + i];
        return true;
    }

    // $0D9F: 3 bytes BCD en hl -> 6 cifras + $10 en $C077; las 5 primeras $10 seguidas pasan a $01.
    void ScoreDigits(int hl)
    {
        for (int i = 0; i < 3; i++)
        {
            int a = m[hl + i];
            m[0xC077 + 2 * i] = (byte)((a >> 4) + 0x10);
            m[0xC078 + 2 * i] = (byte)((a & 0x0F) + 0x10);
        }
        for (int i = 0; i < 5 && m[0xC077 + i] == 0x10; i++) m[0xC077 + i] = 0x01;
    }

    // $0DCF: 7 bytes de $C077 a de, de+2, ...
    void ScoreCells(int de)
    {
        for (int i = 0; i < 7; i++) m[(de + 2 * i) & 0xFFFF] = m[0xC077 + i];
    }

    // $0DFB: vida extra por umbral de puntos.
    void ExtraLife()
    {
        if (m[0xC309] != 0 && m[0xC029] == 0) return;
        if (!BcdLessEq(0xC306, 0xC303)) return;            // $0E0C ret nc
        int a = m[0xC309] + 1;
        if (a < 4)
        {
            m[0xC309] = (byte)a;
            LifeUp(m);                                     // $0E1B call $0E2E
        }
        int t = BcdAdd24(m[0xC306] << 8 | m[0xC307], m[0xC029]); // $0E1E
        m[0xC306] = (byte)(t >> 8); m[0xC307] = (byte)t;
    }

    // $0E2E: vida extra. $C302++ (si llega a $64 vuelve a 7), sonido 9 y iconos $14D0.
    // Tambien la usa el enemigo $46B4 ($4768 jp $0E2E, Enemies.cs).
    internal static void LifeUp(byte[] m)
    {
        int a = m[0xC302] + 1;
        if (a >= 0x64) a = 0x07;
        m[0xC302] = (byte)a;
        Sound(m, 0x09);                                    // $0E3F $0440
        Lives(m);                                          // $0E42 $14D0
    }
}
