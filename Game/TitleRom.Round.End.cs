namespace WonderBoy.Game;

// Fin de ronda: $1908 (bucle de juego $18B7 con $C440 != 0), despacho por $C07F con la tabla en
// $190E (bytes 18 19 1D 19 32 19 3C 19 44 19): 0 $1918, 1 $191D, 2 $1932, 3 $193C, 4 $1944.
//   $1918  $C07F++.
//   $191D  $2193; sigue si $C313 >= 2 o si $C85E bit5 = 0.
//   $1932  $0A24($10), $C07F++.
//   $193C  $199A (bonificacion, varios fotogramas), $C07F++.
//   $1944  $C30B=$0100, $C30F=$C312=$C311=$C07F=0; con $C313 < 2 siguiente ronda ($C301++ & $1F,
//          $C07E=4); si no, puntos $0F por cada vida extra, 2 fotogramas, $DC00=0, $0A24($1F),
//          $C07F=2, $C302=0 (fin de juego via $1B52). Siempre $C032=0.
// Cada espera $0417 es un fotograma: el codigo hasta la primera corre en el fotograma del despacho y
// el resto en continuaciones de 'pending' (convencion de TitleRom.GameOver.cs).
// SIN VERIFICAR todo el archivo: ram_rand no llega a $C440 != 0 ni a $C479 = 1. Tampoco se ha
// medido si $0A24($0C) de $199A pierde fotogramas (se asume que no, como en Clear).
public sealed partial class TitleRom
{
    // $1908. Sin verificar.
    void RoundEndState()
    {
        switch (m[0xC07F])
        {
            case 0: m[0xC07F]++; break;                    // $1918
            case 1:                                        // $191D (sin verificar)
                GameTick();                                // $2193
                if (Unported != null) return;
                if (m[0xC313] < 2 && (m[0xC85E] & 0x20) != 0) return;
                m[0xC07F]++;
                break;
            case 2:                                        // $1932 (sin verificar)
                Clear(0x10);                               // $0A24($10)
                roundCurtain = levelTiles;                 // telon $199A sobre el BG del nivel (solo dibujo)
                levelTiles = false;                        // fin de ronda (solo dibujo)
                m[0xC07F]++;
                break;
            case 3: RunWaits(BonusState()); break;         // $193C (sin verificar)
            case 4: RunWaits(RoundNext()); break;          // $1944 (sin verificar)
            default: Unported = $"$1908/{m[0xC07F]}"; break;
        }
    }

    // Ejecuta un iterador cuyo 'yield' es una espera $0417: el primer tramo en este fotograma y cada
    // tramo siguiente en una continuacion; tras el ultimo sigue el bucle normal del mismo fotograma.
    void RunWaits(IEnumerator<int> co)
    {
        if (!co.MoveNext()) return;
        void Next() { if (co.MoveNext()) pending.Enqueue(Next); }
        pending.Enqueue(Next);
    }

    // $0422: b esperas $0417.
    static IEnumerable<int> Frames(int b)
    {
        for (int i = 0; i < b; i++) yield return 0;
    }

    // $193C: $199A y $C07F++. Sin verificar.
    IEnumerator<int> BonusState()
    {
        foreach (var w in Bonus()) yield return w;
        m[0xC07F]++;
    }

    // Solo --check-round-end: con la RAM sembrada en $C07F=3, deja el siguiente Step() en la ultima
    // espera $0417 de $193C ($0422($78) de $1ACD). Cuenta las esperas sobre la RAM sembrada, avanza un
    // iterador nuevo hasta la ultima y restaura la RAM: el tramo pendiente corre sobre la RAM original.
    internal void ResumeAtLastBonusWait()
    {
        var seed = (byte[])m.Clone();
        int n = 0;
        for (var a = BonusState(); a.MoveNext();) n++;
        Array.Copy(seed, m, m.Length);
        var co = BonusState();
        for (int i = 0; i < n; i++) co.MoveNext();
        Array.Copy(seed, m, m.Length);
        lostFrames = 0;
        pending.Clear();
        pending.Enqueue(() => { if (co.MoveNext()) Unported = "$193C: espera de mas"; });
    }

