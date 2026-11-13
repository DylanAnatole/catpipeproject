using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Spine.Unity;

namespace CatPipe
{
    // The "New feature" demo: a tiny board built from the real game pieces and cats, far away from the level, filmed by
    // its own camera into a RenderTexture that the intro card shows. Each mechanic has a short script that loops:
    // the hand drags / taps, the path lights up, the cat hops home, so the player sees what the mechanic does.
    public class DemoStage : MonoBehaviour
    {
        static readonly Vector3 Origin = new Vector3(1000f, 0f, 0f);
        static readonly Vector3 Feet = new Vector3(0, -0.36f);

        GameController gc;
        SkeletonDataAsset catData, handData;
        Camera cam;
        public RenderTexture Texture { get; private set; }
        Transform root;
        SkeletonAnimation hand;
        int w, h;
        Coroutine loop;
        GameObject display; // the RawImage in the card: the demo runs while it is visible

        class DemoCat { public SkeletonAnimation anim; public Transform tr; public Vector2Int cell; public SpriteRenderer pad, badge; public TextMesh text; }

        public void Init(GameController gc, SkeletonDataAsset catData, SkeletonDataAsset handData)
        {
            this.gc = gc; this.catData = catData; this.handData = handData;
            Texture = new RenderTexture(720, 540, 16) { name = "DemoRT" };
            cam = new GameObject("DemoCamera").AddComponent<Camera>();
            cam.transform.SetParent(transform, false);
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.87f, 0.70f, 0.50f);
            cam.targetTexture = Texture;
            cam.enabled = false;
        }

        public void Play(string feat, GameObject shownOn)
        {
            display = shownOn;
            if (loop != null) StopCoroutine(loop);
            loop = StartCoroutine(Loop(feat));
        }

        IEnumerator Loop(string feat)
        {
            cam.enabled = true;
            while (display && display.activeInHierarchy)
            {
                Clear();
                yield return Script(feat);
                yield return new WaitForSeconds(1.3f);
            }
            Clear();
            cam.enabled = false;
            loop = null;
        }

        void Clear()
        {
            StopAllCoroutinesExceptLoop();
            if (root) Destroy(root.gameObject);
            root = new GameObject("DemoRoot").transform;
            root.SetParent(transform, false);
            root.position = Origin;
            hand = null;
        }

        readonly List<Coroutine> running = new List<Coroutine>();
        void StopAllCoroutinesExceptLoop() { foreach (var c in running) if (c != null) StopCoroutine(c); running.Clear(); }
        void Run(IEnumerator e) => running.Add(StartCoroutine(e));

        // ---------------------------------------------------------------- building blocks

        Vector3 Cell(Vector2Int c) => Origin + new Vector3(c.x - (w - 1) / 2f, -(c.y - (h - 1) / 2f));
        Vector3 Cell(int x, int y) => Cell(new Vector2Int(x, y));

        void Board(int bw, int bh)
        {
            w = bw; h = bh;
            float size = Mathf.Max(bh + 1.9f, (bw + 2.3f) * 0.75f);
            cam.orthographicSize = size / 2f;
            cam.transform.position = Origin + new Vector3(0, 0, -10);
            // same wooden board as the level: rim, inner well, dark floor cells
            Layer(new Vector2(w + 0.5f, h + 0.5f), new Vector3(0.08f, -0.14f), new Color(0.25f, 0.13f, 0.05f, 0.45f), -6);
            Layer(new Vector2(w + 0.5f, h + 0.5f), new Vector3(0, -0.06f), new Color(0.42f, 0.23f, 0.12f), -5);
            Layer(new Vector2(w + 0.5f, h + 0.46f), new Vector3(0, 0.02f), new Color(0.72f, 0.45f, 0.26f), -5);
            Layer(new Vector2(w + 0.12f, h + 0.12f), Vector3.zero, new Color(0.30f, 0.18f, 0.11f), -4);
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    GameController.MakeSprite("DemoCell", root, gc.sprBoardCell, 0.95f, -2, Cell(x, y) - Origin).color = new Color(0.46f, 0.30f, 0.20f);
            Step(1);
        }

        void Layer(Vector2 size, Vector3 pos, Color col, int order)
        {
            var sr = GameController.MakeSprite("DemoBoard", root, gc.sprBoardFrame, 1f, order, pos);
            sr.transform.localScale = Vector3.one;
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.size = size;
            sr.color = col;
        }

        // The card's numbered tips light up as the demo reaches them.
        public Action<int> OnStep;
        void Step(int k) => OnStep?.Invoke(k);

