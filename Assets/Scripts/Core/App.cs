// Entry point: boots automatically in any scene, shows the 3D hub (the eight characters on
// a stage + 12 game cards) and hosts one MiniGame at a time.
using UnityEngine;

namespace MoeGames
{
    public class App : MonoBehaviour
    {
        public static App I { get; private set; }
        public CamRig Rig { get; private set; }
        public MiniGame Current { get; private set; }

        Transform hub;
        Light sun;
        Chibi[] hubChars;
        float t;
        int focus;
        public int HubFocus => focus;
        string toast;
        float toastT;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoBoot()
        {
            if (I || FindAnyObjectByType<App>()) return;
            new GameObject("MoeApp").AddComponent<App>();
        }

        void Awake()
        {
            if (I && I != this) { Destroy(gameObject); return; }
            I = this;
            DontDestroyOnLoad(gameObject);
            Application.targetFrameRate = 60;
            Rig = CamRig.Create();
            TouchPad.Ensure();
            if (Application.isMobilePlatform)
            {
                // Phones: lighter rendering.
                QualitySettings.shadows = ShadowQuality.Disable;
                QualitySettings.antiAliasing = 0;
                Screen.sleepTimeout = SleepTimeout.NeverSleep;
            }
            DontDestroyOnLoad(Rig.gameObject);
            SetupLight();
            BuildHub();
        }

        void SetupLight()
        {
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { sun = l; break; }
            if (!sun)
            {
                var go = new GameObject("MoeSun");
                DontDestroyOnLoad(go);
                sun = go.AddComponent<Light>();
                sun.type = LightType.Directional;
            }
            ResetLight();
        }

        /// <summary>Default lighting; games may tweak <see cref="Sun"/> and ambient and it is restored on exit.</summary>
        public void ResetLight()
        {
            sun.transform.rotation = Quaternion.Euler(50, -30, 0);
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.intensity = 1.1f;
            sun.shadows = LightShadows.Soft;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.58f, 0.65f);
            RenderSettings.fog = false;
        }

        public Light Sun => sun;

        // ── hub ──

        void BuildHub()
        {
            hub = new GameObject("Hub").transform;
            Rig.Background(Js.Hex("#1b2a4a"));
            Rig.Set(new Vector3(0, 2.3f, -7.2f), new Vector3(0, 1.1f, 0), 45f);
            Prim.Cyl(hub, new Vector3(0, -0.1f, 0), 11f, 0.2f, Js.Hex("#2b3f73"));
            Prim.Cyl(hub, new Vector3(0, 0.0f, 0), 9f, 0.05f, Js.Hex("#ffe066"));
            Prim.Cyl(hub, new Vector3(0, 0.03f, 0), 8.6f, 0.05f, Js.Hex("#3a5ba0"));
            hubChars = new Chibi[Cast.Ids.Length];
            for (int i = 0; i < Cast.Ids.Length; i++)
            {
                float a = Mathf.Lerp(-60f, 60f, i / 7f) * Mathf.Deg2Rad;
                var pos = new Vector3(Mathf.Sin(a) * 3.6f, 0.05f, Mathf.Cos(a) * 1.6f - 0.4f);
                var c = Chibi.Spawn(Cast.Ids[i], hub, pos, 1.5f);
                c.Face(new Vector3(0, 0, -7) - pos);
                hubChars[i] = c;
            }
            Sfx.Scope = null;
            Sfx.MusicByName("hub_music");
        }

        void DestroyHub()
        {
            if (hub) Destroy(hub.gameObject);
            hub = null;
            hubChars = null;
        }

        public void Launch(int number)
        {
            var info = Catalog.Get(number);
            if (info == null) return;
            if (info.Type == null) { Toast($"《{info.Title}》製作中"); return; }
            DestroyHub();
            Sfx.StopMusic();
            ResetLight();
            Rig.Viewport(new Rect(0, 0, 1, 1));
            Time.timeScale = 1f;
            var go = new GameObject("Game" + number);
            var g = (MiniGame)go.AddComponent(info.Type);
            Current = g;
            g.Boot(info);
        }

        public void ReturnToHub()
        {
            if (Current) Destroy(Current.gameObject);
            Current = null;
            Fx.Clear();
            Popups.Clear();
            Sfx.StopMusic();
            Time.timeScale = 1f;
            ResetLight();
            Rig.Viewport(new Rect(0, 0, 1, 1));
            Rig.Cam.orthographic = false;
            BuildHub();
        }

        public void Toast(string s) { toast = s; toastT = 2f; }

