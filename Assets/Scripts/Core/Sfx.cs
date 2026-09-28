// Procedural sound effects mirroring the Phaser engine's synth specs
// ({ notes: 'C5 E5 G5', step, type: 'square' } / { type: 'noise', filter: ... }), so every
// game has sound before any Unity-AI audio exists. A generated clip in the registry with the
// same name (e.g. Assets/Generated/Game1/Audio/crack.wav) automatically replaces a defined sound.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoeGames
{
    public enum Wave { Square, Triangle, Sine, Sawtooth, Noise }
    public enum FilterType { None, Lowpass, Highpass, Bandpass }

    public class Tone
    {
        public Wave Type = Wave.Square;
        public float Freq = 440f, FreqEnd = -1f;
        public float Duration = 0.15f, Volume = 0.4f, Attack = 0.005f, Delay;
        /// <summary>Exponential decay envelope ('curve: exponential'); otherwise linear.</summary>
        public bool Exp;
        public FilterType Filter;
        public float FilterFreq = 1000f, FilterFreqEnd = -1f, Q = 1f;
        /// <summary>Melody like "C5 E5 G5 C6:2" (":n" = n steps); empty for a single tone.</summary>
        public string Notes;
        public float Step = 0.1f;

        public static Tone Melody(string notes, float step, Wave type = Wave.Square, float vol = 0.4f)
            => new Tone { Notes = notes, Step = step, Type = type, Volume = vol };
        public static Tone Beep(float freq, float dur, Wave type = Wave.Square, float vol = 0.3f, float freqEnd = -1f)
            => new Tone { Freq = freq, FreqEnd = freqEnd, Duration = dur, Type = type, Volume = vol };
        public static Tone Noise(float dur, float vol, FilterType f = FilterType.None, float ff = 1000f, float ffEnd = -1f, bool exp = true, float q = 1f)
            => new Tone { Type = Wave.Noise, Duration = dur, Volume = vol, Filter = f, FilterFreq = ff, FilterFreqEnd = ffEnd, Exp = exp, Q = q };
    }

    public class Sfx : MonoBehaviour
    {
        const int Rate = 44100;
        static Sfx inst;
        readonly List<AudioSource> voices = new List<AudioSource>();
        AudioSource music;
        readonly Dictionary<string, AudioClip> named = new Dictionary<string, AudioClip>();
        readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();
        public static float MasterVolume = 0.8f;
        /// <summary>Registry scope ("Game1".."Game12") used to prefer a game's own generated audio.</summary>
        public static string Scope;

        static Sfx I
        {
            get
            {
                if (!inst)
                {
                    var go = new GameObject("MoeSfx");
                    DontDestroyOnLoad(go);
                    inst = go.AddComponent<Sfx>();
                    inst.music = go.AddComponent<AudioSource>();
                    inst.music.loop = true;
                    inst.music.playOnAwake = false;
                }
                return inst;
            }
        }

        AudioSource Voice()
        {
            foreach (var v in voices) if (!v.isPlaying) return v;
            if (voices.Count >= 24) return voices[0];
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false;
            voices.Add(s);
            return s;
        }

        public static void PlayClip(AudioClip clip, float volume = 1f, float pitch = 1f)
        {
            if (!clip) return;
            var v = I.Voice();
            v.pitch = pitch;
            v.PlayOneShot(clip, volume * MasterVolume);
        }

        /// <summary>Register a named sound built from layered tones (sound.define).</summary>
        public static void Define(string name, params Tone[] layers)
        {
            I.named[name] = Render(name, layers);
        }

        /// <summary>Play a named sound: generated audio from the registry wins, then the defined synth.</summary>
        public static void Play(string name, float volume = 1f)
        {
            var gen = MoeRegistry.I ? MoeRegistry.I.Audio(name, Scope) : null;
            if (gen) { PlayClip(gen, volume); return; }
            if (I.named.TryGetValue(name, out var c)) PlayClip(c, volume);
        }

        public static bool Has(string name) => I.named.ContainsKey(name) || (MoeRegistry.I && MoeRegistry.I.Audio(name, Scope));

        /// <summary>Play an ad-hoc synth spec (cached by its parameters).</summary>
        public static void Play(params Tone[] layers)
        {
            string key = Key(layers);
            if (!I.cache.TryGetValue(key, out var c)) I.cache[key] = c = Render(key, layers);
            PlayClip(c);
        }

        public static void Notes(string notes, float step, Wave type = Wave.Square, float vol = 0.4f) => Play(Tone.Melody(notes, step, type, vol));
        public static void Beep(float freq, float dur, Wave type = Wave.Square, float vol = 0.3f, float freqEnd = -1f) => Play(Tone.Beep(freq, dur, type, vol, freqEnd));
        internal static void Click() => Beep(660, 0.06f, Wave.Square, 0.25f);

        /// <summary>Loop a music clip (e.g. a generated one); null stops.</summary>
        public static void Music(AudioClip clip, float volume = 0.35f)
        {
            var m = I.music;
            if (m.clip == clip && m.isPlaying) return;
            m.Stop();
            m.clip = clip;
            m.volume = volume * MasterVolume;
            if (clip) m.Play();
        }

        /// <summary>Loop a registry music clip by name if one has been generated.</summary>
        public static void MusicByName(string name, float volume = 0.35f)
        {
            var clip = MoeRegistry.I ? MoeRegistry.I.Audio(name, Scope) : null;
            if (clip) Music(clip, volume);
        }

        public static void StopMusic() => Music(null);

        static string Key(Tone[] layers)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var t in layers)
                sb.Append(t.Type).Append(t.Freq).Append('/').Append(t.FreqEnd).Append('/').Append(t.Duration).Append('/').Append(t.Volume)
                  .Append('/').Append(t.Exp).Append(t.Filter).Append(t.FilterFreq).Append('/').Append(t.FilterFreqEnd).Append('/').Append(t.Notes)
                  .Append('/').Append(t.Step).Append('/').Append(t.Delay).Append(t.Attack).Append('|');
            return sb.ToString();
        }

        // ── synthesis ──
        static readonly string[] noteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

        public static float NoteFreq(string n)
        {
            if (string.IsNullOrEmpty(n)) return 0;
            int i = 1;
            string letter = n.Substring(0, 1).ToUpperInvariant();
            int semi = Array.IndexOf(noteNames, letter);
            if (semi < 0) return 0;
            if (i < n.Length && (n[i] == '#' || n[i] == 'b'))
            {
                semi += n[i] == '#' ? 1 : -1;
                i++;
            }
            int oct = 4;
            if (i < n.Length && int.TryParse(n.Substring(i), out int o)) oct = o;
            int midi = (oct + 1) * 12 + semi;
            return 440f * Mathf.Pow(2f, (midi - 69) / 12f);
        }

        struct Seg { public Tone t; public float start, dur, freq; }

        static AudioClip Render(string name, Tone[] layers)
        {
            var segs = new List<Seg>();
            float total = 0;
            foreach (var t in layers)
            {
                if (!string.IsNullOrEmpty(t.Notes))
                {
                    float at = t.Delay;
                    foreach (var tok in t.Notes.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var parts = tok.Split(':');
                        float len = parts.Length > 1 && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float l) ? l : 1f;
                        float d = t.Step * len;
                        if (parts[0] != "-" && parts[0] != "_" && parts[0] != "R") segs.Add(new Seg { t = t, start = at, dur = d * 1.05f, freq = NoteFreq(parts[0]) });
                        at += d;
                    }
                    total = Mathf.Max(total, at + 0.05f);
                }
                else
                {
                    segs.Add(new Seg { t = t, start = t.Delay, dur = t.Duration, freq = t.Freq });
                    total = Mathf.Max(total, t.Delay + t.Duration);
                }
            }
            int n = Mathf.Max(1, Mathf.CeilToInt(total * Rate));
            var data = new float[n];
            var rnd = new System.Random(name.GetHashCode());
            foreach (var s in segs) Synth(data, s, rnd);
            float peak = 0;
            for (int i = 0; i < n; i++) peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            if (peak > 1f) for (int i = 0; i < n; i++) data[i] /= peak;
            var clip = AudioClip.Create("sfx_" + (name.Length > 40 ? name.GetHashCode().ToString() : name), n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static void Synth(float[] data, Seg s, System.Random rnd)
        {
            var t = s.t;
            int start = Mathf.RoundToInt(s.start * Rate), len = Mathf.RoundToInt(s.dur * Rate);
            double phase = 0;
            float lp = 0, hp = 0, prevIn = 0, bp1 = 0;
            float f0 = s.freq, f1 = t.FreqEnd > 0 && string.IsNullOrEmpty(t.Notes) ? t.FreqEnd : s.freq;
            float atk = Mathf.Max(0.001f, t.Attack);
            for (int i = 0; i < len && start + i < data.Length; i++)
            {
                float u = (float)i / len;
                float tt = (float)i / Rate;
                float f = f0 * Mathf.Pow(f1 / Mathf.Max(1f, f0), u);
                if (f0 <= 0) f = f1;
                phase += f / Rate;
                float p = (float)(phase - Math.Floor(phase));
                float v;
                switch (t.Type)
                {
                    case Wave.Square: v = p < 0.5f ? 0.6f : -0.6f; break;
                    case Wave.Triangle: v = 1f - 4f * Mathf.Abs(p - 0.5f); break;
                    case Wave.Sine: v = Mathf.Sin(p * Mathf.PI * 2f); break;
                    case Wave.Sawtooth: v = (2f * p - 1f) * 0.6f; break;
                    default: v = (float)(rnd.NextDouble() * 2 - 1); break;
                }
                if (t.Filter != FilterType.None)
                {
                    float fc = t.FilterFreqEnd > 0 ? t.FilterFreq * Mathf.Pow(t.FilterFreqEnd / t.FilterFreq, u) : t.FilterFreq;
                    float a = Mathf.Clamp01(2f * Mathf.PI * fc / Rate);
                    switch (t.Filter)
                    {
                        case FilterType.Lowpass: lp += a * (v - lp); v = lp; break;
                        case FilterType.Highpass:
                            hp = (1f - a) * (hp + v - prevIn);
                            prevIn = v;
                            v = hp;
                            break;
                        case FilterType.Bandpass:
                            {
                                // Two one-pole stages around fc; narrower with higher Q.
                                float bw = Mathf.Clamp(1f / Mathf.Max(0.3f, t.Q), 0.05f, 2f);
                                float aLo = Mathf.Clamp01(a * (1f + bw)), aHi = Mathf.Clamp01(a * (1f - bw * 0.5f));
                                lp += aLo * (v - lp);
                                bp1 += aHi * (lp - bp1);
                                v = (lp - bp1) * 2f;
                                break;
                            }
                    }
                }
                float env = tt < atk ? tt / atk : 1f;
                float rest = 1f - u;
                env *= t.Exp ? Mathf.Pow(rest, 3f) : Mathf.Min(1f, rest * 4f);
                data[start + i] += v * env * t.Volume;
            }
        }
    }
}