        Piece P(int x, int y, string cells = "0,0", string paths = null, Action<PieceJson> cfg = null)
        {
            var j = new PieceJson { at = $"{x},{y}", cells = cells, paths = paths };
            cfg?.Invoke(j);
            var go = new GameObject("DemoPiece");
            go.transform.SetParent(root, false);
            var p = go.AddComponent<Piece>();
            p.Build(j, gc);
            p.anchor = new Vector2Int(x, y);
            go.transform.position = Cell(p.anchor);
            return p;
        }

        void Hole(int color, int x, int y)
        {
            GameController.MakeSprite("DemoHole", root, gc.sprBlocks[Piece.Family(color), 13], 0.9f, 2, Cell(x, y) - Origin);
            GameController.MakeSprite("DemoPaw", root, gc.sprPaw, 0.42f, 3, Cell(x, y) - Origin).color = new Color(1f, 1f, 1f, 0.6f);
        }

        DemoCat Cat(int color, int x, int y, bool pad = true)
        {
            var c = new DemoCat { cell = new Vector2Int(x, y) };
            if (pad) c.pad = GameController.MakeSprite("DemoPad", root, gc.sprPad, 0.95f, 2, Cell(x, y) - Origin + new Vector3(0, -0.12f));
            c.anim = SkeletonAnimation.NewSkeletonAnimationGameObject(catData);
            c.tr = c.anim.transform;
            c.tr.SetParent(root, false);
            c.tr.position = Cell(x, y) + Feet;
            c.tr.localScale = Vector3.one * 0.85f;
            c.anim.Skeleton.SetSkin(GameController.CatSkins[color]);
            c.anim.Skeleton.SetSlotsToSetupPose();
            c.anim.GetComponent<MeshRenderer>().sortingOrder = 100;
            c.anim.AnimationState.SetAnimation(0, "front_idle1", true);
            return c;
        }

        // A small dark badge with a number next to a cat (bed count, frozen count).
        void Badge(DemoCat c, string txt, Color col, Sprite sp = null)
        {
            var at = Cell(c.cell) - Origin + new Vector3(0.36f, -0.3f);
            c.badge = GameController.MakeSprite("DemoBadge", root, sp ? sp : gc.sprDisc, 0.42f, 101, at);
            if (!sp) c.badge.color = col;
            c.text = gc.MakeText("DemoBadgeText", root, at, 102);
            c.text.transform.localScale = Vector3.one * 1.2f;
            c.text.color = sp ? new Color(0.08f, 0.25f, 0.55f) : Color.white;
            c.text.text = txt;
        }

        static void Light(Piece p, int color) { for (int i = 0; i < p.offs.Length; i++) p.SetFlow(i, color, i); }

        // The cat hops cell by cell into its hole (the last cell), then sits.
        IEnumerator Hop(DemoCat c, params Vector2Int[] cells)
        {
            if (c.pad) c.pad.enabled = false;
            var pos = c.tr.position;
            string pose = null;
            for (int i = 0; i < cells.Length; i++)
            {
                var target = Cell(cells[i]) + Feet + (i == cells.Length - 1 ? new Vector3(0, 0.1f) : Vector3.zero);
                var dir = target - pos;
                string want = Mathf.Abs(dir.x) >= Mathf.Abs(dir.y) ? "jump3" : dir.y < 0 ? "jump2" : "back_jump";
                if (want != pose) { pose = want; c.anim.AnimationState.SetAnimation(0, pose, false).TrackTime = pose == "back_jump" ? 0.3f : 0.45f; }
                c.anim.Skeleton.ScaleX = pose == "jump3" && dir.x < 0 ? -1f : 1f;
                bool last = i == cells.Length - 1;
                float dur = last ? 0.3f : 0.2f, height = last ? 0.5f : 0.22f;
                var from = pos;
                for (float k = 0; k < 1f; k += Time.deltaTime / dur)
                {
                    pos = Vector3.Lerp(from, target, k);
                    c.tr.position = pos + new Vector3(0, 4f * height * k * (1f - k));
                    yield return null;
                }
                pos = target;
                c.tr.position = pos;
            }
            c.anim.Skeleton.ScaleX = 1f;
            c.anim.AnimationState.SetAnimation(0, "select_to_sit", false);
            c.anim.AnimationState.AddAnimation(0, "front_idle1", true, 0);
            c.cell = cells[cells.Length - 1];
        }