    // $199A: pantalla de bonificacion. Sin verificar.
    //   24 filas (una por fotograma) de 28 celdas tile $01 / atributo $08 desde $E904; $0A24($0C);
    //   scroll $C000-$C002 = 0, $DC00=$AF, $0C2E(5, o 6 con $C008 bit7), $09DB, paleta $0C, texto 6,
    //   grafico $543B en $EA8E (c=1); 60 fotogramas; $C3E1=0, $C469 = min($C469, $2000), $C3E3=$EB5A,
    //   barra $4E74; vacia la vitalidad con $1AD3 (3 pasos por fotograma) y la cuenta en $C3E1 pasa a
    //   puntos con $1B10. Con $C313 >= 2 y $C314 != 0 bonificacion extra ($C314 en $EEAC, x100 en
    //   $ED58, $1B10(3)); si no, con $C312 != 0 la cuenta se repite en $ED5A. 120 fotogramas al final.
    IEnumerable<int> Bonus()
    {
        int hl = 0xE904;
        for (int c = 0; c < 0x18; c++)                     // $19A0
        {
            for (int b = 0; b < 0x1C; b++) { m[hl++] = 0x01; m[hl++] = 0x08; }
            yield return 0;                                // $19AC $0417
            hl += 8;
        }
        Clear(0x0C);                                       // $19B3 $0A24($0C)
        SetW(0xC000, 0); m[0xC002] = 0;                    // $19B8
        m[0xDC00] = 0xAF;
        SpriteSet((m[0xC008] & 0x80) != 0 ? 6 : 5);        // $19C7 $0C2E
        FillRows(0xE986, 0); FillRows(0xE987, 0);          // $19D5 $09DB
        QueuePalette(0x0C);                                // $19DA $0736
        Text(0x5429, 0x06);                                // $19DF $0B75
        DrawPacked(0x543B, 0xEA8E, 0x01);                  // $19EA $0C15
        foreach (var w in Frames(0x3C)) yield return w;    // $19EF $0422
        SetW(0xC3E1, 0);                                   // $19F2
        if (W(0xC469) > 0x2000) SetW(0xC469, 0x2000);      // $19F8-$1A07
        SetW(0xC3E3, 0xEB5A);
        VitalityBar();                                     // $1A11 $4E74
        while (true)                                       // $1A14
        {
            bool done = false;
            for (m[0xC3DF] = 3; m[0xC3DF] != 0; m[0xC3DF]--)
                if (VitalityDrain()) { done = true; break; } // $1A19 $1AD3
            if (done) break;
            yield return 0;                                // $1A24 $0417
        }
        foreach (var w in Frames(0x1E)) yield return w;    // $1A29
        int keep = W(0xC3E1);                              // $1A2E
        foreach (var w in CountDown(0)) yield return w;    // $1B10(0)
        SetW(0xC3E1, keep);
        if (m[0xC313] >= 2 && m[0xC314] != 0)              // $1A3D-$1A48
        {
            Text(0x5429, 0x08);                            // $1A4C $0B75
            int a = m[0xC314], t = 0;                      // $1A4F: binario -> BCD
            while (a >= 10) { a -= 10; t++; }
            SetW(0xC3E1, (t << 4) | a);
            PrintBcd(0xEEAC, 0xC3E1, 2, 0x08);             // $1A72 $0AD1
            foreach (var w in Frames(0x1E)) yield return w;
            SetW(0xED60, 0x0030); SetW(0xED62, 0x0030);    // $1A7A
            m[0xC3E2] = m[0xC3E1];                         // x100
            m[0xC3E1] = 0;
            SetW(0xC3E3, 0xED58);
            PrintBcd(0xED58, 0xC3E2, 4, 0x00);             // $1A99 $0AD1
            foreach (var w in Frames(0x1E)) yield return w;
            foreach (var w in CountDown(3)) yield return w; // $1AA3 $1B10(3)
            m[0xED60] = 0;
        }
        else
        {
            SetW(0xED62, 0x0030);                          // $1AAC
            if (m[0xC312] != 0)
            {
                SetW(0xC3E3, 0xED5A);                      // $1AB5
                PrintBcd(0xED5A, 0xC3E2, 4, 0x00);         // $1AC1 $0AD1
                foreach (var w in Frames(0x1E)) yield return w;
                foreach (var w in CountDown(0)) yield return w; // $1ACA $1B10(0)
            }
        }
        foreach (var w in Frames(0x78)) yield return w;    // $1ACD $0422
    }

    // $1AD3: $C469 -= $10 (a 0 si no llega, y devuelve true = carry). Si no, con $C3E2 < $10 suma 2
    // (BCD) a la cuenta $C3E1-$C3E2. Barra $4E74 y cuenta en ($C3E3). Sin verificar.
    bool VitalityDrain()
    {
        int hl = W(0xC469) - 0x10;
        bool carry = hl < 0;
        if (carry) hl = 0;
        SetW(0xC469, hl);
        if (!carry && m[0xC3E2] < 0x10)
        {
            int lo = Bcd(m[0xC3E1]) + 2, cy = lo >= 100 ? 1 : 0;
            m[0xC3E1] = ToBcd(lo % 100);
            m[0xC3E2] = ToBcd(Bcd(m[0xC3E2]) + cy);
        }
        VitalityBar();                                     // $1AFF $4E74
        PrintBcd(W(0xC3E3), 0xC3E2, 4, 0x00);              // $1B0B $0AD1
        return carry;
    }

