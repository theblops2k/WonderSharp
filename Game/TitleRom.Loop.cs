namespace WonderBoy.Game;

// Bucle de juego de la tabla $4F20 ($C07E=7): $18B7.
//   $18B7  con $C440 = 0: $2193, grabacion de la demo ($C3CE != 0, IX=$C3CA) y, si el jugador acabo
//          ($C400 != 0), sonido $C2, $C088 = 0 y $C032 = 0 (el planificador pasa a $C07E=8, $1B52).
//          con $C440 != 0 (fin de ronda): $1908 despacho por $C07F con la tabla $190E
//          (0 $1918, 1 $191D, 2 $1932, 3 $193C, 4 $1944), en TitleRom.Round.End.cs (sin verificar).
//   $2193  con $C477 = 0: $274D (PlayerRoutine) y $4CD2; siempre $2BD5 y $24EB; con $C477 = 0 $21CF
//          y $2297; siempre $239E; con $C477 = 0 $4E3F (vitalidad).
//   $21BC  igual sin $274D ni $4E3F y con $21CF/$2297 sin condicion (presentacion de ronda).
public sealed partial class TitleRom
{
    void GameLoopState() // $18B7
    {
        if (m[0xC440] != 0) { RoundEndState(); return; }   // $1908
        GameTick();                                        // $2193
        if (Unported != null) return;
        if (m[0xC3CE] != 0) DemoRecord();                  // $18C1
        if (m[0xC400] == 0) return;                        // $18F6
        Sound(m, 0xC2);                                    // $18FD $0440
        m[0xC088] = 0;
        m[0xC032] = 0;
    }

    // $18C7: grabacion de entradas de la demo. IX=$C3CA: +0 ultima entrada ($C086 & $C6), +1 cuenta,
    // +2/+3 puntero de escritura (pares cuenta, entrada; $FF de fin). Sin verificar (ram_rand: $C3CE=0).
    void DemoRecord()
    {
        int a = m[0xC086] & 0xC6;
        if (a == m[0xC3CA]) { m[0xC3CB]++; return; }      // $18D5
        int hl = W(0xC3CC);
        m[hl] = m[0xC3CB];
        m[(hl + 1) & 0xFFFF] = (byte)a;
        hl = (hl + 2) & 0xFFFF;
        m[hl] = 0xFF;
        SetW(0xC3CC, hl);
        m[0xC3CA] = (byte)a;
        m[0xC3CB] = 0;
    }

    // $1B52 (tabla $4F20 [8]): perdida de vida, despacho por $C07F con la tabla $1B58.
    //   0 $1B5E  $21BC, $C013 = 1.
    //   1 $1B6B  $21BC hasta que pasa $0AA6 (1 s).
    //   2 $1B76  punto de reanudacion $C30B = ($C442 & $F000, o $0100) y $C30F = H-1; sin juego libre
    //            ($C02F = 0) $C302--; copia $C300-$C33F al jugador ($C340, o $C380 con $C3C3 != 0);
    //            $C3C3 ^= $C083; con vidas $C07E = 3 (el planificador pasa a 4, $13D9); $C07F = $C032 = 0.
    void LifeLostState()
    {
        switch (m[0xC07F])
        {
            case 0:
                GameStep();                                // $21BC
                if (Unported != null) return;
                m[0xC013] = 1;
                m[0xC07F]++;
                break;
            case 1:
                GameStep();                                // $21BC
                if (Unported != null) return;
                if (!WaitSeconds()) return;                // $0AA6
                m[0xC07F]++;
                break;
            case 2:
            {
                int h = m[0xC443] & 0xF0;                  // $1B76
                if (h == 0) h = 1;
                SetW(0xC30B, h << 8);
                m[0xC30F] = (byte)(h - 1);
                if (m[0xC02F] == 0 && m[0xC302] != 0) m[0xC302]--; // $1B89
                int de = m[0xC3C3] == 0 ? 0xC340 : 0xC380;
                Array.Copy(m, 0xC300, m, de, 0x40);        // $1BA6 ldir
                m[0xC3C3] ^= m[0xC083];
                if (m[0xC302] != 0) m[0xC07E] = 3;         // $1BB9
                m[0xC07F] = 0;
                m[0xC032] = 0;
                break;
            }
            default: Unported = $"$1B52/{m[0xC07F]}"; break;
        }
    }

    // $2193: un paso de juego completo.
    void GameTick()
    {
        tickStep = stepCount;                              // solo para InLevel (dibujo panoramico)
        bool live = m[0xC477] == 0;
        if (live)
        {
            int miss = PlayerRoutine.Update(m);            // $274D
            if (miss != 0) { Unported = $"${miss:X4}"; return; }
            Weapon.Update(m);                              // $4CD2
        }
        EnemySlots();                                      // $2BD5
        if (Unported != null) return;
        SlotUpdate.Update(m);                              // $24EB
        if (live)
        {
            Camera.Update(m);                              // $21CF
            TilemapFill.Update(m);                         // $2297
        }
        WorldToScreen.Update(m);                           // $239E
        if (live) VitalityTick();                          // $4E3F
    }

    // $4E3F: con $C440 = 0, $C469 -= 2 ($C46F != 0: -= 8); si pasa a negativo, $C469 = 0, $C85E bit2
    // y texto $0B75(5). Si $C46A cambio respecto a $C46B, redibuja la barra ($4E74).
    void VitalityTick()
    {
        if (m[0xC440] != 0) return;
        int hl = (W(0xC469) + (m[0xC46F] != 0 ? 0xFFF8 : 0xFFFE)) & 0xFFFF; // $4E44-$4E53
        SetW(0xC469, hl);
        if ((hl & 0x8000) != 0)                            // $4E57
        {
            SetW(0xC469, 0);
            m[0xC85E] |= 0x04;
            Text(0x5429, 0x05);                            // $0B75
        }
        if (m[0xC46A] != m[0xC46B]) VitalityBar();         // $4E6B-$4E74
    }
}