        // A hop forward that stops and comes back: the way is shut.
        IEnumerator Bounce(DemoCat c, Vector2Int toward)
        {
            var home = c.tr.position;
            var half = Vector3.Lerp(home, Cell(toward) + Feet, 0.45f);
            c.anim.AnimationState.SetAnimation(0, "jump3", false).TrackTime = 0.45f;
            for (float k = 0; k < 1f; k += Time.deltaTime / 0.25f) { c.tr.position = Vector3.Lerp(home, half, k) + new Vector3(0, 0.6f * k * (1f - k)); yield return null; }
            for (float k = 0; k < 1f; k += Time.deltaTime / 0.25f) { c.tr.position = Vector3.Lerp(half, home, k); yield return null; }
            c.tr.position = home;
            c.anim.AnimationState.SetAnimation(0, "front_idle1", true);
        }

        void EnsureHand()
        {
            if (hand) return;
            hand = SkeletonAnimation.NewSkeletonAnimationGameObject(handData);
            hand.name = "DemoHand";
            hand.transform.SetParent(root, false);
            hand.transform.localScale = Vector3.one * 0.32f;
            hand.GetComponent<MeshRenderer>().sortingOrder = 200;
        }

        Vector3 PieceCell(Piece p) => Cell(p.anchor + p.offs[0]);

        // Hand presses the block and drags it to a new anchor.
        IEnumerator Drag(Piece p, int tx, int ty)
        {
            EnsureHand();
            hand.gameObject.SetActive(true);
            hand.transform.position = PieceCell(p);
            hand.AnimationState.SetAnimation(0, "click", false);
            yield return new WaitForSeconds(0.45f);
            p.SetDragging(true);
            var to = new Vector2Int(tx, ty);
            Vector3 a = p.transform.position, b = Cell(to), ha = hand.transform.position, hb = Cell(to + p.offs[0]);
            for (float k = 0; k < 1f; k += Time.deltaTime / 0.55f)
            {
                float s = Mathf.SmoothStep(0, 1, k);
                p.transform.position = Vector3.Lerp(a, b, s);
                hand.transform.position = Vector3.Lerp(ha, hb, s);
                yield return null;
            }
            p.transform.position = b;
            p.anchor = to;
            p.SetDragging(false);
            yield return new WaitForSeconds(0.25f);
            hand.gameObject.SetActive(false);
        }

        // Hand presses the block, but it will not move (shakes red).
        IEnumerator Refuse(Piece p)
        {
            EnsureHand();
            hand.gameObject.SetActive(true);
            hand.transform.position = PieceCell(p);
            hand.AnimationState.SetAnimation(0, "click", false);
            yield return new WaitForSeconds(0.4f);
            p.Shake();
            p.Flash(new Color(1f, 0.35f, 0.35f));
            yield return new WaitForSeconds(0.6f);
            hand.gameObject.SetActive(false);
        }

        IEnumerator Tap(Piece p)
        {
            EnsureHand();
            hand.gameObject.SetActive(true);
            hand.transform.position = PieceCell(p);
            hand.AnimationState.SetAnimation(0, "click", false);
            yield return new WaitForSeconds(0.35f);
            p.Rotate(true);
            yield return new WaitForSeconds(0.45f);
            hand.gameObject.SetActive(false);
        }

        static Vector2Int V(int x, int y) => new Vector2Int(x, y);
        static WaitForSeconds Wait(float s) => new WaitForSeconds(s);

        // ---------------------------------------------------------------- one short story per mechanic

