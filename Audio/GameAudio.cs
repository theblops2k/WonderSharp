using Raylib_cs;

namespace WonderBoy.Audio;

// Salida de audio de las ventanas del port: los codigos de TitleRom.SoundPort van a SoundProgram.Nmi;
// cada fotograma (Frame) son 4 IRQ del programa de sonido y 735 muestras (44100 / 60) generadas por
// ToneGenerator a partir de las notas de ChipNotes, en 4 tramos (las notas cambian en cada IRQ).
// Las muestras se envian a un AudioStream de raylib (16 bits, mono).
public sealed class GameAudio : IDisposable
{
    const int FrameSamples = ToneGenerator.SampleRate / 60;
    const int Chunk = FrameSamples * 2;      // tamano de cada sub-buffer del AudioStream
    const int MaxPending = Chunk;       // latencia maxima antes de descartar muestras

    readonly SoundProgram program;
    readonly ChipNotes chips = new();
    readonly ToneGenerator gen;
    readonly AudioStream stream;
    readonly short[] frameBuf = new short[FrameSamples];
    readonly short[] chunkBuf = new short[Chunk];
    readonly List<short> pending = new();

    GameAudio(byte[] soundRom)
    {
        // Ganancias de mezcla del driver: sn1 0.40, sn2 0.60 (system1.cpp).
        gen = new ToneGenerator(0.40, 0.40, 0.40, 0.40, 0.60, 0.60, 0.60, 0.60);
        program = new SoundProgram(soundRom) { ChipWrite = chips.Write };
        chips.ApplyTo(gen);
        Raylib.SetAudioStreamBufferSizeDefault(Chunk);
        stream = Raylib.LoadAudioStream(ToneGenerator.SampleRate, 16, 1);
        Raylib.PlayAudioStream(stream);
    }

    // null si no hay dispositivo de audio.
    public static GameAudio? Open(byte[] soundRom)
    {
        Raylib.InitAudioDevice();
        if (!Raylib.IsAudioDeviceReady()) return null;
        return new GameAudio(soundRom);
    }

    public void Send(byte code) => program.Nmi(code);

    // SoundVolume (Config.cfg, 0..100) -> volumen del AudioStream (0..1), efecto inmediato.
    public void SetVolume(int percent) => Raylib.SetAudioStreamVolume(stream, Math.Clamp(percent, 0, 100) / 100f);

    public void Frame()
    {
        int done = 0;
        for (int i = 0; i < 4; i++)
        {
            program.Irq();
            chips.ApplyTo(gen);
            int n = (FrameSamples * (i + 1)) / 4 - done;
            gen.Render(frameBuf.AsSpan(done, n));
            done += n;
        }
        pending.AddRange(frameBuf);
        if (pending.Count > MaxPending) pending.RemoveRange(0, pending.Count - MaxPending);
        while (Raylib.IsAudioStreamProcessed(stream) && pending.Count >= Chunk)
        {
            pending.CopyTo(0, chunkBuf, 0, Chunk);
            pending.RemoveRange(0, Chunk);
            Raylib.UpdateAudioStream<short>(stream, chunkBuf, Chunk);
        }
    }

    public void Dispose()
    {
        Raylib.StopAudioStream(stream);
        Raylib.UnloadAudioStream(stream);
        Raylib.CloseAudioDevice();
    }
}
