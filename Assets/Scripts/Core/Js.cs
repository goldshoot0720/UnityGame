// JavaScript-compatible helpers so the TypeScript game rules port 1:1 (rounding, stable
// "sort then shift" queues, insertion-ordered maps, injectable RNGs, hex colours).
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MoeGames
{
    public static class Js
    {
        /// <summary>JS Math.round: halves round towards +∞ (C# Math.Round is banker's rounding).</summary>
        public static double Round(double x) => Math.Floor(x + 0.5);
        public static int RoundInt(double x) => (int)Math.Floor(x + 0.5);
        public static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
        public static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
        public static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
        public static double Lerp(double a, double b, double t) => a + (b - a) * t;
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
        public static double Hypot(double x, double y) => Math.Sqrt(x * x + y * y);
        public static double Sign(double x) => x > 0 ? 1 : x < 0 ? -1 : 0;
        /// <summary>JS Math.trunc.</summary>
        public static double Trunc(double x) => x < 0 ? Math.Ceiling(x) : Math.Floor(x);

        /// <summary>Equivalent of JS <c>q.sort((a,b) =&gt; key(a)-key(b)); q.shift()</c> on a queue that is only
        /// appended to: the stable sort keeps ties in insertion order, so this is the first strict minimum.</summary>
        public static T ShiftMin<T>(List<T> q, Func<T, double> key)
        {
            int bi = 0;
            double bk = key(q[0]);
            for (int i = 1; i < q.Count; i++)
            {
                double k = key(q[i]);
                if (k < bk) { bk = k; bi = i; }
            }
            T r = q[bi];
            q.RemoveAt(bi);
            return r;
        }

        /// <summary>Stable in-place sort (JS Array.prototype.sort is stable; List.Sort is not).</summary>
        public static void StableSort<T>(List<T> list, Comparison<T> cmp)
        {
            if (list.Count < 2) return;
            var tmp = new T[list.Count];
            list.CopyTo(tmp);
            MergeSort(tmp, new T[tmp.Length], 0, tmp.Length, cmp);
            for (int i = 0; i < tmp.Length; i++) list[i] = tmp[i];
        }

        static void MergeSort<T>(T[] a, T[] buf, int lo, int hi, Comparison<T> cmp)
        {
            if (hi - lo < 2) return;
            int mid = (lo + hi) / 2;
            MergeSort(a, buf, lo, mid, cmp);
            MergeSort(a, buf, mid, hi, cmp);
            int i = lo, j = mid, k = lo;
            while (i < mid && j < hi) buf[k++] = cmp(a[j], a[i]) < 0 ? a[j++] : a[i++];
            while (i < mid) buf[k++] = a[i++];
            while (j < hi) buf[k++] = a[j++];
            for (k = lo; k < hi; k++) a[k] = buf[k];
        }

        public static List<T> Sorted<T>(IEnumerable<T> src, Comparison<T> cmp)
        {
            var l = new List<T>(src);
            StableSort(l, cmp);
            return l;
        }

        /// <summary>Parse '#rgb', '#rrggbb' or '#rrggbbaa'.</summary>
        public static Color Hex(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return Color.white;
            string h = hex[0] == '#' ? hex.Substring(1) : hex;
            if (h.Length == 3 || h.Length == 4)
            {
                var s = "";
                foreach (char c in h) s += new string(c, 2);
                h = s;
            }
            if (h.Length < 6) return Color.magenta;
            byte P(int i) => Convert.ToByte(h.Substring(i, 2), 16);
            byte a = h.Length >= 8 ? P(6) : (byte)255;
            return new Color32(P(0), P(2), P(4), a);
        }

        public static Color Hex(string hex, float alpha)
        {
            var c = Hex(hex);
            c.a = alpha;
            return c;
        }
    }

    /// <summary>Random sources: the TS code takes <c>rng: () =&gt; number</c>; here it is <c>Func&lt;double&gt;</c>.</summary>
    public static class Rand
    {
        static readonly System.Random shared = new System.Random();
        /// <summary>Math.random equivalent in [0, 1).</summary>
        public static readonly Func<double> Default = () => shared.NextDouble();
        public static Func<double> Seeded(int seed)
        {
            var r = new System.Random(seed);
            return () => r.NextDouble();
        }
        /// <summary>Cycles through fixed values — the verify.ts <c>seq([...])</c> helper.</summary>
        public static Func<double> Seq(params double[] vals)
        {
            int i = 0;
            return () => vals[i++ % vals.Length];
        }
        public static double Value => shared.NextDouble();
        public static int Int(int maxExclusive) => (int)Math.Floor(shared.NextDouble() * maxExclusive);
        public static T Pick<T>(IList<T> l, Func<double> rng = null) => l[(int)Math.Floor((rng ?? Default)() * l.Count)];
        /// <summary>Fisher–Yates shuffle, in place (same algorithm most of the TS sources use).</summary>
        public static void Shuffle<T>(IList<T> l, Func<double> rng = null)
        {
            rng ??= Default;
            for (int i = l.Count - 1; i > 0; i--)
            {
                int j = (int)Math.Floor(rng() * (i + 1));
                (l[i], l[j]) = (l[j], l[i]);
            }
        }
    }

    /// <summary>Insertion-ordered map with JS <c>Map</c> semantics (overwriting keeps the original position).</summary>
    public class OrderedMap<TK, TV> : IEnumerable<KeyValuePair<TK, TV>>
    {
        readonly Dictionary<TK, int> index = new Dictionary<TK, int>();
        readonly List<TK> keys = new List<TK>();
        readonly List<TV> vals = new List<TV>();
        readonly List<bool> alive = new List<bool>();
        int count;

        public int Count => count;
        public bool Has(TK k) => index.ContainsKey(k);
        public bool TryGet(TK k, out TV v)
        {
            if (index.TryGetValue(k, out int i)) { v = vals[i]; return true; }
            v = default;
            return false;
        }
        public TV Get(TK k, TV fallback = default) => index.TryGetValue(k, out int i) ? vals[i] : fallback;
        public void Set(TK k, TV v)
        {
            if (index.TryGetValue(k, out int i)) { vals[i] = v; return; }
            index[k] = keys.Count;
            keys.Add(k); vals.Add(v); alive.Add(true);
            count++;
        }
        public bool Delete(TK k)
        {
            if (!index.TryGetValue(k, out int i)) return false;
            index.Remove(k);
            alive[i] = false;
            vals[i] = default;
            count--;
            return true;
        }
        public void Clear() { index.Clear(); keys.Clear(); vals.Clear(); alive.Clear(); count = 0; }
        public IEnumerable<TV> Values { get { for (int i = 0; i < keys.Count; i++) if (alive[i]) yield return vals[i]; } }
        public IEnumerable<TK> Keys { get { for (int i = 0; i < keys.Count; i++) if (alive[i]) yield return keys[i]; } }
        public IEnumerator<KeyValuePair<TK, TV>> GetEnumerator()
        {
            for (int i = 0; i < keys.Count; i++) if (alive[i]) yield return new KeyValuePair<TK, TV>(keys[i], vals[i]);
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
