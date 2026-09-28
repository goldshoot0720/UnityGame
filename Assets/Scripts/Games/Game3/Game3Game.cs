// 萌友卡丁車 GP — real 3D presentation of the Game3 race sim: the pseudo-3D track pieces are
// turned into a closed 3D ribbon (curves → heading, hills → height), with a chase camera.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MoeGames.Game3
{
    [MoeGame(3)]
    public class Game3Game : MiniGame
    {
        public override TouchLayout Touch => new TouchLayout { HorizontalOnly = true }.Button("油門", KeyCode.UpArrow).Button("煞車", KeyCode.DownArrow).Button("漂移", KeyCode.LeftShift).Button("道具", KeyCode.Space);

        protected override string Backdrop => "sky_ocean";

        enum Scr { Title, Select, Race, Over }

        const float U = 0.004f;            // world metres per track unit (road half-width 1000 → 4 m)
        const float TurnPerCurve = 0.0075f; // radians of heading per curve unit per segment

        Scr scr;
        float t;
        string racer = "whale";
        int trackIdx;
        CastPicker picker;
        Chibi[] lineup;
        RaceSim sim;
        List<RaceResultRow> result;
        int resultPlace;
        double resultTime, resultBest;

        // track geometry (per segment boundary)
        Vector3[] centers;
        float[] headings;
        readonly Dictionary<Kart, Transform> kartViews = new Dictionary<Kart, Transform>();
        readonly List<Transform> boxViews = new List<Transform>();
        readonly List<Transform> hazardViews = new List<Transform>();
        readonly List<Transform> shotViews = new List<Transform>();
        Transform dyn;

        protected override void Begin()
        {
            Sfx.Define("box", Tone.Melody("C6 E6 G6", 0.04f, Wave.Square, 0.25f));
            Sfx.Define("boost", Tone.Beep(200, 0.4f, Wave.Sawtooth, 0.25f, 900));
            Sfx.Define("spin", Tone.Beep(700, 0.5f, Wave.Square, 0.3f, 120));
            Sfx.Define("beep", Tone.Beep(660, 0.15f, Wave.Square, 0.3f));
            Sfx.Define("go", Tone.Beep(1320, 0.4f, Wave.Square, 0.3f));
            Sfx.Define("bump", Tone.Noise(0.12f, 0.4f, FilterType.Lowpass, 500, -1, false));
            Sfx.Define("lap", Tone.Melody("E5 A5", 0.08f, Wave.Square, 0.3f));
            Sfx.Define("finish", Tone.Melody("C5 E5 G5 C6:3", 0.1f, Wave.Square, 0.35f));
            Sfx.Define("star", Tone.Melody("C6 D6 E6 G6 E6 G6", 0.06f, Wave.Square, 0.25f));
            Sfx.Define("bubble", Tone.Beep(400, 0.2f, Wave.Sine, 0.3f, 900));
            Rig.Background(Js.Hex("#1a1030"));
            Sfx.MusicByName("music", 0.3f);
            GoTitle();
        }

        // ── screens ──

        void GoTitle()
        {
            scr = Scr.Title;
            lineup = ShowLineup(Cast.Ids, "#1b1040", "#ffe066");
        }

        void GoSelect()
        {
            scr = Scr.Select;
            var ids = Data.RACERS.Select(r => r.Id).ToList();
            picker = new CastPicker(ids, 1);
            picker.Picks.Add(racer);
            picker.Cursor = ids.IndexOf(racer);
            lineup = ShowLineup(ids, "#1b1040", "#ffe066", 1.35f, 1.4f);
        }

        void GoRace()
        {
            scr = Scr.Race;
            sim = new RaceSim(trackIdx, racer);
            sim.Sound += n => Sfx.Play(n);
            sim.Shake += d => Rig.Shake(0.2f, d);
            sim.Done += OnDone;
            BuildTrack();
        }

        void OnDone(List<RaceResultRow> order, int place, double time)
        {
            string key = $"moekart-best-{trackIdx}";
            double prev = PlayerPrefs.GetFloat(key, 0);
            if (prev <= 0 || time < prev) PlayerPrefs.SetFloat(key, (float)time);
            result = order;
            resultPlace = place;
            resultTime = time;
            resultBest = prev > 0 && prev < time ? prev : time;
            scr = Scr.Over;
            Rig.Background(Js.Hex("#1a1030"));
            RenderSettings.fog = false;
            lineup = ShowLineup(order.Take(3).Select(r => r.Id).ToList(), "#1b1040", "#ffe066", 1.6f, 1.6f);
            // Podium heights: 2nd, 1st, 3rd from left.
            if (lineup.Length >= 3)
            {
                var p = new[] { lineup[1], lineup[0], lineup[2] };
                float[] h = { 0.5f, 0.9f, 0.3f };
                for (int i = 0; i < 3; i++)
                {
                    var pos = new Vector3((i - 1) * 1.6f, 0, 0);
                    Prim.Box(World, pos + Vector3.up * h[i] / 2, new Vector3(1.4f, h[i], 1.2f), Js.Hex(i == 1 ? "#ffd23f" : i == 0 ? "#c0c8d8" : "#d08a4a"));
                    p[i].transform.localPosition = pos + Vector3.up * h[i];
                }
            }
            bool podium = place < 3;
            if (podium) Sfx.Notes("C5 E5 G5 C6 G5 C6:3", 0.1f, Wave.Square, 0.4f);
            else Sfx.Notes("G4 F4 E4 D4 C4:3", 0.14f, Wave.Triangle, 0.4f);
        }

        // ── track geometry ──

        void BuildGeometry(Road road)
        {
            int n = road.Count;
            var raw = new float[n + 1];
            for (int i = 0; i < n; i++) raw[i + 1] = raw[i] + (float)road.Curve[i] * TurnPerCurve;
            float total = raw[n];
            float turns = total >= 0 ? 1 : -1;
            float bias = (turns * Mathf.PI * 2 - total) / n;
            headings = new float[n + 1];
            centers = new Vector3[n + 1];
            float seg = (float)Data.SEGMENT * U;
            for (int i = 0; i <= n; i++) headings[i] = raw[i] + bias * i;
            for (int i = 0; i < n; i++)
            {
                float h = (headings[i] + headings[i + 1]) / 2;
                centers[i + 1] = centers[i] + new Vector3(Mathf.Sin(h), 0, Mathf.Cos(h)) * seg;
            }
            // Close the loop: spread the end-point error along the lap.
            var err = centers[n] - centers[0];
            for (int i = 0; i <= n; i++)
            {
                centers[i] -= err * ((float)i / n);
                centers[i].y = (float)road.Height[i % n] * U;
            }
        }

        /// <summary>Track space (z along the road, x in half-widths) → world position; also returns the forward direction.</summary>
        Vector3 TrackPos(double z, double x, out Vector3 fwd)
        {
            int n = sim.Road.Count;
            double f = sim.Wrap(z) / Data.SEGMENT;
            int i = Mathf.Clamp((int)f, 0, n - 1);
            float k = (float)(f - i);
            var c = Vector3.Lerp(centers[i], centers[i + 1], k);
            float h = Mathf.Lerp(headings[i], headings[i + 1], k);
            fwd = new Vector3(Mathf.Sin(h), 0, Mathf.Cos(h));
            var right = new Vector3(Mathf.Cos(h), 0, -Mathf.Sin(h));
            return c + right * (float)(x * Data.ROAD_W) * U;
        }

        Vector3 TrackPos(double z, double x) => TrackPos(z, x, out _);

        void BuildTrack()
        {
            ClearWorld();
            kartViews.Clear();
            boxViews.Clear();
            hazardViews.Clear();
            shotViews.Clear();
            var tr = sim.Tr;
            BuildGeometry(sim.Road);
            Rig.Background(Js.Hex(tr.Fog));
            RenderSettings.fog = true;
            RenderSettings.fogColor = Js.Hex(tr.Fog);
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 60;
            RenderSettings.fogEndDistance = 220;
            if (tr.Id == "neon")
            {
                App.I.Sun.intensity = 0.5f;
                RenderSettings.ambientLight = new Color(0.35f, 0.3f, 0.5f);
            }

            // Ribbon mesh with one sub-mesh per colour.
            var mats = new List<Material>();
            var subs = new List<List<int>>();
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var roadMat = Mats.Generated(tr.Id + "_road", Scope, Vector2.one);
            int roadSub = -1;
            int Sub(string hex)
            {
                var m = Mats.Get(hex);
                int idx = mats.IndexOf(m);
                if (idx >= 0) return idx;
                mats.Add(m);
                subs.Add(new List<int>());
                return mats.Count - 1;
            }
            void Quad(int i, float x0, float x1, int sub, float lift = 0)
            {
                var a = Edge(i, x0, lift); var b = Edge(i, x1, lift); var c = Edge(i + 1, x1, lift); var d = Edge(i + 1, x0, lift);
                int v = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
                // u across the strip, v along the lap (one texture repeat per 4 segments).
                float v0 = i * 0.25f, v1 = (i + 1) * 0.25f;
                uvs.Add(new Vector2(0, v0)); uvs.Add(new Vector2(1, v0)); uvs.Add(new Vector2(1, v1)); uvs.Add(new Vector2(0, v1));
                var l = subs[sub];
                l.Add(v); l.Add(v + 3); l.Add(v + 2); l.Add(v); l.Add(v + 2); l.Add(v + 1);
            }
            int n = sim.Road.Count;
            for (int i = 0; i < n; i++)
            {
                int alt = (i / 3) % 2;
                Quad(i, -7f, -1.14f, Sub(tr.Grass[alt]), -0.02f);
                Quad(i, 1.14f, 7f, Sub(tr.Grass[alt]), -0.02f);
                Quad(i, -1.14f, -1f, Sub(tr.Rumble[alt]));
                Quad(i, 1f, 1.14f, Sub(tr.Rumble[alt]));
                if (roadMat && i > 1)
                {
                    if (roadSub < 0) { mats.Add(roadMat); subs.Add(new List<int>()); roadSub = mats.Count - 1; }
                    Quad(i, -1f, 1f, roadSub);
                }
                else Quad(i, -1f, 1f, Sub(i == 0 ? "#ffffff" : i == 1 ? "#222222" : tr.Road[alt]));
                if (alt == 0 && i > 1)
                {
                    Quad(i, -1f / 3 - 0.025f, -1f / 3 + 0.025f, Sub(tr.Lane), 0.01f);
                    Quad(i, 1f / 3 - 0.025f, 1f / 3 + 0.025f, Sub(tr.Lane), 0.01f);
                }
            }
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, name = "track" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = subs.Count;
            for (int s = 0; s < subs.Count; s++) mesh.SetTriangles(subs[s], s);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject("Track");
            go.transform.SetParent(World, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mats.ToArray();
            // A big ground plane under everything.
            var bounds = mesh.bounds;
            Prim.Box(World, new Vector3(bounds.center.x, bounds.min.y - 0.3f, bounds.center.z), new Vector3(bounds.size.x + 200, 0.2f, bounds.size.z + 200), Js.Hex(tr.Grass[1]));

            foreach (var (z, x) in sim.Props) BuildProp(tr.Prop, TrackPos(z, x));
            dyn = Prim.Empty("Dynamic", World).transform;
            foreach (var b in sim.Boxes)
            {
                var art = Art.Standee(dyn, "itembox", TrackPos(b.Z, b.X), 1.1f);
                if (art) { boxViews.Add(art.transform); continue; }
                var bx = Prim.Box(dyn, TrackPos(b.Z, b.X) + Vector3.up * 0.6f, Vector3.one * 0.7f, Js.Hex("#ff9ad8"));
                Prim.SetColor(bx, Js.Hex("#ff9ad8"), true);
                Prim.Box(bx.transform, Vector3.zero, Vector3.one * 1.08f, Js.Hex("#9ad8ff")).transform.localScale = new Vector3(1.08f, 0.3f, 1.08f);
                boxViews.Add(bx.transform);
            }
            foreach (var k in sim.Karts) kartViews[k] = BuildKart(k);
        }

        Vector3 Edge(int i, float x, float lift)
        {
            float h = headings[i];
            var right = new Vector3(Mathf.Cos(h), 0, -Mathf.Sin(h));
            return centers[i] + right * x * (float)Data.ROAD_W * U + Vector3.up * lift;
        }

        void BuildProp(string kind, Vector3 p)
        {
            float ph = kind == "palm" ? 6f : kind == "snowman" ? 3.2f : 5f;
            if (Art.Standee(World, kind, p, ph)) return;
            switch (kind)
            {
                case "palm":
                    for (int i = 0; i < 5; i++)
                    {
                        var seg = Prim.Cyl(World, p + new Vector3(i * 0.08f, 0.5f + i * 0.9f, 0), 0.35f - i * 0.03f, 0.95f, Js.Hex("#9a6a3a"));
                        seg.transform.localRotation = Quaternion.Euler(0, 0, -4 * i);
                    }
                    for (int i = 0; i < 6; i++)
                    {
                        var leaf = Prim.Box(World, p + new Vector3(0.4f, 4.8f, 0), new Vector3(3f, 0.1f, 0.6f), Js.Hex("#3a9a3a"));
                        leaf.transform.localRotation = Quaternion.Euler(0, i * 60, -20);
                    }
                    break;
                case "snowman":
                    Prim.Sphere(World, p + Vector3.up * 0.7f, 1.4f, Color.white);
                    Prim.Sphere(World, p + Vector3.up * 1.8f, 1f, Color.white);
                    Prim.Sphere(World, p + Vector3.up * 2.55f, 0.7f, Color.white);
                    Prim.Cyl(World, p + Vector3.up * 3f, 0.55f, 0.5f, Js.Hex("#222233"));
                    Prim.Box(World, p + new Vector3(0, 2.55f, -0.38f), new Vector3(0.1f, 0.1f, 0.3f), Js.Hex("#ff8a2a"));
                    break;
                default:
                    Prim.Cyl(World, p + Vector3.up * 2.2f, 0.15f, 4.4f, Js.Hex("#555577"));
                    var bulb = Prim.Sphere(World, p + Vector3.up * 4.5f, 0.6f, Js.Hex("#ffe066"));
                    Prim.SetColor(bulb, Js.Hex(Random.value < 0.5f ? "#ff4fd8" : "#39e6ff"), true);
                    break;
            }
        }

        Transform BuildKart(Kart k)
        {
            var root = Prim.Empty("kart_" + k.R.Id, dyn).transform;
            var col = Js.Hex(k.R.Color);
            Prim.Box(root, new Vector3(0, 0.3f, 0), new Vector3(1.2f, 0.3f, 1.8f), col);
            Prim.Box(root, new Vector3(0, 0.42f, 0.75f), new Vector3(1.0f, 0.22f, 0.4f), Color.Lerp(col, Color.white, 0.4f));
            Prim.Box(root, new Vector3(0, 0.55f, -0.85f), new Vector3(1.3f, 0.08f, 0.3f), Color.Lerp(col, Color.black, 0.4f));
            foreach (var wx in new[] { -0.62f, 0.62f })
                foreach (var wz in new[] { -0.6f, 0.62f })
                {
                    var w = Prim.Cyl(root, new Vector3(wx, 0.22f, wz), 0.44f, 0.22f, Js.Hex("#222222"));
                    w.transform.localRotation = Quaternion.Euler(0, 0, 90);
                }
            var driver = SpawnChar(k.R.Id, new Vector3(0, 0.35f, -0.15f), 1.05f, root);
            driver.SetLoop(Chibi.Loop.None);
            return root;
        }

        // ── update ──

        void Update()
        {
            float dt = Time.deltaTime;
            t += dt;
            switch (scr)
            {
                case Scr.Title:
                    if (In.Back) { ExitToHub(); return; }
                    if (In.Confirm || In.MouseDown(0)) { Sfx.Notes("C5 E5 G5 C6:2", 0.07f, Wave.Square, 0.4f); GoSelect(); }
                    break;
                case Scr.Select:
                    if (In.Back) { GoTitle(); return; }
                    if (In.UpDown) trackIdx = (trackIdx + Data.TRACKS.Length - 1) % Data.TRACKS.Length;
                    if (In.DownDown) trackIdx = (trackIdx + 1) % Data.TRACKS.Length;
                    if (picker.UpdateKeys(lineup) || (In.Down(KeyCode.Return) && picker.Ready)) StartRace();
                    break;
                case Scr.Race:
                    if (In.Back) { RenderSettings.fog = false; App.I.ResetLight(); GoTitle(); return; }
                    var input = new DriveInput
                    {
                        Steer = (In.RightHeld ? 1 : 0) - (In.LeftHeld ? 1 : 0),
                        Gas = In.Held(KeyCode.UpArrow, KeyCode.W, KeyCode.Z),
                        Brake = In.Held(KeyCode.DownArrow, KeyCode.S),
                        Drift = In.Held(KeyCode.LeftShift, KeyCode.RightShift, KeyCode.C),
                        ItemPressed = In.Down(KeyCode.Space, KeyCode.X),
                    };
                    sim.Update(dt, input);
                    if (scr == Scr.Race) SyncViews(dt, input.Steer);
                    break;
                case Scr.Over:
                    if (In.Confirm) { App.I.ResetLight(); GoRace(); }
                    else if (In.Back) GoSelect();
                    if (lineup != null && Mathf.Repeat(t, 0.8f) < dt && lineup.Length > 0 && lineup[0]) lineup[0].Act("win", 0.7f);
                    break;
            }
        }

        void StartRace()
        {
            if (picker.Picks.Count > 0) racer = picker.Picks[0];
            Sfx.Notes("G4 C5 E5 G5:2", 0.07f, Wave.Square, 0.4f);
            GoRace();
        }

        void SyncViews(float dt, int steer)
        {
            foreach (var kv in kartViews)
            {
                var k = kv.Key;
                var pos = TrackPos(k.Z, k.X, out var fwd);
                var tr = kv.Value;
                tr.localPosition = pos;
                float yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
                if (k.Spin > 0) yaw += (float)(k.Spin * 720);
                else if (k.You) yaw += steer * 6 + k.DriftDir * 18;
                tr.localRotation = Quaternion.Euler(0, yaw, 0);
                float bounce = k.Speed > 100 ? Mathf.Sin(t * 30 + k.R.Id.Length) * 0.02f : 0;
                tr.localPosition += Vector3.up * bounce;
                if (k.Star > 0) Prim.SetColor(tr.GetChild(0).gameObject, Js.Hex(Mathf.FloorToInt(t * 12) % 2 == 1 ? "#ffb3f0" : "#b3fff0"), true);
                else Prim.SetColor(tr.GetChild(0).gameObject, Js.Hex(k.R.Color));
                if (k.Boost > 0 && Random.value < 0.6f) Fx.Burst(tr.position - fwd * 1.1f + Vector3.up * 0.4f, 2, Js.Hex("#ff9a3c"), 1.5f, 0.25f, 0.12f, 0);
                if (k.You && k.DriftDir != 0 && k.Drift > 0.4)
                {
                    var c = k.Drift > 1.6 ? Js.Hex("#ff9a3c") : k.Drift > 0.8 ? Js.Hex("#5cc8ff") : Color.white;
                    var right = Vector3.Cross(Vector3.up, fwd);
                    Fx.Burst(tr.position - fwd * 0.8f + right * 0.6f + Vector3.up * 0.1f, 1, c, 2f, 0.25f, 0.08f);
                    Fx.Burst(tr.position - fwd * 0.8f - right * 0.6f + Vector3.up * 0.1f, 1, c, 2f, 0.25f, 0.08f);
                }
            }
            for (int i = 0; i < sim.Boxes.Count; i++)
            {
                var b = sim.Boxes[i];
                var v = boxViews[i];
                v.gameObject.SetActive(b.Respawn <= 0);
                v.localPosition = TrackPos(b.Z, b.X) + Vector3.up * (0.8f + Mathf.Sin(t * 4) * 0.15f);
                if (!v.GetComponent<Billboard>()) v.localRotation = Quaternion.Euler(20, t * 90, 20);
            }
            SyncList(hazardViews, sim.Hazards.Count, () => { var a = Art.Standee(dyn, "banana", Vector3.zero, 0.8f); if (a) return a.transform; var g = Prim.Sphere(dyn, Vector3.zero, 0.5f, Js.Hex("#ffe066")); g.transform.localScale = new Vector3(0.6f, 0.25f, 0.4f); return g.transform; });
            for (int i = 0; i < sim.Hazards.Count; i++) hazardViews[i].localPosition = TrackPos(sim.Hazards[i].Z, sim.Hazards[i].X) + Vector3.up * 0.12f;
            SyncList(shotViews, sim.Shots.Count, () => { var g = Prim.Sphere(dyn, Vector3.zero, 0.9f, Js.Hex("#9fe3ff")); Prim.SetColor(g, Js.Hex("#9fe3ff"), true); return g.transform; });
            for (int i = 0; i < sim.Shots.Count; i++) shotViews[i].localPosition = TrackPos(sim.Shots[i].Z, sim.Shots[i].X) + Vector3.up * 0.6f;

            // Chase camera.
            var me = sim.Me;
            var mp = TrackPos(me.Z, me.X, out var mf);
            var camPos = TrackPos(me.Z - 1400, me.X * 0.6) + Vector3.up * 2.6f;
            var look = TrackPos(me.Z + 1600, me.X * 0.8) + Vector3.up * 0.9f;
            camPos.y = Mathf.Max(camPos.y, mp.y + 1.8f);
            Rig.Follow(camPos, look, 8f, dt);
            Cam.fieldOfView = Mathf.Lerp(Cam.fieldOfView, me.Boost > 0 ? 70 : 60, dt * 4);
        }

        static void SyncList(List<Transform> views, int count, System.Func<Transform> make)
        {
            while (views.Count < count) views.Add(make());
            while (views.Count > count) { Destroy(views[views.Count - 1].gameObject); views.RemoveAt(views.Count - 1); }
        }

        // ── GUI ──

        void OnGUI()
        {
            Gui.Begin();
            float W = Gui.W, H = Gui.H, cx = W / 2;
            switch (scr)
            {
                case Scr.Title:
                    Gui.TitleCard("萌友卡丁車 GP", "八位萌友　三圈決勝的道具賽車", null, t, Js.Hex("#ffe066"));
                    break;
                case Scr.Select:
                    Gui.Label("選擇車手與賽道", cx, 40, 38, Color.white, 0.5f, 0.5f, Js.Hex("#1b1040"));
                    picker.Draw(Cam, lineup, 96, (r, i) =>
                    {
                        var rc = Data.RACERS[i];
                        Gui.Label(rc.Name, r.center.x, r.y + 16, 18, Color.white);
                        CastPicker.Stat(r, r.y + 40, "極速", (float)rc.Speed - 0.8f, 0.3f);
                        CastPicker.Stat(r, r.y + 58, "加速", (float)rc.Accel - 0.8f, 0.35f);
                        CastPicker.Stat(r, r.y + 76, "操控", (float)rc.Handling - 0.8f, 0.35f);
                    });
                    if (picker.Picks.Count > 0) racer = picker.Picks[0];
                    for (int i = 0; i < Data.TRACKS.Length; i++)
                    {
                        var tr = Data.TRACKS[i];
                        var r = new Rect(cx - 390 + i * 265, 80, 250, 70);
                        if (Gui.Button(r, "", 20, Js.Hex(tr.Road[0], 0.95f), null, trackIdx == i)) trackIdx = i;
                        Gui.Label(tr.Name, r.center.x, r.y + 24, 22, Color.white, 0.5f, 0.5f, Color.black);
                        Gui.Label(tr.Subtitle, r.center.x, r.y + 50, 14, Js.Hex("#dddddd"));
                        float best = PlayerPrefs.GetFloat($"moekart-best-{i}", 0);
                        if (best > 0) Gui.Label($"最佳 {Data.FmtTime(best)}", r.center.x, r.yMax + 12, 13, Js.Hex("#ffe066"));
                    }
                    if (Gui.Button(new Rect(cx - 150, H - 70, 300, 56), "出發！", 28, Js.Hex("#d9452b"), Js.Hex("#ff6a47"))) StartRace();
                    Gui.Label("← → 選車手　↑ ↓ 選賽道　Enter 出發　Esc 返回", cx, H - 8, 15, Js.Hex("#dddddd"), 0.5f, 1f);
                    break;
                case Scr.Race: DrawHud(W, H); break;
                case Scr.Over: DrawOver(W, H); break;
            }
        }

        void DrawHud(float W, float H)
        {
            var me = sim.Me;
            int pl = sim.Place(me) + 1;
            Gui.Panel(new Rect(16, 16, 210, 118));
            Gui.Label($"第 {pl} 名", 121, 50, 44, pl == 1 ? Js.Hex("#ffe066") : Color.white, 0.5f, 0.5f, Js.Hex("#1b1040"));
            Gui.Label($"圈數 {Mathf.Min(Data.LAPS, Mathf.Max(1, (int)System.Math.Floor(me.Total / sim.Len) + 1))} / {Data.LAPS}", 121, 102, 22, Js.Hex("#9fe3ff"));
            Gui.Panel(new Rect(W - 236, 16, 220, 118));
            Gui.Label(Data.FmtTime(sim.T), W - 126, 44, 30, Color.white);
            Gui.Label($"{Js.RoundInt(me.Speed * Data.KMH)} km/h", W - 126, 88, 28, me.Boost > 0 ? Js.Hex("#ff9a3c") : Js.Hex("#ffe066"));
            float best = PlayerPrefs.GetFloat($"moekart-best-{trackIdx}", 0);
            if (best > 0) Gui.Label($"最佳 {Data.FmtTime(best)}", W - 126, 118, 16, Js.Hex("#cccccc"));
            // Item slot.
            float ix = W / 2 - 56;
            var itemCol = me.Item != ItemId.None ? Js.Hex(Data.ITEMS[me.Item].color) : new Color(1, 1, 1, 0.33f);
            Gui.Panel(new Rect(ix, 16, 112, 112), Js.Hex("#0d1433", 0.8f), itemCol);
            if (me.Item != ItemId.None)
            {
                Gui.Circle(W / 2, 64, 30, itemCol);
                Gui.Label(Data.ITEMS[me.Item].name.Substring(0, 1), W / 2, 64, 30, Color.white, 0.5f, 0.5f, Color.black);
                Gui.Label(Data.ITEMS[me.Item].name, W / 2, 114, 17, Color.white);
            }
            else Gui.Label("道具", W / 2, 72, 20, new Color(1, 1, 1, 0.33f));
            // Progress strip.
            float px = 30, pw = W - 60, py = H - 16;
            Gui.Rect(px, py - 3, pw, 6, new Color(0, 0, 0, 0.4f));
            foreach (var k in sim.Karts)
            {
                float f = Mathf.Clamp01((float)(k.Total / (Data.LAPS * sim.Len)));
                Gui.Circle(px + pw * f, py, k.You ? 9 : 6, k.You ? Js.Hex("#ffe066") : Js.Hex(k.R.Color));
            }
            // Drift charge.
            if (me.DriftDir != 0)
            {
                var c = me.Drift > 1.6 ? Js.Hex("#ff9a3c") : me.Drift > 0.8 ? Js.Hex("#5cc8ff") : Color.white;
                Gui.Rect(W / 2 - 60, H - 150, 120, 10, new Color(0, 0, 0, 0.53f));
                Gui.Rect(W / 2 - 60, H - 150, 120 * Mathf.Clamp01((float)me.Drift / 1.6f), 10, c);
            }
            if (sim.Count > 0)
            {
                int nn = (int)System.Math.Ceiling(sim.Count - 0.6);
                Gui.Label(nn > 0 ? nn.ToString() : "GO!", W / 2, H * 0.36f, 120, nn > 0 ? Color.white : Js.Hex("#5cffb0"), 0.5f, 0.5f, Js.Hex("#1b1040"));
                Gui.Label("↑ 油門　← → 轉向　Shift 漂移集氣　Space 使用道具", W / 2, H * 0.52f, 22, Color.white, 0.5f, 0.5f, Color.black);
                Gui.Label("GO 的瞬間按住油門 = 火箭起步", W / 2, H * 0.57f, 18, Js.Hex("#ffe066"), 0.5f, 0.5f, Color.black);
            }
            else if (sim.Count > -0.8)
            {
                var c = Js.Hex("#5cffb0");
                c.a = Mathf.Clamp01((float)(sim.Count + 0.8) / 0.8f);
                Gui.Label("GO!", W / 2, H * 0.36f, 120, c, 0.5f, 0.5f, Js.Hex("#1b1040"));
            }
            if (sim.MsgT > 0)
            {
                var c = Color.white;
                c.a = Mathf.Clamp01((float)sim.MsgT * 2);
                Gui.Label(sim.Msg, W / 2, H * 0.26f, 40, c, 0.5f, 0.5f, Js.Hex("#1b1040"));
            }
        }

        void DrawOver(float W, float H)
        {
            float cx = W / 2;
            Gui.Label(resultPlace == 0 ? "冠軍！" : $"第 {resultPlace + 1} 名", cx, 70, 72, resultPlace < 3 ? Js.Hex("#ffe066") : Color.white, 0.5f, 0.5f, Js.Hex("#1b1040"));
            Gui.Label($"完賽時間 {Data.FmtTime(resultTime)}　最佳 {Data.FmtTime(resultBest)}", cx, 130, 22, Color.white, 0.5f, 0.5f, Color.black);
            var r = new Rect(W - 330, 170, 300, 30 * result.Count + 20);
            Gui.Panel(r);
            for (int i = 0; i < result.Count; i++)
            {
                var row = result[i];
                var col = row.You ? Js.Hex("#ffe066") : Color.white;
                Gui.Label($"{i + 1}.", r.x + 20, r.y + 24 + i * 30, 18, col, 0f, 0.5f);
                Gui.Label(Cast.Name(row.Id) + (row.You ? "（你）" : ""), r.x + 56, r.y + 24 + i * 30, 18, col, 0f, 0.5f);
                Gui.Label(Data.FmtTime(row.Time), r.xMax - 20, r.y + 24 + i * 30, 18, col, 1f, 0.5f);
            }
            if (Gui.Button(new Rect(cx - 250, H - 100, 230, 62), "再跑一場", 28, Js.Hex("#d9452b"), Js.Hex("#ff6a47"))) { App.I.ResetLight(); GoRace(); }
            if (Gui.Button(new Rect(cx + 20, H - 100, 230, 62), "重選車手", 28)) GoSelect();
        }
    }
}