    // $1B10: pasa la cuenta BCD $C3E1-$C3E2 a puntos: 8 unidades por fotograma, cada una con $0E48(a)
    // y $0D28; tras cada 8, $0417 y sonido $9A. Sin verificar.
    IEnumerable<int> CountDown(int a)
    {
        m[0xC3E0] = (byte)a;
        while (true)
        {
            for (m[0xC3DF] = 8; m[0xC3DF] != 0; m[0xC3DF]--) // $1B13
            {
                int hl = W(0xC3E1);
                if (hl == 0) yield break;                  // $1B1D
                int v = Bcd(hl & 0xFF) + 100 * Bcd(hl >> 8) - 1;
                SetW(0xC3E1, ToBcd(v % 100) | ToBcd(v / 100) << 8);
                PrintBcd(W(0xC3E3), 0xC3E2, 4, 0x00);      // $1B36 $0AD1
                Enemies.Score(m, m[0xC3E0]);               // $1B3C $0E48
                ScoreUpdate();                             // $1B3F $0D28
            }
            yield return 0;                                // $1B48 $0417
            Sound(m, 0x9A);                                // $1B4D $0440
        }
    }

    static byte ToBcd(int v) => (byte)((v / 10) << 4 | v % 10);

    // $0AD1: b cifras BCD desde (de) hacia abajo (nibble alto primero) a celdas en hl; ceros a la
    // izquierda como tile 0, cifras $30+n; atributo = c & 8. Sin verificar.
    void PrintBcd(int hl, int de, int b, int c)
    {
        c &= ~3;
        while (b-- > 0)
        {
            int a = m[de];
            if ((c & 1) == 0) a >>= 4;
            a &= 0x0F;
            byte v;
            if (a != 0) { c |= 2; v = (byte)(a + 0x30); }
            else v = (byte)((c & 2) != 0 ? 0x30 : 0);
            m[hl++ & 0xFFFF] = v;
            c ^= 1;
            m[hl++ & 0xFFFF] = (byte)(c & 8);
            if ((c & 1) == 0) de--;
        }
    }

    // $1944: reanudacion y paso a la siguiente ronda o al fin de juego. Rama de siguiente ronda
    // ($C313 < 2) verificada con --check-round-end (f8012 -> f8013, 11 bytes). Rama de fin de juego
    // ($C313 >= 2) sin verificar: ninguna grabacion llega a ella.
    IEnumerator<int> RoundNext()
    {
        SetW(0xC30B, 0x0100);
        m[0xC30F] = m[0xC312] = m[0xC311] = 0;
        m[0xC07F] = 0;
        if (m[0xC313] < 2)                                 // $1957
        {
            m[0xC301] = (byte)((m[0xC301] + 1) & 0x1F);
            m[0xC07E] = 4;
        }
        else
        {
            if (m[0xC302] >= 2)                            // $196E
            {
                for (int i = m[0xC302] - 1; i > 0; i--) Enemies.Score(m, 0x0F); // $1977 $0E48
                yield return 0; yield return 0;            // $197E $0422(2)
            }
            m[0xDC00] = 0;                                 // $1983
            Clear(0x1F);                                   // $1989 $0A24($1F)
            m[0xC07F] = 2;
            m[0xC302] = 0;
        }
        m[0xC032] = 0;                                     // $1995
    }

    // $2C39: cola de $2BD5 con $C479 = 1 y todas las ranuras libres. Con $C408 != 0: $C408=0,
    // $C85F=5 y vitalidad +$0500 ($4EF4). Objetos de fin de ronda en las ranuras $C9E0 ($98, x=$3C40),
    // $C9C0 ($9B, $3D00), $C8C0 ($AC, $3E80), $C960 ($BA/$FC, $3EC0); con $C300 = 7, o $C300 = 6 o
    // >= 8 y $C314 < $1C, ademas $C313=1 y $C980 ($A8, $3F60). $C479++. Sin verificar.
    void RoundGoalSpawn()
    {
        if (m[0xC408] != 0)
        {
            m[0xC408] = 0;
            m[0xC85F] = 0x05;
            Enemies.Vitality(m, 0x0500);                   // $2C4B $4EF4
        }
        m[0xC9F8] = 0x98; SetW(0xC9F0, 0x3C40);            // $2C4E
        m[0xC9D8] = 0x9B; SetW(0xC9D0, 0x3D00);
        m[0xC8D8] = 0xAC; SetW(0xC8D0, 0x3E80);
        m[0xC978] = 0xBA; m[0xC979] = 0xFC; SetW(0xC970, 0x3EC0);
        int a = m[0xC300];                                 // $2C7F
        if (a >= 6 && (a == 7 || m[0xC314] < 0x1C))
        {
            m[0xC313] = 0x01;                              // $2C90
            m[0xC998] = 0xA8; SetW(0xC990, 0x3F60);
        }
        m[0xC479]++;                                       // $2CA0
    }
}