        void Update()
        {
            float dt = Time.deltaTime;
            t += dt;
            Popups.Tick(Time.unscaledDeltaTime);
            GuideBook.Tick();
            if (GuideBook.IsOpen) return;
            if (toastT > 0) toastT -= Time.unscaledDeltaTime;
            if (Current || !hub) return;
            int n = Catalog.All.Count;
            if (In.RightDown) focus = (focus + 1) % n;
            if (In.LeftDown) focus = (focus + n - 1) % n;
            if (In.DownDown) focus = (focus + 4) % n;
            if (In.UpDown) focus = (focus + n - 4) % n;
            if (In.Confirm) { Launch(focus + 1); return; }
            int d = In.DigitDown;
            if (d > 0) { Launch(d); return; }
            hub.Rotate(0, Mathf.Sin(t * 0.3f) * 3f * dt, 0);
            if (hubChars != null && Mathf.Repeat(t, 0.9f) < dt)
            {
                var c = hubChars[Random.Range(0, hubChars.Length)];
                if (c && !c.Busy) c.Act(Random.value < 0.3f ? "win" : "hop", 0.6f);
            }
        }

        void OnGUI()
        {
            GUI.depth = -100;
            Gui.Begin();
            if (Current) Popups.Draw(Rig.Cam);
            else DrawHub();
            if (toastT > 0) Gui.Banner(toast, null, Gui.H * 0.5f);
            TouchPad.Layout = Current ? Current.Touch : null;
            TouchPad.Draw();
            GuideBook.Draw();
        }

        /// <summary>Bold width estimate (CJK/full-width ≈ 1 em, ASCII ≈ 0.6 em); dynamic-font CalcSize can
        /// under-report glyphs that have not been rasterised yet.</summary>
        static float TextWidthEstimate(string s, float size)
        {
            float w = 0;
            foreach (char ch in s) w += ch < 0x2E80 ? 0.6f : 1.02f;
            return w * size;
        }

        void DrawHub()
        {
            float W = Gui.W, H = Gui.H, cx = W / 2;
            Gui.Label("萌友遊戲大廳", cx, 52, 54, Js.Hex("#fff6c8"), 0.5f, 0.5f, Js.Hex("#1b2a6b"));
            Gui.Label("八位萌友・十二款遊戲", cx, 100, 22, Color.white, 0.5f, 0.5f, Color.black);
            var all = Catalog.All;
            const int cols = 4;
            float cw = Mathf.Min(290, (W - 80) / cols - 12), ch = 92, gap = 12;
            float x0 = cx - (cols * cw + (cols - 1) * gap) / 2, y0 = H - 3 * (ch + gap) - 26;
            for (int i = 0; i < all.Count; i++)
            {
                var g = all[i];
                var r = new Rect(x0 + (i % cols) * (cw + gap), y0 + (i / cols) * (ch + gap), cw, ch);
                bool hover = Gui.Hover(r);
                if (hover) focus = i;
                bool sel = focus == i;
                var col = Js.Hex(g.Color);
                Gui.Panel(r, sel ? new Color(col.r * 0.6f, col.g * 0.6f, col.b * 0.6f, 0.95f) : new Color(0.05f, 0.08f, 0.2f, 0.85f), sel ? Js.Hex("#ffe066") : new Color(1, 1, 1, 0.3f));
                Gui.Circle(r.x + 30, r.y + 30, 18, col);
                Gui.Label((i + 1).ToString(), r.x + 30, r.y + 30, 20, Color.white, 0.5f, 0.5f, Color.black);
                // Shrink long titles to the card width on narrow screens (指南 sits in the bottom-right corner).
                float titleSize = 22, titleRoom = r.width - 56 - 8;
                while (titleSize > 11 && Mathf.Max(Gui.Measure(g.Title, titleSize).x, TextWidthEstimate(g.Title, titleSize)) > titleRoom) titleSize--;
                Gui.Label(g.Title, r.x + 56, r.y + 28, titleSize, sel ? Js.Hex("#ffe066") : Color.white, 0f, 0.5f, Color.black);
                if (sel)
                {
                    // Selected card: genre moves up and the blurb (≤ 2 lines) fills the rest of the card.
                    Gui.Label(g.Genre + (g.Type == null ? "（製作中）" : ""), r.x + 56, r.y + 50, 14, Js.Hex("#9fd2ff"), 0f, 0.5f);
                    float bw = r.width - 20 - 56, bs = 13;
                    while (bs > 9 && Mathf.Ceil(Gui.Measure(g.Blurb, bs).x / bw) * bs * 1.3f > r.height - 62) bs--;
                    Gui.Label(g.Blurb, r.x + 10, r.y + 61, bs, new Color(1, 1, 1, 0.85f), 0f, 0f, null, bw);
                }
                else Gui.Label(g.Genre + (g.Type == null ? "（製作中）" : ""), r.x + 56, r.y + 56, 16, Js.Hex("#9fd2ff"), 0f, 0.5f);
                var gb = new Rect(r.xMax - 56, r.yMax - 32, 48, 24);
                if (GuideBook.Has(i + 1) && Gui.Button(gb, "指南", 13, Js.Hex("#2a6fdb"))) GuideBook.Open(i + 1);
                else if (Gui.Clicked(r)) Launch(i + 1);
            }
            Gui.Label("方向鍵選擇　Enter / 點擊開始　G 遊戲指南／攻略　數字鍵 1-9 快速進入", cx, H - 12, 15, new Color(1, 1, 1, 0.7f));
        }
    }
}
