namespace WonderBoy.Game;

// Carga de nivel: $1431 (32 pasadas de $22A2 con la tabla $5F38). El paso $22A2 (flujo de columnas
// que escribe pares (tile, color) en la VRAM $E000+) es TilemapFill.Pass. Estado: $C453 contador,
// $C454 puntero del flujo, $C456/$C458 punteros de datos, $C45A-$C45E deltas y contador de pares.
// Lo llama $1651 sub0 (TitleRom.Round.cs), repartido en varios fotogramas (ver LevelPasses).
public sealed partial class TitleRom
{
    // $1431-$145D: preparacion de las 32 pasadas con la entrada $C301 de $5F38.
    void LevelPassSetup()
    {
        m[0xC0BD] = 1;
        int hl = (m[0xC30F] & 1) != 0 ? 0x0100 : 0x0000;
        SetW(0xC000, hl);
        m[0xC002] = 0;
        SetW(0xC454, Table(0x5F38, m[0xC301]));    // $0A9A
        m[0xC453] = 0x10;
        m[0xC3DF] = 0x20;
        LevelLoading = true;                           // solo dibujo (WideView)
    }

    // $1460-$146A: hasta n pasadas de $22A2 contando $C3DF. true = $C3DF llego a 0 ($1431 termina).
    bool LevelPasses(int n)
    {
        for (int i = 0; i < n; i++)
        {
            TilemapFill.Pass(m);                       // $22A2
            levelTiles = true;                         // solo dibujo (LevelTilesDrawn)
            if (--m[0xC3DF] == 0) { LevelLoading = false; return true; }
        }
        return false;
    }
}