        IEnumerator Script(string feat)
        {
            switch (feat)
            {
                case "cross":
                {
                    Board(3, 3);
                    var plus = P(0, 0, "1,0 0,1 1,1 2,1 1,2");
                    Hole(1, 3, 1); Hole(3, 1, 3);
                    var red = Cat(1, -1, 1); var blue = Cat(3, 1, -1);
                    yield return Wait(0.6f);
                    for (int i = 0; i < 5; i++) if (plus.offs[i].y == 1) plus.SetFlow(i, 1, plus.offs[i].x);
                    yield return Wait(0.4f);
                    yield return Hop(red, V(0, 1), V(1, 1), V(2, 1), V(3, 1));
                    plus.ResetFlow();
                    for (int i = 0; i < 5; i++) if (plus.offs[i].x == 1) plus.SetFlow(i, 3, plus.offs[i].y);
                    yield return Wait(0.4f);
                    Step(2);
                    yield return Hop(blue, V(1, 0), V(1, 1), V(1, 2), V(1, 3));
                    break;
                }
                case "dead":
                {
                    Board(3, 3);
                    var l = P(0, 1, paths: "EW"); var r = P(2, 1, paths: "EW");
                    var crate = P(1, 1, paths: "-"); var road = P(1, 0, paths: "EW");
                    Hole(1, 3, 1); var cat = Cat(1, -1, 1);
                    yield return Wait(0.6f);
                    yield return Drag(crate, 1, 2);
                    Step(2);
                    yield return Drag(road, 1, 1);
                    Light(l, 1); Light(road, 1); Light(r, 1);
                    yield return Wait(0.4f);
                    yield return Hop(cat, V(0, 1), V(1, 1), V(2, 1), V(3, 1));
                    break;
                }
                case "junction":
                {
                    Board(3, 2);
                    P(0, 0, paths: "EW"); var j = P(1, 0, paths: "ESW"); var r = P(2, 0, paths: "EW"); var d = P(1, 1, paths: "NS");
                    Hole(2, 3, 0); var cat = Cat(2, 1, 2);
                    yield return Wait(0.7f);
                    Light(d, 2); j.SetFlow(0, 2, 1); Light(r, 2);
                    yield return Wait(0.5f);
                    Step(2);
                    yield return Hop(cat, V(1, 1), V(1, 0), V(2, 0), V(3, 0));
                    break;
                }
                case "special":
                {
                    Board(3, 2);
                    var a = P(0, 0, paths: "SW"); var b = P(0, 1, paths: "NE"); var m = P(1, 0, paths: "EW"); var e = P(2, 1, paths: "EW");
                    Hole(4, 3, 1); var cat = Cat(4, -1, 0);
                    yield return Wait(0.6f);
                    yield return Drag(m, 1, 1);
                    Step(2);
                    Light(a, 4); Light(b, 4); Light(m, 4); Light(e, 4);
                    yield return Wait(0.4f);
                    yield return Hop(cat, V(0, 0), V(0, 1), V(1, 1), V(2, 1), V(3, 1));
                    break;
                }
                case "rotate":
                {
                    Board(3, 1);
                    var l = P(0, 0, paths: "EW"); var rot = P(1, 0, paths: "NS", cfg: j => j.rotate = true); var r = P(2, 0, paths: "EW");
                    Hole(3, 3, 0); var cat = Cat(3, -1, 0);
                    yield return Wait(0.7f);
                    yield return Tap(rot);
                    Step(2);
                    Light(l, 3); Light(rot, 3); Light(r, 3);
                    yield return Wait(0.4f);
                    yield return Hop(cat, V(0, 0), V(1, 0), V(2, 0), V(3, 0));
                    break;
                }
                case "multicat":
                {
                    Board(3, 3);
                    var l = P(0, 0, paths: "EW"); var j = P(1, 0, paths: "ESW"); var r = P(2, 0, paths: "EW");
                    var d1 = P(1, 1, paths: "NS"); var d2 = P(1, 2, paths: "NS");
                    Hole(5, 3, 0); Hole(5, 1, 3);
                    var second = Cat(5, -1, 0, pad: false); second.tr.gameObject.SetActive(false);
                    var first = Cat(5, -1, 0);
                    Badge(first, "x2", new Color(0.95f, 0.45f, 0.2f));
                    yield return Wait(0.7f);
                    Light(l, 5); j.SetFlow(0, 5, 1); Light(r, 5);
                    yield return Wait(0.4f);
                    first.badge.enabled = false; first.text.text = "";
                    var pad = first.pad; first.pad = null;
                    yield return Hop(first, V(0, 0), V(1, 0), V(2, 0), V(3, 0));
                    Step(2);
                    second.tr.gameObject.SetActive(true);
                    second.anim.AnimationState.SetAnimation(0, "select_to_sit", false);
                    second.anim.AnimationState.AddAnimation(0, "front_idle1", true, 0);
                    yield return Wait(0.6f);
                    r.ResetFlow(); Light(d1, 5); Light(d2, 5);
                    yield return Wait(0.4f);
                    second.pad = pad;
                    yield return Hop(second, V(0, 0), V(1, 0), V(1, 1), V(1, 2), V(1, 3));
                    break;
                }
                case "oneway":
                {
                    Board(3, 2);
                    var ok = P(0, 0, "0,0 1,0 2,0", cfg: j => j.oneway = "E");
                    var no = P(0, 1, "0,0 1,0 2,0", cfg: j => j.oneway = "W");
                    Hole(2, 3, 0); Hole(3, 3, 1);
                    var go = Cat(2, -1, 0); var stuck = Cat(3, -1, 1);
                    yield return Wait(0.7f);
                    Step(2);
                    yield return Bounce(stuck, V(0, 1));
                    no.Flash(new Color(1f, 0.35f, 0.35f));
                    yield return Wait(0.4f);
                    Light(ok, 2);
                    yield return Wait(0.4f);
                    yield return Hop(go, V(0, 0), V(1, 0), V(2, 0), V(3, 0));
                    break;
                }
                case "ice":
                {
                    Board(3, 2);
                    var l = P(0, 0, paths: "EW"); var ice = P(1, 0, paths: "EW", cfg: j => j.ice = 1); var r = P(2, 1, paths: "EW");
                    Hole(8, 3, 0); var cat = Cat(8, -1, 0);
                    yield return Wait(0.6f);
                    yield return Refuse(ice);
                    Step(2);
                    yield return Drag(r, 2, 0);
                    ice.ice = 0; ice.Refresh(false); ice.Flash(new Color(0.75f, 0.95f, 1f));
                    yield return Wait(0.5f);
                    Light(l, 8); Light(ice, 8); Light(r, 8);
                    yield return Wait(0.4f);
                    yield return Hop(cat, V(0, 0), V(1, 0), V(2, 0), V(3, 0));
                    break;
                }
                case "keylock":
                {
                    Board(3, 2);
                    var top = P(0, 0, "0,0 1,0 2,0", cfg: j => j.key = true);
                    var bot = P(0, 1, "0,0 1,0 2,0", cfg: j => j.@lock = true);
                    Hole(2, 3, 0); Hole(3, 3, 1);
                    var red = Cat(2, -1, 0); var blue = Cat(3, -1, 1);
                    yield return Wait(0.6f);
                    yield return Bounce(blue, V(0, 1));
                    Light(top, 2);
                    yield return Wait(0.3f);
                    Run(Hop(red, V(0, 0), V(1, 0), V(2, 0), V(3, 0)));
                    yield return Wait(0.25f);
                    Step(2);
                    top.key = false; top.Refresh(true); bot.Refresh(true); bot.Flash(new Color(1f, 0.95f, 0.5f));
                    yield return Wait(1.2f);
                    Light(bot, 3);
                    yield return Wait(0.4f);
                    yield return Hop(blue, V(0, 1), V(1, 1), V(2, 1), V(3, 1));
                    break;
                }
                case "inner":
                {
                    Board(3, 2);
                    var m = P(1, 1, paths: "EW"); var r = P(2, 0, paths: "EW");
                    Hole(7, 3, 0); var cat = Cat(7, 0, 0);
                    yield return Wait(0.6f);
                    Step(2);
                    yield return Drag(m, 1, 0);
                    Light(m, 7); Light(r, 7);
                    yield return Wait(0.4f);
                    yield return Hop(cat, V(1, 0), V(2, 0), V(3, 0));
                    break;
                }
                case "frozen":
                {
                    Board(3, 2);
                    var top = P(0, 0, "0,0 1,0 2,0"); var bot = P(0, 1, "0,0 1,0 2,0");
                    Hole(4, 3, 0); Hole(3, 3, 1);
                    var pink = Cat(4, -1, 0); var ice = Cat(3, -1, 1);
                    ice.anim.AnimationState.SetAnimation(0, "ice1_idle", true);
                    Badge(ice, "1", Color.white, gc.sprIce);
                    yield return Wait(0.8f);
                    Light(top, 4);
                    yield return Wait(0.3f);
                    yield return Hop(pink, V(0, 0), V(1, 0), V(2, 0), V(3, 0));
                    Step(2);
                    ice.badge.enabled = false; ice.text.text = "";
                    ice.anim.AnimationState.SetAnimation(0, "unfreeze", false);
                    ice.anim.AnimationState.AddAnimation(0, "front_idle1", true, 0);
                    yield return Wait(1.0f);
                    Light(bot, 3);
                    yield return Wait(0.3f);
                    yield return Hop(ice, V(0, 1), V(1, 1), V(2, 1), V(3, 1));
                    break;
                }
                case "color":
                {
                    Board(3, 2);
                    var top = P(0, 0, "0,0 1,0 2,0", cfg: j => j.color = 1);
                    var bot = P(0, 1, "0,0 1,0 2,0", cfg: j => j.color = 1);
                    Hole(1, 3, 0); Hole(3, 3, 1);
                    var red = Cat(1, -1, 0); var blue = Cat(3, -1, 1);
                    yield return Wait(0.6f);
                    Step(2);
                    yield return Bounce(blue, V(0, 1));
                    bot.Flash(new Color(1f, 0.35f, 0.35f));
                    yield return Wait(0.3f);
                    Light(top, 1);
                    yield return Wait(0.4f);
                    yield return Hop(red, V(0, 0), V(1, 0), V(2, 0), V(3, 0));
                    break;
                }
                case "fixed":
                {
                    Board(3, 2);
                    var l = P(0, 0, paths: "EW"); var f = P(1, 0, paths: "EW", cfg: j => j.@fixed = true); var r = P(2, 1, paths: "EW");
                    Hole(6, 3, 0); var cat = Cat(6, -1, 0);
                    yield return Wait(0.6f);
                    yield return Refuse(f);
                    Step(2);
                    yield return Drag(r, 2, 0);
                    Light(l, 6); Light(f, 6); Light(r, 6);
                    yield return Wait(0.4f);
                    yield return Hop(cat, V(0, 0), V(1, 0), V(2, 0), V(3, 0));
                    break;
                }
                case "axis":
                {
                    Board(4, 2);
                    var l = P(0, 0, paths: "EW"); var a = P(1, 1, "0,0 1,0", cfg: j => j.axis = "V"); var r = P(3, 0, paths: "EW");
                    Hole(2, 4, 0); var cat = Cat(2, -1, 0);
                    yield return Wait(0.6f);
                    // sideways: it won't go; up: it slides
                    EnsureHand();
                    hand.gameObject.SetActive(true);
                    hand.transform.position = PieceCell(a);
                    hand.AnimationState.SetAnimation(0, "click", false);
                    yield return Wait(0.4f);
                    var home = a.transform.position;
                    for (float k = 0; k < 1f; k += Time.deltaTime / 0.5f)
                    {
                        hand.transform.position = PieceCell(a) + new Vector3(-0.7f * Mathf.Sin(k * Mathf.PI), 0);
                        a.transform.position = home + new Vector3(-0.08f * Mathf.Sin(k * Mathf.PI), 0);
                        yield return null;
                    }
                    a.transform.position = home;
                    a.Flash(new Color(1f, 0.35f, 0.35f));
                    yield return Wait(0.3f);
                    Step(2);
                    yield return Drag(a, 1, 0);
                    Light(l, 2); Light(a, 2); Light(r, 2);
                    yield return Wait(0.4f);
                    yield return Hop(cat, V(0, 0), V(1, 0), V(2, 0), V(3, 0), V(4, 0));
                    break;
                }
                case "mystery":
                {
                    Board(3, 2);
                    var l = P(0, 0, paths: "EW"); var m = P(1, 1, paths: "EW", cfg: j => j.mystery = true); var r = P(2, 0, paths: "EW");
                    Hole(5, 3, 0); var cat = Cat(5, -1, 0);
                    yield return Wait(0.7f);
                    yield return Drag(m, 1, 0);
                    Step(2);
                    m.Reveal(); m.Flash(new Color(1f, 0.95f, 0.6f));
                    yield return Wait(0.5f);
                    Light(l, 5); Light(m, 5); Light(r, 5);
                    yield return Wait(0.4f);
                    yield return Hop(cat, V(0, 0), V(1, 0), V(2, 0), V(3, 0));
                    break;
                }
                case "bomb":
                {
                    Board(3, 2);
                    var bomb = P(0, 0, "0,0 1,0 2,0", cfg: j => j.bomb = 9); var road = P(0, 1, "0,0 1,0 2,0");
                    Hole(1, 3, 0); var cat = Cat(1, -1, 0);
                    Run(Countdown(bomb));
                    yield return Wait(1.2f);
                    Light(bomb, 1);
                    yield return Wait(0.3f);
                    Step(2);
                    Run(Hop(cat, V(0, 0), V(1, 0), V(2, 0), V(3, 0)));
                    yield return Wait(0.2f);
                    bomb.bombLive = false; bomb.Refresh(false); bomb.Flash(new Color(1f, 0.95f, 0.6f));
                    yield return Wait(1.2f);
                    break;
                }
                default:
                    Board(3, 1);
                    yield return Wait(1f);
                    break;
            }
        }

        IEnumerator Countdown(Piece p)
        {
            while (p && p.bombLive && p.bomb > 1f)
            {
                p.bomb -= Time.deltaTime * 2f;
                p.Refresh(false);
                yield return null;
            }
        }
    }
}
