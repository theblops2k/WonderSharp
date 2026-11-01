namespace WonderBoy.Game;

// Cola de comandos de sonido (sin salida de audio real).
//   $C0DB        cuenta de entradas (0..15)
//   $C0DC-$C0EA  codigos pendientes ($C0DC = el siguiente a enviar)
// $0440  peticion del codigo A (preserva bc/de/hl):
//        con $C005 != 0 (attract/demo) y A >= $09: A >= $C0 se descarta; si no, solo pasa con $C006 != 0.
//        Con la cola llena ($C0DB >= $0F) primero envia la cabeza ($0428, entre di/ei) y escribe en la
//        posicion $C0DB-1 (la original, no la decrementada por $0428); luego $C0DB = posicion + 1.
// $0428  envio (IRQ $006E, solo con $C009 != 0, y desde $0440): si $C0DB != 0, $C0DB--, OUT ($14),($C0DC)
//        y ldir de 14 bytes $C0DD -> $C0DC (siempre 14, sea cual sea la cuenta; $C0EA no se toca).
public sealed partial class TitleRom
{
    // Destino de OUT ($14): recibe cada codigo enviado. Sin audio real; null = se descarta.
    public static Action<byte>? SoundPort;

    internal static void Sound(byte[] m, int c) // $0440
    {
        if (m[0xC005] != 0 && c >= 0x09)                             // $0444-$044D
        {
            if (c >= 0xC0 || m[0xC006] == 0) return;                 // $044F-$0457
        }
        int a = m[0xC0DB];                                           // $0459
        if (a >= 0x0F)                                               // $045C
        {
            a--;                                                     // $0460
            SoundDrain(m);                                           // $0464
        }
        m[0xC0DC + a] = (byte)c;                                     // $046A
        m[0xC0DB] = (byte)(a + 1);                                   // $0472
    }

    internal static void SoundDrain(byte[] m) // $0428
    {
        if (m[0xC0DB] == 0) return;                                  // $042B
        m[0xC0DB]--;                                                 // $042D
        SoundPort?.Invoke(m[0xC0DC]);                                // $043B out ($14),a
        for (int i = 0; i < 0x0E; i++) m[0xC0DC + i] = m[0xC0DD + i]; // $043D ldir
    }
}
