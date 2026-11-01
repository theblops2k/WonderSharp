namespace WonderBoy.Game;

// Pantallas de creditos y espera de START de la tabla de juego $4F20 (despacho en GameFrame).
//   $1283  creditos: $C008 bit7, $C003=1, espera $0417, borrado $0A24($0F), paletas $0736(4/7),
//          objetos $C800/$C820 ($1FA3+$1FC0), $239E, textos $0B7A(1/$0D/2), 6 fotogramas ($0422),
//          luego $C008 bit7=0, $C3DF=0 y $C032=0 (pasa al estado siguiente de $4F20).
//   $132C  espera START: escribe $EEF8 con $C016|$30, anima $C820 ($1FC0) y $239E en cada fotograma.
//          START1 ($C00E bit4) o START2 (bit5) pasan a $136E; sin creditos (<2) se queda.
// Sonido $0440 ($F7, $02): TitleRom.Sound. Tiempos de $0A24 medidos solo por codigo (sin $0A24 lost frames).
public sealed partial class TitleRom
{
    const int TextTable = 0x50D9;                     // $0B7A usa siempre HL=$50D9 y A como indice

    void CreditsState() // $1283
    {
        m[0xC008] |= 0x80;
        m[0xC003] = 1;
        // $1290 sonido $F7 tras la $0417: en ram_rand esta en la cola en f126, que en el port es el
        // fotograma del despacho (el cuerpo de abajo va un fotograma despues).
        Sound(m, 0xF7);                                // $1290 $0440
        // $0417 inicial: el cuerpo corre en el fotograma siguiente al despacho (ram_rand f126)
        pending.Enqueue(() =>
        {
            m[0xC07F] = 0; SetW(0xC000, 0); m[0xC002] = 0;
            Clear(0x0F);                               // $0A24($0F)
            m[0xDC00] = 0xAD;
            QueuePalette(0x04);                        // $0736
            QueuePalette(0x07);
            m[0xC0BD] = 1;
            SetW(0xC442, 0x0100); SetW(0xC810, 0x0100); SetW(0xC830, 0x0100);
            SetW(0xC444, 0x0300);
            SetW(0xC812, 0x02CC); SetW(0xC832, 0x0270);
            m[0xC81F] = 1; m[0xC83F] = 1;
            ObjectRoutines.Start(m, 0xC800, 0x0600);   // $1FA3 + $1FC0
            ObjectRoutines.Start(m, 0xC820, 0x0000);
            WorldToScreen.Update(m);                   // $239E
            Text(TextTable, 0x01);                     // $0B7A
            if (m[0xC02F] != 0) Text(TextTable, 0x0D);
            if (m[0xC016] < 0x02) Text(TextTable, 0x02);

            // 6 esperas $0417 de $0422 (B=6). Calibrado con ram_rand: $C07E pasa a 2 en f134.
            for (int i = 0; i < 6; i++) pending.Enqueue(() => { });
            pending.Enqueue(() =>
            {
                m[0xC008] &= 0x7F; m[0xC3DF] = 0; m[0xC032] = 0;
            });
        });
    }

    void StartWaitState() // $132C
    {
        m[0xEEF8] = (byte)(m[0xC016] | 0x30);
        ObjectRoutines.Animate(m, 0xC820);             // $1FC0
        WorldToScreen.Update(m);                       // $239E
        Sound(m, 0x02);                                // $1345 $0440

        int b;
        int input = m[0xC00E];
        if ((input & 0x10) != 0) b = 0;                // START1 -> $136E
        else
        {
            if (m[0xC016] < 0x02) return;              // sin creditos: espera
            b = 1;
            if ((input & 0x20) == 0)                   // START2 -> $136E
            {
                if (m[0xC3DF] != 0) return;
                Text(TextTable, 0x04);                 // $0B7A(4)
                m[0xC3DF] = 1;
                return;
            }
        }
        m[0xC083] = (byte)b;                           // $136E
        Text(TextTable, b == 0 ? 0x03 : 0x05);         // $1372-$137A
        m[0xC032] = 0; m[0xC088] = 0;
        m[0xDC00] = 0;
    }
}
