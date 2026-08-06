using System;
using System.Collections.Generic;
using Godot;

namespace Slingtt.Render;

public enum SfxId
{
    Launch,
    WallBounce,
    Hit,
    HeavyHit,
    Ultimate,
    Death,
    Heal,
    Victory,
    Defeat,
    UiTap,
}

// Procedurally synthesised sound effects. The web original generates its SFX with
// WebAudio oscillators rather than shipping audio files, and this keeps that
// property: the APK carries no audio binaries, so there is no sound asset that can
// fail to package or go missing on a case-sensitive filesystem.
public sealed partial class Sfx : Node
{
    private const int SampleRate = 22050;
    private const int VoiceCount = 8;

    private readonly Dictionary<SfxId, AudioStreamWav> _cache = new();
    private readonly List<AudioStreamPlayer> _voices = new();
    private int _cursor;

    public bool Muted { get; set; }

    public override void _Ready()
    {
        for (int i = 0; i < VoiceCount; i++)
        {
            var p = new AudioStreamPlayer { Bus = "Master", VolumeDb = -6f };
            AddChild(p);
            _voices.Add(p);
        }
        foreach (SfxId id in Enum.GetValues<SfxId>())
        {
            _cache[id] = Synth(id);
        }
    }

    public void Play(SfxId id, float volumeDb = -6f)
    {
        if (Muted || _voices.Count == 0)
        {
            return;
        }
        AudioStreamPlayer p = _voices[_cursor];
        _cursor = (_cursor + 1) % _voices.Count;
        p.Stream = _cache[id];
        p.VolumeDb = volumeDb;
        p.Play();
    }

    private static AudioStreamWav Synth(SfxId id)
    {
        (float dur, Func<float, float, float> voice) = id switch
        {
            // t = seconds, k = normalised 0..1 progress through the sound.
            SfxId.Launch => (0.22f, (Func<float, float, float>)((t, k)
                => Osc(t, Lerp(220, 660, k)) * Env(k, 0.02f) * 0.5f
                   + Noise(t) * Env(k, 0.05f) * 0.15f)),

            SfxId.WallBounce => (0.09f, (t, k)
                => Osc(t, Lerp(900, 420, k)) * Env(k, 0.005f) * 0.45f),

            SfxId.Hit => (0.16f, (t, k)
                => Osc(t, Lerp(320, 90, k)) * Env(k, 0.004f) * 0.7f
                   + Noise(t) * Env(k, 0.004f) * 0.25f),

            SfxId.HeavyHit => (0.32f, (t, k)
                => Osc(t, Lerp(180, 45, k)) * Env(k, 0.006f) * 0.85f
                   + Noise(t) * Env(k, 0.01f) * 0.3f),

            SfxId.Ultimate => (0.55f, (t, k)
                => Osc(t, Lerp(180, 900, k)) * Env(k, 0.06f) * 0.5f
                   + Osc(t, Lerp(270, 1350, k)) * Env(k, 0.06f) * 0.25f),

            SfxId.Death => (0.42f, (t, k)
                => Osc(t, Lerp(420, 70, k)) * Env(k, 0.01f) * 0.6f
                   + Noise(t) * Env(k, 0.02f) * 0.2f),

            SfxId.Heal => (0.38f, (t, k)
                => Osc(t, Lerp(520, 880, k)) * Env(k, 0.08f) * 0.4f),

            SfxId.Victory => (0.9f, (t, k)
                => Osc(t, Step(k, 523.25f, 659.25f, 783.99f, 1046.5f)) * Env(k, 0.05f) * 0.5f),

            SfxId.Defeat => (0.9f, (t, k)
                => Osc(t, Step(k, 440f, 392f, 349.23f, 261.63f)) * Env(k, 0.05f) * 0.5f),

            _ => (0.06f, (t, k) => Osc(t, 880f) * Env(k, 0.004f) * 0.35f),
        };

        int frames = (int)(dur * SampleRate);
        var pcm = new byte[frames * 2];
        for (int i = 0; i < frames; i++)
        {
            float t = i / (float)SampleRate;
            float k = i / (float)frames;
            float sample = Mathf.Clamp(voice(t, k), -1f, 1f);
            short s16 = (short)(sample * short.MaxValue);
            pcm[i * 2] = (byte)(s16 & 0xFF);
            pcm[i * 2 + 1] = (byte)((s16 >> 8) & 0xFF);
        }

        return new AudioStreamWav
        {
            Data = pcm,
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = SampleRate,
            Stereo = false,
        };
    }

    private static float Osc(float t, float freq) => Mathf.Sin(t * freq * Mathf.Tau);

    /// <summary>Deterministic value noise — a fixed hash, not Random, so a build is
    /// byte-reproducible.</summary>
    private static float Noise(float t)
    {
        int n = (int)(t * SampleRate);
        unchecked
        {
            uint h = (uint)n * 2654435761u;
            h ^= h >> 15;
            return (h & 0xFFFF) / 32768f - 1f;
        }
    }

    /// <summary>Attack/decay envelope: short linear attack, exponential tail.</summary>
    private static float Env(float k, float attack)
    {
        if (k < attack)
        {
            return k / Mathf.Max(attack, 1e-5f);
        }
        return Mathf.Exp(-4f * (k - attack));
    }

    private static float Lerp(float a, float b, float k) => a + (b - a) * k;

    private static float Step(float k, params float[] notes)
        => notes[Mathf.Clamp((int)(k * notes.Length), 0, notes.Length - 1)];
}
