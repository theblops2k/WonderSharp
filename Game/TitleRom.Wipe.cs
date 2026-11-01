namespace WonderBoy.Game;

// Barrido de fin de ronda $152D (estado $C07E=5): despacho por $C07F (tabla $1533).
//   [0] $1539  $0A73(2), $1546 (franja desde $E880, 32 pasos), $C07F++.
//   [1] $1635  $1556 (aborta hasta $C3DF=0), $0A24($10), $17D2, $C013=0, $C07F++.
//   [2] $1649  $C07F=0, $C032=0.
// Verificado contra ram_goal_scroll f2056-f2091 (FromRam f2056, 35 Step): $C07E $C07F $C081 $C032 $C3DF
// $C3E1 $C013 $C473 $C407 $C840-$C85F coinciden en todos los fotogramas.
// Sin verificar: las escrituras en VRAM de $1556 ($E880..) y la region de $0A73(2) fuera de $C000-$CFFF
// (la grabacion solo cubre $C000-$CFFF).
public sealed partial class TitleRom
{
    void RoundWipeState() // $152D
    {
        switch (m[0xC07F])
        {
            case 0:                                    // $1539
                FillRegion(2);                         // $0A73(2)
                StripeInit();                          // $1546
                m[0xC07F]++;
                break;
            case 1:                                    // $1635
                if (!Stripe()) return;                 // $1556
                Clear(0x10);                           // $0A24($10)
                PlayerPlaceRound();                    // $17D2
                m[0xC013] = 0;
                m[0xC07F]++;
                break;
            case 2:                                    // $1649
                m[0xC07F] = 0;
                m[0xC032] = 0;
                break;
            default: Unported = $"$152D/{m[0xC07F]}"; break;
        }
    }

    // $17D2: como $179B pero con X = $C30B-$C0.
    void PlayerPlaceRound() => PlayerPlaceAt(0xC0);
}
