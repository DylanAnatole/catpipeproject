using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Spine.Unity;

namespace CatPipe
{
    public class GameController : MonoBehaviour
    {
        const float GateInterval = 2f;
        const int Empty = -1, Wall = -2, CatCell = -3, HoleCell = -4; // cats and holes can also sit on board cells

        [NonSerialized] public Sprite sprBoardFrame, sprBoardCell, sprWall, sprDot, sprGlow, sprBg, sprKey, sprLock, sprBomb, sprIce, sprPad, sprPaw, sprGateBar, sprSparkle, sprAxisArrow, sprOneWay, sprMystery, sprHubRing, sprCatWin, sprBtn, sprPopup, sprWhite, sprDisc, sprRotate;
        [NonSerialized] public Sprite[,] sprBlocks = new Sprite[9, 24]; // [color 0-8, shape 1-23], 0 = neutral white
        [NonSerialized] public Sprite[] sprSh = new Sprite[24], sprHid = new Sprite[24]; // no-path crate / hidden cover per shape
        [NonSerialized] public Sprite sprHole;
        SkeletonDataAsset catData, handData;
        // Spine skins of the original cat, by color 1-9.
        internal static readonly string[] CatSkins = { "", "do", "vang", "xanhduong", "xanhla", "hong", "timxanh", "cam", "xanhngoc", "trang" };
        static readonly Vector3 Feet = new Vector3(0, -0.36f); // the cat skeleton's origin is at its feet
        Font font;

        class Bed { public TextMesh text; public SpriteRenderer badge; public int left; } // several cats of one color on one bed
        class Cat { public Bed bed; public Cat bedNext; public bool waiting; // waiting = still queued on its bed
            public Vector2Int outside, cell; public bool inside; public SpriteRenderer pad; public int dir, color, ice; public Transform tr; public SkeletonAnimation anim; public TextMesh iceText; public SpriteRenderer iceBadge; public bool done; }
        class Nest { public Vector2Int pos; public int color; public bool occupied, ready; public Transform tr; public SpriteRenderer paw; public Vector3 baseScale; }
        class Trace { public readonly List<Vector2Int> cells = new List<Vector2Int>(); public Nest nest; public bool success; }
        struct Step { public int piece; public Vector2Int from, to; public bool rot; } // rot: a tap that turns the block

        TextAsset[] levels;
        int levelIndex, w, h;
        float timeLeft, timeTotal, gateTimer;
        int[,] occ;
        readonly List<Piece> pieces = new List<Piece>();
        readonly List<Cat> cats = new List<Cat>();
        readonly List<Nest> nests = new List<Nest>();
        readonly List<Step> scrambleSteps = new List<Step>();
        bool keysCollected, busy, playing, resolvePending;
        Transform boardRoot;
        Camera cam;
        Coroutine resolveCo;

        int dragIdx = -1, movesMade, curPar, curParTime;
        float pickDist; Vector2 pickF; // a press that barely moves = a tap (turns a rotating block)
        Vector2Int dragTarget;
        List<Vector2Int> dragReach;
        SpriteRenderer ghost;

        Text titleText, timerText, hintText, bannerText;
        RectTransform banner;
        Image hintIcon;
        SkeletonAnimation hand;
        bool tutorialOn, hintOnce; // hintOnce: the hand shows one move (hint booster), then hides
        int tutStep;
        Coroutine handCo;

        // ---- sound (original Catdom clips in Resources/Audio)
        AudioSource sfx;
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        int lastTickSec = -1;

        internal void Sfx(string name, float vol = 1f, float pitchVar = 0.06f) =>
            SfxAt(name, vol, 1f + UnityEngine.Random.Range(-pitchVar, pitchVar));

        // A small pool of sources: each sound keeps its own pitch (a rising run of ticks doesn't bend the others).
        AudioSource[] sfxPool;
        int sfxNext;

        internal void SfxAt(string name, float vol, float pitch)
        {
            if (!clips.TryGetValue(name, out var c)) clips[name] = c = Resources.Load<AudioClip>("Audio/" + name);
            if (c == null) return;
            if (sfxPool == null)
            {
                sfxPool = new AudioSource[8];
                for (int i = 0; i < sfxPool.Length; i++)
                {
                    var s = sfxPool[i] = sfx.gameObject.AddComponent<AudioSource>();
                    s.playOnAwake = false;
                    s.volume = sfx.volume;
                    s.outputAudioMixerGroup = sfx.outputAudioMixerGroup;
                }
            }
            var src = sfxPool[sfxNext];
            sfxNext = (sfxNext + 1) % sfxPool.Length;
            src.pitch = pitch;
            src.PlayOneShot(c, vol);
        }

        // ---- continuous drag: the block collides with walls and other blocks and slides along them
        Vector2 dragPos, grabOffF, bumpOffset;
        Vector2Int lastSnap;
        int dragSteps;
        float lastBump;
        bool wasBlocked;

        // ---- HUD avatar: the original in-game cat (IP_ingame) reacts to what happens
        SkeletonAnimation avatar;
        float idleSince;
        bool avatarWorried;
        SpriteRenderer bgSr;
        Vector2Int lastScreen;
        Coroutine bannerCo;
        GameObject panel;
        RectTransform backlight, timerPill;
        Image panelBurst;
        readonly Image[] panelStars = new Image[3];
        readonly List<Spine.Unity.SkeletonGraphic> winCats = new List<Spine.Unity.SkeletonGraphic>();
        GameObject introPanel;
        Image introIconImg;
        Sprite sprRibbonBlue;
        bool panelIsWin;

        // ---------------------------------------------------------------- setup

        void Awake()
        {
            sprBoardFrame = Load("board_frame"); sprBoardCell = Load("board_cell"); sprWall = Load("wall"); sprDot = Load("dot"); sprGlow = Load("glow"); sprBg = Load("bg_game");
            sprKey = Load("key"); sprLock = Load("lock"); sprBomb = Load("bomb"); sprIce = Load("ice");
            sprPad = Load("pad"); sprPaw = Load("paw"); sprGateBar = Load("gate_bar"); sprSparkle = Load("sparkle"); sprAxisArrow = Load("axis_arrow"); sprOneWay = Load("oneway_arrow"); sprMystery = Load("mystery"); sprHubRing = Load("hub_ring"); sprCatWin = Load("cat_win");
            for (int c = 0; c < 9; c++)
                for (int s = 1; s <= 23; s++) sprBlocks[c, s] = Load($"blocks/b{c}_{s}");
            for (int s = 1; s <= 23; s++) { sprSh[s] = Load($"blocks/sh_{s}"); sprHid[s] = Load($"blocks/hid_{s}"); }
            sprHole = Load("hole");
            catData = Resources.Load<SkeletonDataAsset>("Spine/Cat/cat_ingame_SkeletonData");
            handData = Resources.Load<SkeletonDataAsset>("Spine/Hand/hand_tut_SkeletonData");
            sprBtn = Load("btn"); sprPopup = Load("popup");
            sprWhite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4);
            sprDisc = MakeDisc(64);
            sprRotate = Load("ui_retry");
            font = Resources.Load<Font>("GameFont");
            sfx = gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            AudioListener.volume = SoundOn ? 1f : 0f;

            cam = Camera.main;
            if (cam == null) cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.87f, 0.70f, 0.50f);

            levels = Resources.LoadAll<TextAsset>("Levels").OrderBy(t => t.name).ToArray();
            demo = new GameObject("DemoStage").AddComponent<DemoStage>();
            demo.Init(this, catData, handData);
            BuildUI();
            var parsed = levels.Select(t => JsonUtility.FromJson<LevelJson>(t.text)).ToArray();
            map = new GameObject("Map", typeof(RectTransform)).AddComponent<MapScreen>();
            map.Build(this, uiRoot, Load, parsed, i => { Sfx("click"); TryPlay(i); });
            var homeBtn = MakeImage(map.transform, Load("ui_home"), new Vector2(0, 1), new Vector2(110, -110), new Vector2(150, 120)); // back to home
            homeBtn.preserveAspect = true;
            homeBtn.gameObject.AddComponent<Button>().onClick.AddListener(() => { Sfx("click"); ShowHome(); });
            front = new GameObject("Front", typeof(RectTransform)).AddComponent<FrontScreens>();
            front.Build(this, uiRoot, Load, catData, Resources.Load<Material>("Spine/SkeletonGraphicStraight"),
                () => TryPlay(Unlocked - 1), () => { front.Hide(); ShowMap(); });
            BuildHeartsUI(uiRoot);
            BuildSettingsUI(uiRoot);
            ShowMap();
            if (Environment.GetCommandLineArgs().Contains("-autotest")) front.Hide(); // tests go straight to the levels
            else StartCoroutine(front.RunLoading(Unlocked));
        }

        FrontScreens front;

        void ShowHome()
        {
            ShowMap(); // stops the level; the home screen covers the map
            map.gameObject.SetActive(false);
            front.ShowHome(Unlocked);
        }

        // ---- level map and saved progress
        const string UnlockedKey = "CatPipe_Unlocked";
        MapScreen map;
        Transform uiRoot;
        bool autotest;
        int Unlocked => Mathf.Clamp(PlayerPrefs.GetInt(UnlockedKey, 1), 1, levels.Length);

        void ShowMap()
        {
            CloseSettings();
            SetPickCat(false);
            if (resolveCo != null) StopCoroutine(resolveCo);
            playing = false;
            panel.SetActive(false);
            introPanel.SetActive(false);
            map.Show(Unlocked);
        }

        static Sprite Load(string n)
        {
            var s = Resources.Load<Sprite>("Sprites/" + n);
            if (s == null) Debug.LogError("CatPipe: missing sprite Sprites/" + n);
            return s;
        }

        void LoadLevel(int idx)
        {
            if (resolveCo != null) StopCoroutine(resolveCo);
            map.gameObject.SetActive(false);
            front.Hide();
            if (boardRoot) Destroy(boardRoot.gameObject);
            pieces.Clear(); cats.Clear(); nests.Clear();
            keysCollected = false; busy = false; playing = true; resolvePending = false; dragIdx = -1; gateTimer = 0; movesMade = 0;
            flowLit = -1; flowReady = 0;
            panel.SetActive(false);
            CloseSettings();
            SetPickCat(false);
            hintOnce = false;

            levelIndex = ((idx % levels.Length) + levels.Length) % levels.Length;
            var L = JsonUtility.FromJson<LevelJson>(levels[levelIndex].text);
            w = L.w; h = L.h;
            consumeLevel = L.consume;
            timeLeft = timeTotal = L.time > 0 ? L.time : 120;
            curPar = L.par; curParTime = L.parTime;
            hintText.text = levelHint = string.IsNullOrEmpty(L.hint) ? "Kéo khối" : L.hint;
            hintIcon.sprite = string.IsNullOrEmpty(L.hintIcon) ? sprPaw : Load(L.hintIcon);
            bannerText.text = $"Màn {levelIndex + 1}" + (L.difficulty >= 3 ? "  •  Siêu khó" : L.difficulty == 2 ? "  •  Cực khó" : L.difficulty == 1 ? "  •  Khó" : "");
            if (bannerCo != null) StopCoroutine(bannerCo);
            bannerCo = StartCoroutine(ShowBanner());
            introFeat = null;
            introPanel.SetActive(false);
            if (!string.IsNullOrEmpty(L.introText))
                ShowIntro(L.introFeat, string.IsNullOrEmpty(L.introTitle) ? L.hint : L.introTitle, L.introIcon, L.introText);
            Sfx("banner", 0.6f, 0f);
            occ = new int[w, h];
            boardRoot = new GameObject("Board").transform;

            var frameShadow = MakeSprite("BoardShadow", boardRoot, sprBoardFrame, 1f, -4, new Vector3(0.08f, -0.14f));
            frameShadow.transform.localScale = Vector3.one;
            frameShadow.drawMode = SpriteDrawMode.Sliced;
            frameShadow.size = new Vector2(w + 0.5f, h + 0.5f);
            frameShadow.color = new Color(0.25f, 0.13f, 0.05f, 0.45f);
            BoardLayer(new Vector2(w + 0.5f, h + 0.5f), new Vector3(0, -0.06f), new Color(0.42f, 0.23f, 0.12f), -4); // rim bottom edge
            BoardLayer(new Vector2(w + 0.5f, h + 0.46f), new Vector3(0, 0.02f), new Color(0.72f, 0.45f, 0.26f), -4); // rim
            BoardLayer(new Vector2(w + 0.12f, h + 0.12f), Vector3.zero, new Color(0.30f, 0.18f, 0.11f), -3);          // inner well
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    occ[x, y] = Empty;
                    MakeSprite("Floor", boardRoot, sprBoardCell, 0.95f, -2, CellToWorld(new Vector2Int(x, y))).color = new Color(0.46f, 0.30f, 0.20f);
                }
            if (!string.IsNullOrEmpty(L.walls))
                foreach (var s in L.walls.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var p = Dir.ParseXY(s);
                    occ[p.x, p.y] = Wall;
                    MakeSprite("Wall", boardRoot, sprWall, 0.98f, 0, CellToWorld(p));
                }

            foreach (var pj in L.pieces)
            {
                var go = new GameObject("Piece");
                go.transform.SetParent(boardRoot, false);
                var piece = go.AddComponent<Piece>();
                piece.Build(pj, this);
                pieces.Add(piece);
                Put(pieces.Count - 1, Dir.ParseXY(pj.at));
            }

            foreach (var e in L.nests)
            {
                bool inside = !string.IsNullOrEmpty(e.at);
                var n = new Nest { pos = inside ? Dir.ParseXY(e.at) : EdgeOutside(e), color = e.color };
                n.tr = MakeSprite("Nest", boardRoot, sprBlocks[Piece.Family(e.color), 13], 0.9f, 2, CellToWorld(n.pos)).transform; // 1-cell colored hole
                if (inside) // a hole in the board: the original cat-shaped slot on a colored tile
                {
                    occ[n.pos.x, n.pos.y] = HoleCell;
                    MakeSprite("NestSlot", n.tr, sprHole, 0.62f / n.tr.localScale.x, 1, Vector3.zero).color = new Color(0.25f, 0.13f, 0.06f, 0.75f);
                }
                n.paw = MakeSprite("NestPaw", boardRoot, sprPaw, 0.42f, 3, CellToWorld(n.pos));
                n.paw.color = new Color(1f, 1f, 1f, 0.6f);
                n.baseScale = n.tr.localScale;
                nests.Add(n);
            }

            foreach (var e in L.cats)
            {
              int bedCount = Mathf.Max(1, e.count);
              Cat prevOnBed = null;
              for (int b = 0; b < bedCount; b++) // a bed with several cats: one sits on it, the others wait their turn
              {
                var c = new Cat { inside = !string.IsNullOrEmpty(e.at), color = e.color };
                if (c.inside) { c.cell = Dir.ParseXY(e.at); occ[c.cell.x, c.cell.y] = CatCell; }
                else { c.outside = EdgeOutside(e); c.dir = EdgeDir(e.side); }
                var basePos = CellToWorld(c.inside ? c.cell : c.outside);
                c.pad = prevOnBed != null ? prevOnBed.pad : MakeSprite("Pad", boardRoot, sprPad, 0.95f, 2, basePos + new Vector3(0, -0.12f));
                c.anim = SkeletonAnimation.NewSkeletonAnimationGameObject(catData);
                c.anim.name = "Cat";
                c.tr = c.anim.transform;
                c.tr.SetParent(boardRoot, false);
                c.tr.localPosition = basePos + Feet;
                c.tr.localScale = Vector3.one * 0.85f;
                c.anim.Skeleton.SetSkin(CatSkins[e.color]);
                c.anim.Skeleton.SetSlotsToSetupPose();
                c.anim.GetComponent<MeshRenderer>().sortingOrder = 100;
                var shadow = MakeSprite("CatShadow", c.tr, sprGlow, 1.0f / 0.85f, 99, new Vector3(0, 0.03f));
                shadow.transform.localScale = new Vector3(shadow.transform.localScale.x, shadow.transform.localScale.y * 0.32f, 1);
                shadow.color = new Color(0.15f, 0.07f, 0.02f, 0.55f);
                c.ice = e.ice;
                if (c.ice > 0) // frozen cat: original ice animation, plus the drops left
                {
                    // big, readable counter on a snow badge: how many other cats must get home first
                    c.iceBadge = MakeSprite("CatIceBadge", boardRoot, sprIce, 0.42f, 101, basePos + new Vector3(0.36f, -0.28f));
                    c.iceText = MakeText("CatIceCount", boardRoot, basePos + new Vector3(0.36f, -0.28f), 102);
                    c.iceText.transform.localScale = Vector3.one * 1.5f;
                    c.iceText.color = new Color(0.08f, 0.25f, 0.55f);
                    c.iceText.text = c.ice.ToString();
                    Play(c, IceAnim(c.ice) + "_idle", true);
                }
                else Idle(c);
                if (prevOnBed != null) { c.waiting = true; c.bed = prevOnBed.bed; prevOnBed.bedNext = c; c.tr.gameObject.SetActive(false); }
                else if (bedCount > 1)
                {
                    c.bed = new Bed { left = bedCount };
                    c.bed.badge = MakeSprite("BedBadge", boardRoot, sprDisc, 0.42f, 101, basePos + new Vector3(-0.36f, -0.3f));
                    c.bed.badge.color = new Color(0.95f, 0.45f, 0.2f);
                    c.bed.text = MakeText("BedCount", boardRoot, basePos + new Vector3(-0.36f, -0.3f), 102);
                    c.bed.text.transform.localScale = Vector3.one * 1.1f;
                    c.bed.text.text = "x" + bedCount;
                }
                prevOnBed = c;
                cats.Add(c);
              }
            }

            if (!string.IsNullOrEmpty(L.start)) ApplyStart(L);
            else Scramble(L.scramble);
            startAt = pieces.Select(p => p.anchor).ToArray(); // the hint booster replays the solution from here
            startMask = pieces.Select(p => p.masks[0]).ToArray();
            prevAt = startAt.ToArray();
            history.Clear();
            FitCamera();
            lastScreen = new Vector2Int(Screen.width, Screen.height);
            BuildBackground();
            PlaceAvatar();
            BuildMotes();
            UpdateHud();
            UpdateFlow();
            for (int i = 0; i < pieces.Count; i++) pieces[i].StartCoroutine(pieces[i].PopIn(0.03f * i)); // runs on the piece: stops if it is destroyed
            foreach (var c in cats) StartCoroutine(DropIn(c));
            StartTutorial(L.tutorial);
            resolveCo = StartCoroutine(Resolve());
        }

        // ---------------------------------------------------------------- onboarding (no text: a hand shows each move)

        void StartTutorial(bool on)
        {
            if (handCo != null) StopCoroutine(handCo);
            tutorialOn = on && scrambleSteps.Count > 0;
            tutStep = scrambleSteps.Count - 1; // the scramble played backwards is the solution
            if (!tutorialOn) { if (hand) hand.gameObject.SetActive(false); return; }
            ShowHand();
        }

        void ShowHand()
        {
            if (!hand)
            {
                hand = SkeletonAnimation.NewSkeletonAnimationGameObject(handData);
                hand.name = "TutorialHand";
                hand.transform.localScale = Vector3.one * 0.32f;
                hand.GetComponent<MeshRenderer>().sortingOrder = 200;
            }
            hand.gameObject.SetActive(true);
            handCo = StartCoroutine(HandLoop());
        }

        void AdvanceTutorial(int moved, bool rotated)
        {
            if (!tutorialOn) return;
            var st = scrambleSteps[tutStep];
            if (moved == st.piece && (st.rot ? rotated : !rotated && pieces[moved].anchor == st.from)) tutStep = hintOnce ? -1 : tutStep - 1;
            else tutStep = -1; // the player found their own way: stop guiding
            if (tutStep < 0)
            {
                tutorialOn = false;
                hintOnce = false;
                StopCoroutine(handCo);
                hand.gameObject.SetActive(false);
            }
        }

        // Press on the block, drag it to where it belongs, repeat.
        IEnumerator HandLoop()
        {
            while (tutorialOn)
            {
                var st = scrambleSteps[tutStep];
                var o = pieces[st.piece].offs[0];
                Vector3 a = CellToWorld(st.to + o), b = CellToWorld(st.from + o);
                hand.transform.position = a;
                hand.AnimationState.SetAnimation(0, "click", false);
                if (st.rot) { yield return new WaitForSeconds(1.2f); continue; } // a tap on the rotating block
                yield return new WaitForSeconds(0.5f);
                for (float k = 0; k < 1f; k += Time.deltaTime / 0.8f)
                {
                    hand.transform.position = Vector3.Lerp(a, b, Mathf.SmoothStep(0, 1, k));
                    yield return null;
                }
                yield return new WaitForSeconds(0.6f);
            }
        }

        void PlaceAvatar()
        {
            if (!avatar)
            {
                avatar = SkeletonAnimation.NewSkeletonAnimationGameObject(Resources.Load<SkeletonDataAsset>("Spine/Avatar/ip_ingame_SkeletonData"));
                avatar.name = "Avatar";
                avatar.Skeleton.SetSkin("no_bg");
                avatar.Skeleton.SetSlotsToSetupPose();
                avatar.GetComponent<MeshRenderer>().sortingOrder = 150;
            }
            PositionAvatar();
            avatarWorried = false;
            idleSince = Time.time;
            AvatarIdle();
        }

        void PositionAvatar()
        {
            var p = cam.ViewportToWorldPoint(new Vector3(0.125f, 0.91f, 10f));
            avatar.transform.position = new Vector3(p.x, p.y, 0);
            avatar.transform.localScale = Vector3.one * cam.orthographicSize * 0.042f; // same on-screen size for any board
        }

        void AvatarIdle()
        {
            avatar.AnimationState.SetAnimation(0, "ip_idle", true);
            avatar.AnimationState.AddAnimation(0, "ip_idle2", false, 0);
            avatar.AnimationState.AddAnimation(0, "ip_idle", true, 0);
        }

        // One-off reaction, then back to idle (or to the worried loop when time is short).
        void AvatarReact(string anim)
        {
            if (!avatar) return;
            avatar.AnimationState.SetAnimation(0, anim, false);
            avatar.AnimationState.AddAnimation(0, avatarWorried ? "ip_worried_loop" : "ip_idle", true, 0);
        }

        // A few soft light motes drifting over the wooden background.
        void BuildMotes()
        {
            float hh = cam.orthographicSize, ww = hh * cam.aspect;
            for (int i = 0; i < 14; i++)
            {
                var sr = MakeSprite("Mote", boardRoot, sprGlow, UnityEngine.Random.Range(0.25f, 0.6f), -9,
                    new Vector3(UnityEngine.Random.Range(-ww, ww), UnityEngine.Random.Range(-hh, hh) + cam.transform.position.y));
                sr.color = new Color(1f, 0.9f, 0.7f, 0);
                StartCoroutine(Mote(sr, ww, hh));
            }
        }

        IEnumerator Mote(SpriteRenderer sr, float ww, float hh)
        {
            float phase = UnityEngine.Random.Range(0f, 10f), speed = UnityEngine.Random.Range(0.08f, 0.2f);
            var basePos = sr.transform.localPosition;
            while (sr)
            {
                float t = Time.time + phase;
                basePos.y += speed * Time.deltaTime;
                if (basePos.y > cam.transform.position.y + hh + 0.5f) basePos.y = cam.transform.position.y - hh - 0.5f;
                sr.transform.localPosition = basePos + new Vector3(Mathf.Sin(t * 0.7f) * 0.3f, 0);
                sr.color = new Color(1f, 0.9f, 0.7f, 0.12f + 0.1f * Mathf.Sin(t * 1.3f));
                yield return null;
            }
        }

        static string IceAnim(int ice) => "ice" + Mathf.Clamp(ice, 1, 3);

        void Play(Cat c, string anim, bool loop, string thenLoop = null)
        {
            var st = c.anim.AnimationState;
            st.SetAnimation(0, anim, loop);
            if (thenLoop != null) st.AddAnimation(0, thenLoop, true, 0);
        }

        // A random idle so the cats don't move in sync.
        void Idle(Cat c)
        {
            var e = c.anim.AnimationState.SetAnimation(0, "front_idle" + UnityEngine.Random.Range(1, 5), true);
            e.TrackTime = UnityEngine.Random.Range(0f, 3f);
        }

        // Cats drop onto their cushion with a little squash.
        IEnumerator DropIn(Cat c)
        {
            var end = c.tr.localPosition;
            for (float t = 0; t < 1f && c.tr; t += Time.deltaTime / 0.45f)
            {
                float y = t < 0.7f ? Mathf.Lerp(1.5f, 0f, (t / 0.7f) * (t / 0.7f)) : Mathf.Sin((t - 0.7f) / 0.3f * Mathf.PI) * 0.12f;
                c.tr.localPosition = end + new Vector3(0, y);
                yield return null;
            }
            if (c.tr) c.tr.localPosition = end;
        }

        // The level file holds the solved layout. Shuffle it with legal slides only,
        // so the level is always solvable (slides are reversible).
        void Scramble(int count)
        {
            foreach (var c in cats)
                if (!TraceFrom(c, true).success)
                    Debug.LogError($"CatPipe: level {levels[levelIndex].name} solved layout does not lead cat color {c.color} home");

            scrambleSteps.Clear();
            var movable = Enumerable.Range(0, pieces.Count).Where(i => pieces[i].Movable(false)).ToList();
            if (movable.Count == 0) return;
            var rng = new System.Random(levelIndex * 7919 + 17);
            if (count <= 0) count = movable.Count * 3;
            for (int s = 0; s < count + 60; s++)
            {
                if (s >= count && !cats.Any(c => TraceFrom(c, true).success)) break;
                int i = movable[s % movable.Count];
                var reach = Reachable(i);
                reach.Remove(pieces[i].anchor);
                if (reach.Count == 0) continue;
                var to = reach[rng.Next(reach.Count)];
                scrambleSteps.Add(new Step { piece = i, from = pieces[i].anchor, to = to });
                Lift(i);
                Put(i, to);
            }
        }

        // Start layout and optimal solution stored by the generator. The solution is kept as scrambleSteps
        // (last entry = first move, from = where the block goes), the form the tutorial hand and AutoSolve use.
        void ApplyStart(LevelJson L)
        {
            var starts = L.start.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < pieces.Count; i++) Lift(i);
            for (int i = 0; i < pieces.Count; i++) Put(i, Dir.ParseXY(starts[i]));
            if (!string.IsNullOrEmpty(L.rot)) // rotating blocks start turned
                foreach (var tok in L.rot.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var rq = tok.Split(':');
                    for (int q = int.Parse(rq[1]); q > 0; q--) pieces[int.Parse(rq[0])].Rotate(false);
                }
            scrambleSteps.Clear();
            var at = pieces.Select(p => p.anchor).ToArray();
            foreach (var mv in (L.solution ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = mv.Split(':');
                int i = int.Parse(kv[0]);
                if (kv[1] == "R") { scrambleSteps.Insert(0, new Step { piece = i, from = at[i], to = at[i], rot = true }); continue; }
                var to = Dir.ParseXY(kv[1]);
                scrambleSteps.Insert(0, new Step { piece = i, from = to, to = at[i] });
                at[i] = to;
            }
        }

        void BoardLayer(Vector2 size, Vector3 pos, Color col, int order)
        {
            var sr = MakeSprite("BoardLayer", boardRoot, sprBoardFrame, 1f, order, pos);
            sr.transform.localScale = Vector3.one;
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.size = size;
            sr.color = col;
        }

        // The original in-game wooden background, scaled to cover the screen.
        void BuildBackground()
        {
            bgSr = MakeSprite("Background", boardRoot, sprBg, 1f, -10, Vector3.zero);
            ScaleBackground();
        }

        void ScaleBackground()
        {
            float hh = cam.orthographicSize * 2f, ww = hh * cam.aspect;
            var b = sprBg.bounds.size;
            bgSr.transform.position = new Vector3(cam.transform.position.x, cam.transform.position.y, 0);
            bgSr.transform.localScale = Vector3.one * Mathf.Max(ww / b.x, hh / b.y);
        }

        // On the web the canvas can change size after the level is built (page layout, window resize, phone rotation):
        // refit the camera and everything placed relative to the screen.
        void RelayoutIfResized()
        {
            var size = new Vector2Int(Screen.width, Screen.height);
            if (size == lastScreen) return;
            lastScreen = size;
            FitCamera();
            if (bgSr) ScaleBackground();
            if (avatar) PositionAvatar();
        }

        void FitCamera()
        {
            float worldW = w + 2.25f, worldH = h + 2.6f + 3.2f; // sides: just room for the cats and holes outside the board
            cam.orthographicSize = Mathf.Max(worldH / 2f, worldW / (2f * cam.aspect));
            cam.transform.position = new Vector3(0, -0.2f, -10);
        }

        // ---------------------------------------------------------------- grid helpers

        Vector3 CellToWorld(Vector2Int p) => new Vector3(p.x - (w - 1) / 2f, -(p.y - (h - 1) / 2f), 0);

        Vector2 WorldToCellF(Vector3 wp) => new Vector2(wp.x + (w - 1) / 2f, -wp.y + (h - 1) / 2f);

        bool InGrid(Vector2Int p) => p.x >= 0 && p.y >= 0 && p.x < w && p.y < h;

        Vector2Int EdgeOutside(EdgeRef e)
        {
            switch (e.side)
            {
                case "L": return new Vector2Int(-1, e.index);
                case "R": return new Vector2Int(w, e.index);
                case "T": return new Vector2Int(e.index, -1);
                default: return new Vector2Int(e.index, h);
            }
        }

        static int EdgeDir(string side) => side == "L" ? 1 : side == "R" ? 3 : side == "T" ? 2 : 0;

        void Lift(int i)
        {
            foreach (var o in pieces[i].offs) occ[pieces[i].anchor.x + o.x, pieces[i].anchor.y + o.y] = Empty;
        }

        void Put(int i, Vector2Int a)
        {
            var p = pieces[i];
            p.anchor = a;
            foreach (var o in p.offs) occ[a.x + o.x, a.y + o.y] = i;
            p.transform.localPosition = CellToWorld(a);
        }

        bool Fits(int i, Vector2Int a)
        {
            foreach (var o in pieces[i].offs)
            {
                var c = a + o;
                if (!InGrid(c) || (occ[c.x, c.y] != Empty && occ[c.x, c.y] != i)) return false;
            }
            return true;
        }

        // Every anchor the piece can slide to through free cells (BFS, others fixed).
        List<Vector2Int> Reachable(int i)
        {
            var start = pieces[i].anchor;
            var seen = new HashSet<Vector2Int> { start };
            var list = new List<Vector2Int> { start };
            for (int k = 0; k < list.Count; k++)
                for (int dd = 0; dd < 4; dd++)
                {
                    if (pieces[i].axis >= 0 && dd % 2 == pieces[i].axis) continue; // axis lock: 0 = horizontal only (skip N/S)
                    var n = list[k] + Dir.Step[dd];
                    if (seen.Contains(n) || !Fits(i, n)) continue;
                    seen.Add(n);
                    list.Add(n);
                }
            return list;
        }

        // The cat finds its own way: BFS to a free hole of its color along a route that never passes the same
        // cell twice (so it never loops around near its hole). States carry the cells already on the route.
        // On success cells = the route; otherwise cells = every cell the cat can reach (lit to show progress).
        struct RouteNode { public Vector2Int pos; public int dir; public bool key; public ulong visited; public int parent; }

        Trace TraceFrom(Cat cat, bool ignoreBlockers = false)
        {
            var res = new Trace();
            var nodes = new List<RouteNode>();
            if (!cat.inside) nodes.Add(new RouteNode { pos = cat.outside + Dir.Step[cat.dir], dir = cat.dir, key = keysCollected, parent = -1 });
            else // a cat on a board cell can step out on any side
                for (int d = 0; d < 4; d++)
                    nodes.Add(new RouteNode { pos = cat.cell + Dir.Step[d], dir = d, key = keysCollected, visited = 1UL << (cat.cell.y * w + cat.cell.x), parent = -1 });
            var seen = new HashSet<(Vector2Int, int, bool, ulong)>();
            var reached = new HashSet<Vector2Int>();
            for (int qi = 0; qi < nodes.Count && nodes.Count < 60000; qi++)
            {
                var st = nodes[qi];
                if (!InGrid(st.pos) || occ[st.pos.x, st.pos.y] == HoleCell)
                {
                    var n = nests.Find(x => x.pos == st.pos);
                    if (n == null || n.color != cat.color || (!ignoreBlockers && n.occupied)) continue;
                    res.cells.Clear();
                    for (int k = st.parent; k >= 0; k = nodes[k].parent) res.cells.Add(nodes[k].pos);
                    res.cells.Reverse();
                    res.nest = n;
                    res.success = true;
                    return res;
                }
                ulong bit = 1UL << (st.pos.y * w + st.pos.x);
                if ((st.visited & bit) != 0) continue; // already on this route: no loops
                int idx = occ[st.pos.x, st.pos.y];
                if (idx < 0) continue;
                var p = pieces[idx];
                int ci = p.CellIndex(st.pos), m = p.masks[ci], enter = Dir.Opp(st.dir);
                if (((m >> enter) & 1) == 0) continue;
                if (p.color > 0 && p.color != cat.color) continue;
                if (!ignoreBlockers && (p.ice > 0 || (p.isLock && !st.key) || (p.gate && !p.gateOpen))) continue;
                if (reached.Add(st.pos)) res.cells.Add(st.pos);
                bool key = st.key || p.key;
                ulong visited = st.visited | bit;
                for (int d = 0; d < 4; d++)
                {
                    if (d == enter || ((m >> d) & 1) == 0) continue;
                    if (m == 0b1111 && !p.junction[ci] && d != st.dir) continue; // plain crossing: straight only
                    if (p.oneWay >= 0 && d != p.outDir[ci]) continue;            // one-way: only toward the exit
                    var np = st.pos + Dir.Step[d];
                    if (!seen.Add((np, d, key, visited))) continue;
                    nodes.Add(new RouteNode { pos = np, dir = d, key = key, visited = visited, parent = qi });
                }
            }
            return res;
        }

        // ---------------------------------------------------------------- gameplay

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.R)) { LoadLevel(levelIndex); return; }
            if (Input.GetKeyDown(KeyCode.N)) { LoadLevel(levelIndex + 1); return; }
            if (Input.GetKeyDown(KeyCode.P)) { LoadLevel(levelIndex - 1); return; }
            if (Input.GetKeyDown(KeyCode.S)) { StartCoroutine(AutoSolve()); return; }
            UpdateHearts();
            if (panel.activeSelf) backlight.Rotate(0, 0, -30f * Time.deltaTime);
            if (introPanel.activeSelf)
            {
                introGlow.Rotate(0, 0, -20f * Time.deltaTime);
                introTag.localScale = Vector3.one * (1f + 0.08f * Mathf.Sin(Time.time * 5f));
                for (int k = 0; k < 2; k++) tipNums[k].localScale = Vector3.one * (k + 1 == tipOn ? 1f + 0.1f * Mathf.Abs(Mathf.Sin(Time.time * 4f)) : 0.9f);
            }
            if (panel.activeSelf) panelBurst.rectTransform.localScale = Vector3.one * (1f + 0.06f * Mathf.Sin(Time.time * 3f));
            if (!playing || settingsPanel.activeSelf) return;
            if (sendBtn) sendBtn.localScale = Vector3.one * (pickCat ? 1f + 0.08f * Mathf.Sin(Time.time * 8f) : 1f);
            if (introPanel.activeSelf)
            {
                if (Input.GetMouseButtonDown(0) && Time.time - introShownAt > 2.5f) { CloseIntro(); Sfx("click"); }
                return;
            }

            if (avatar)
            {
                if (Input.GetMouseButtonDown(0) && dragIdx < 0)
                {
                    var wp = cam.ScreenToWorldPoint(Input.mousePosition);
                    if (Vector2.Distance(wp, avatar.transform.position) < cam.orthographicSize * 0.12f) AvatarReact("ip_cheer");
                }
                if (dragIdx >= 0) idleSince = Time.time;
                if (!avatarWorried && Time.time - idleSince > 6f) { idleSince = Time.time; AvatarReact("ip_thinking"); }
                if (!avatarWorried && timeLeft <= 10f)
                {
                    avatarWorried = true;
                    avatar.AnimationState.SetAnimation(0, "ip_worried_start", false);
                    avatar.AnimationState.AddAnimation(0, "ip_worried_loop", true, 0);
                }
            }

            timeLeft -= Time.deltaTime;
            UpdateHud();
            if (timeLeft <= 0) { ShowPanel(false, "Hết giờ!"); return; }
            int secNow = Mathf.CeilToInt(timeLeft);
            if (secNow <= 10 && secNow != lastTickSec) { lastTickSec = secNow; Sfx("tick", 0.6f, 0f); }

            foreach (var p in pieces)
            {
                if (!p.bombLive) continue;
                p.bomb -= Time.deltaTime;
                p.Refresh(keysCollected);
                if (p.bomb <= 0) { Sfx("bomb"); ShowPanel(false, "Bom nổ rồi!"); return; }
            }

            gateTimer += Time.deltaTime;
            if (gateTimer >= GateInterval)
            {
                gateTimer = 0;
                foreach (var p in pieces.Where(p => p.gate)) { p.gateOpen = !p.gateOpen; p.Refresh(keysCollected); }
                UpdateFlow();
                if (dragIdx < 0) RequestResolve();
            }

            foreach (var n in nests) // a hole pulses while a cat's route to it is complete
                n.tr.localScale = n.baseScale * (n.ready && !n.occupied ? 1f + 0.1f * Mathf.Abs(Mathf.Sin(Time.time * 5f)) : 1f);

            if (pickCat) HandlePickCat();
            else HandleDrag();
        }

        void LateUpdate() => RelayoutIfResized();

        void HandleDrag()
        {
            if (busy) return;
            var f = WorldToCellF(cam.ScreenToWorldPoint(Input.mousePosition));

            if (dragIdx < 0)
            {
                if (!Input.GetMouseButtonDown(0)) return;
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
                var c = new Vector2Int(Mathf.RoundToInt(f.x), Mathf.RoundToInt(f.y));
                if (!InGrid(c) || occ[c.x, c.y] < 0) return;
                int i = occ[c.x, c.y];
                if (!pieces[i].Movable(keysCollected))
                {
                    pieces[i].Shake();
                    pieces[i].Flash(new Color(1f, 0.35f, 0.35f));
                    Sfx("block_locked", 0.7f);
                    return;
                }
                dragIdx = i;
                pickF = f; pickDist = 0;
                if (hand) hand.gameObject.SetActive(false);
                dragPos = pieces[i].anchor;
                grabOffF = f - dragPos;
                dragReach = Reachable(i);
                bumpOffset = Vector2.zero;
                wasBlocked = false;
                ghost = pieces[i].MakeGhost(boardRoot);
                pieces[i].SetDragging(true);
                lastSnap = pieces[i].anchor; dragSteps = 0;
                Sfx("block_pick", 0.5f);
                return;
            }

            var piece = pieces[dragIdx];
            if (Input.GetMouseButton(0))
            {
                pickDist = Mathf.Max(pickDist, (f - pickF).magnitude);
                var want = f - grabOffF;
                if (piece.axis == 0) want.y = dragPos.y;
                if (piece.axis == 1) want.x = dragPos.x;
                var before = dragPos;
                dragPos = Slide(dragIdx, dragPos, want);
                // pulling against an obstacle: bump feedback
                var pull = want - dragPos;
                bool blocked = pull.magnitude > 0.35f && (dragPos - before).magnitude < 0.002f;
                if (blocked && !wasBlocked) // bump once, when the block first meets the obstacle
                {
                    bumpOffset = pull.normalized * 0.06f;
                    if (Time.time - lastBump > 0.3f) { lastBump = Time.time; Sfx("drop", 0.35f); } // soft knock, never a rattle
                    var blocker = BlockerAt(dragIdx, dragPos, pull.normalized);
                    if (blocker >= 0) pieces[blocker].Shake();
                }
                if (pull.magnitude < 0.2f || (dragPos - before).magnitude > 0.01f) wasBlocked = false;
                else if (blocked) wasBlocked = true;
                bumpOffset = Vector2.Lerp(bumpOffset, Vector2.zero, 14f * Time.deltaTime);
                var visual = dragPos + new Vector2(bumpOffset.x, bumpOffset.y);
                piece.Hold(CellToWorldF(visual));
                var snap = new Vector2Int(Mathf.RoundToInt(dragPos.x), Mathf.RoundToInt(dragPos.y));
                if (snap != lastSnap) // a soft tick for every cell the block slides across, rising in pitch
                {
                    lastSnap = snap;
                    dragSteps++;
                    SfxAt("cell_tick", 0.55f, Mathf.Min(1.45f, 0.95f + 0.06f * dragSteps));
                }
                ghost.transform.parent.localPosition = CellToWorld(snap);
                ghost.enabled = snap != piece.anchor && Fits(dragIdx, snap);
                return;
            }

            var from = piece.anchor;
            dragTarget = new Vector2Int(Mathf.RoundToInt(dragPos.x), Mathf.RoundToInt(dragPos.y));
            if (!Fits(dragIdx, dragTarget)) // safety: nearest cell the block could slide to
                dragTarget = dragReach.OrderBy(a => (a - dragPos).sqrMagnitude).First();
            var heldAt = piece.HeldBase;
            Lift(dragIdx);
            Put(dragIdx, dragTarget);
            piece.Land(heldAt);
            Destroy(ghost.transform.parent.gameObject);
            dragIdx = -1;
            bool tap = dragTarget == from && pickDist < 0.3f;
            if (tap && piece.rotatable) Sfx("block_turn", 0.8f);
            else Sfx("drop", dragTarget != from ? 0.75f : 0.3f);
            if (dragTarget != from) OnDrop(pieces.IndexOf(piece));
            else if (tap && piece.rotatable) { piece.Rotate(true); OnDrop(pieces.IndexOf(piece), true); }
            if (hand && tutorialOn) hand.gameObject.SetActive(true);
        }

        Vector3 CellToWorldF(Vector2 p) => new Vector3(p.x - (w - 1) / 2f, -(p.y - (h - 1) / 2f), 0);

        // Can piece i sit at a fractional anchor? Every grid cell it overlaps must be free (or its own).
        bool FitsF(int i, Vector2 a)
        {
            const float eps = 0.02f;
            foreach (var o in pieces[i].offs)
            {
                float cx = a.x + o.x, cy = a.y + o.y;
                for (int gx = Mathf.CeilToInt(cx - 1f + eps); gx <= Mathf.FloorToInt(cx + 1f - eps); gx++)
                    for (int gy = Mathf.CeilToInt(cy - 1f + eps); gy <= Mathf.FloorToInt(cy + 1f - eps); gy++)
                    {
                        if (gx < 0 || gy < 0 || gx >= w || gy >= h) return false;
                        int v = occ[gx, gy];
                        if (v != Empty && v != i) return false;
                    }
            }
            return true;
        }

        // Move toward want one axis at a time, stopping at obstacles (so the block slides along them).
        // Corner assist: when blocked on one axis, ease the other axis toward the grid so the block can turn.
        Vector2 Slide(int i, Vector2 cur, Vector2 want)
        {
            var d = want - cur;
            int n = Mathf.Max(1, Mathf.CeilToInt(d.magnitude / 0.1f));
            var step = d / n;
            bool xMajor = Mathf.Abs(d.x) >= Mathf.Abs(d.y);
            // corner assist only when the pull is mostly along one axis (a diagonal pull just slides along whatever is free)
            bool assist = Mathf.Min(Mathf.Abs(d.x), Mathf.Abs(d.y)) < 0.5f * Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.y));
            for (int k = 0; k < n; k++)
            {
                bool assisted;
                if (xMajor)
                {
                    cur = MoveAxis(i, cur, new Vector2(step.x, 0), 1, assist, out assisted);
                    if (!assisted) cur = MoveAxis(i, cur, new Vector2(0, step.y), 0, false, out _);
                }
                else
                {
                    cur = MoveAxis(i, cur, new Vector2(0, step.y), 0, assist, out assisted);
                    if (!assisted) cur = MoveAxis(i, cur, new Vector2(step.x, 0), 1, false, out _);
                }
            }
            return cur;
        }

        Vector2 MoveAxis(int i, Vector2 cur, Vector2 delta, int otherAxis, bool assist, out bool assisted)
        {
            assisted = false;
            if (delta.sqrMagnitude < 1e-8f) return cur;
            if (FitsF(i, cur + delta)) return cur + delta;
            float other = otherAxis == 0 ? cur.x : cur.y, target = Mathf.Round(other), diff = target - other;
            if (assist && Mathf.Abs(diff) > 0.001f && Mathf.Abs(diff) < 0.45f)
            {
                var nudge = otherAxis == 0 ? new Vector2(Mathf.Clamp(diff, -0.08f, 0.08f), 0) : new Vector2(0, Mathf.Clamp(diff, -0.08f, 0.08f));
                if (FitsF(i, cur + nudge)) { assisted = true; return cur + nudge; }
            }
            float lo = 0, hi = 1; // move as far as possible
            for (int it = 0; it < 6; it++) { float mid = (lo + hi) / 2f; if (FitsF(i, cur + delta * mid)) lo = mid; else hi = mid; }
            return cur + delta * lo;
        }

        // The piece that stops the dragged block in this direction (-1: wall / edge).
        int BlockerAt(int i, Vector2 pos, Vector2 dir)
        {
            foreach (var o in pieces[i].offs)
            {
                var c = new Vector2Int(Mathf.RoundToInt(pos.x + o.x + dir.x * 0.6f), Mathf.RoundToInt(pos.y + o.y + dir.y * 0.6f));
                if (InGrid(c) && occ[c.x, c.y] >= 0 && occ[c.x, c.y] != i) return occ[c.x, c.y];
            }
            return -1;
        }

        void OnDrop(int moved, bool rotated = false)
        {
            movesMade++;
            history.Add(new Step { piece = moved, from = prevAt[moved], to = pieces[moved].anchor, rot = rotated });
            prevAt[moved] = pieces[moved].anchor;
            if (pieces[moved].mystery) Sfx("mystery", 0.8f);
            pieces[moved].Reveal();
            AdvanceTutorial(moved, rotated);
            foreach (var p in pieces)
                if (p.ice > 0) { p.ice--; p.Refresh(keysCollected); Sfx(p.ice == 0 ? "ice_crack" : "ice", 0.6f); }
            UpdateFlow();
            RequestResolve();
        }

        // Only one Resolve runs at a time; a request while it runs makes it check the board again.
        void RequestResolve()
        {
            if (busy) { resolvePending = true; return; }
            resolveCo = StartCoroutine(Resolve());
        }

        IEnumerator Resolve()
        {
            busy = true;
            do
            {
                resolvePending = false;
                yield return new WaitForSeconds(0.1f);
                bool moved = true;
                while (moved)
                {
                    moved = false;
                    foreach (var cat in cats)
                    {
                        if (cat.done || cat.ice > 0 || cat.waiting) continue;
                        var t = TraceFrom(cat);
                        if (!t.success) continue;
                        yield return Walk(cat, t);
                        moved = true;
                        break;
                    }
                }
            } while (resolvePending);
            UpdateFlow();
            busy = false;
            if (playing && cats.All(c => c.done))
            {
                playing = false;
                yield return new WaitForSeconds(0.4f);
                ShowPanel(true, "Tuyệt vời!");
            }
        }

        IEnumerator Walk(Cat cat, Trace t, bool fly = false)
        {
            cat.done = true;
            t.nest.occupied = true;
            if (cat.inside) { occ[cat.cell.x, cat.cell.y] = Empty; cat.pad.enabled = false; } // its cell is free for blocks now
            if (cat.bed != null) // the next cat on the bed hops up
            {
                cat.bed.left--;
                cat.bed.text.text = cat.bed.left > 1 ? "x" + cat.bed.left : "";
                cat.bed.badge.enabled = cat.bed.left > 1;
                if (cat.bedNext != null) { cat.bedNext.waiting = false; cat.bedNext.tr.gameObject.SetActive(true); StartCoroutine(DropIn(cat.bedNext)); }
            }
            var pts = t.cells.Select(c => CellToWorld(c) + Feet).ToList();
            pts.Add(t.nest.tr.position + Feet + new Vector3(0, 0.1f));
            Play(cat, "jump1", false); // take-off
            Sfx("cat_whoosh", 0.45f);
            yield return new WaitForSeconds(0.12f);
            Sfx("meow" + UnityEngine.Random.Range(1, 8), 0.8f, 0.1f);
            var pos = cat.tr.position;
            int stepSide = 1;
            var baseScale = cat.tr.localScale;
            string pose = null;
            for (int i = 0; i < pts.Count; i++)
            {
                var target = pts[i];
                var dir = target - pos;
                // pose by direction: fly sideways (facing the way it goes), dive when going down, back view going up
                string want = Mathf.Abs(dir.x) >= Mathf.Abs(dir.y) ? "jump3" : dir.y < 0 ? "jump2" : "back_jump";
                if (want != pose)
                {
                    pose = want;
                    var e = cat.anim.AnimationState.SetAnimation(0, pose, false);
                    e.TrackTime = pose == "back_jump" ? 0.3f : 0.45f; // already airborne
                }
                cat.anim.Skeleton.ScaleX = pose == "jump3" && dir.x < 0 ? -1f : 1f;
                bool last = i == pts.Count - 1;
                float hop = Mathf.Lerp(0.17f, 0.09f, Mathf.InverseLerp(10f, 40f, pts.Count)); // long routes hop faster
                float dur = last ? (fly ? 0.75f : 0.32f) : hop, height = last ? (fly ? 2.4f : 0.55f) : 0.22f; // fly: one big leap
                var from = pos;
                for (float k = 0; k < 1f; k += Time.deltaTime / dur)
                {
                    pos = Vector3.Lerp(from, target, k);
                    cat.tr.position = pos + new Vector3(0, 4f * height * k * (1f - k));
                    yield return null;
                }
                pos = target;
                cat.tr.position = pos;
                if (!last)
                {
                    stepSide = -stepSide;
                    StartCoroutine(PawPrint(pos - Feet, dir, stepSide));
                }
                if (i < t.cells.Count)
                {
                    var p = pieces[occ[t.cells[i].x, t.cells[i].y]];
                    if (p.key)
                    {
                        p.key = false; keysCollected = true; foreach (var q in pieces) q.Refresh(true); Sfx("key");
                        if (pieces.Any(q => q.isLock)) Sfx("lock_open", 0.55f); // the locks click open
                    }
                    if (p.bombLive) { p.bombLive = false; p.Refresh(keysCollected); }
                    p.Reveal();
                }
            }
            cat.tr.position = pos;
            cat.anim.Skeleton.ScaleX = 1f;
            if (consumeLevel) ConsumeRoute(t);
            for (float k = 0; k < 1f; k += Time.deltaTime / 0.22f) // landing squash
            {
                float sq = Mathf.Sin(k * Mathf.PI) * 0.22f;
                cat.tr.localScale = new Vector3(baseScale.x * (1f + sq), baseScale.y * (1f - sq), 1f);
                yield return null;
            }
            cat.tr.localScale = baseScale;
            t.nest.paw.enabled = false;
            Sfx("hole", 0.7f);
            Sfx("nest_in", 0.7f);
            AvatarReact("ip_like");
            foreach (var c in cats) // frozen cats thaw as other cats get home
            {
                if (c.done || c.ice <= 0) continue;
                c.ice--;
                c.iceText.text = c.ice > 0 ? c.ice.ToString() : "";
                c.iceBadge.enabled = c.ice > 0;
                if (c.ice > 0) { Play(c, IceAnim(c.ice), false, IceAnim(c.ice) + "_idle"); Sfx("ice", 0.7f); }
                else { Play(c, "unfreeze", false); c.anim.AnimationState.AddAnimation(0, "front_idle1", true, 0); Sfx("unfreeze", 0.8f); }
            }
            Play(cat, "select_to_sit", false, "front_idle1");
            for (int k = 0; k < 7; k++) StartCoroutine(Sparkle(t.nest.tr.position, k * Mathf.PI * 2f / 7f));
            StartCoroutine(Praise());
        }

        // Consume levels: every block the cat walked over vanishes once it is home, freeing its cells
        // (same rule as Tools/DeepSolver: the next cats only have the blocks that are left).
        bool consumeLevel;

        void ConsumeRoute(Trace t)
        {
            var used = new HashSet<int>();
            foreach (var c in t.cells) { int i = occ[c.x, c.y]; if (i >= 0) used.Add(i); }
            int n = 0;
            foreach (int i in used)
            {
                Lift(i);
                StartCoroutine(Vanish(pieces[i], n++));
            }
        }

        // The blocks pop one after another, each a little higher.
        IEnumerator Vanish(Piece p, int order)
        {
            yield return new WaitForSeconds(0.07f * order);
            SfxAt("vanish", 0.5f, 1f + 0.09f * order);
            var a = p.transform.localPosition;
            var center = a;
            if (p.offs.Length > 0)
            {
                var sum = Vector3.zero;
                foreach (var o in p.offs) sum += CellToWorld(p.anchor + o);
                center = sum / p.offs.Length;
            }
            for (int k = 0; k < 4; k++) StartCoroutine(Sparkle(center, k * Mathf.PI / 2f + 0.4f));
            var s0 = p.transform.localScale;
            for (float k = 0; k < 1f; k += Time.deltaTime / 0.35f)
            {
                float s = 1f - k * k;
                p.transform.localScale = s0 * s;
                p.transform.localPosition = center + (a - center) * s;
                yield return null;
            }
            p.gameObject.SetActive(false);
        }

        // Faint paw print left on the path, fading away.
        IEnumerator PawPrint(Vector3 at, Vector3 dir, int side)
        {
            dir.z = 0;
            if (dir.sqrMagnitude < 1e-6f) yield break;
            dir.Normalize();
            var side3 = new Vector3(-dir.y, dir.x) * 0.09f * side;
            var sr = MakeSprite("PawPrint", boardRoot, sprPaw, 0.2f, 30, at + side3 - new Vector3(0, 0.05f));
            sr.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f);
            for (float k = 0; k < 1f && sr; k += Time.deltaTime / 1.2f)
            {
                sr.color = new Color(1f, 0.95f, 0.85f, 0.55f * (1f - k));
                yield return null;
            }
            if (sr) Destroy(sr.gameObject);
        }

        // Level intro: the original violet banner drops in with the level number and name, then leaves.
        IEnumerator ShowBanner()
        {
            float Ease(float t) => 1f - Mathf.Pow(1f - t, 3f);
            for (float t = 0; t < 1f; t += Time.deltaTime / 0.35f) { banner.anchoredPosition = new Vector2(0, Mathf.Lerp(200f, -430f, Ease(t))); yield return null; }
            banner.anchoredPosition = new Vector2(0, -430f);
            yield return new WaitForSeconds(1.2f);
            for (float t = 0; t < 1f; t += Time.deltaTime / 0.3f) { banner.anchoredPosition = new Vector2(0, Mathf.Lerp(-430f, 200f, t * t)); yield return null; }
            banner.anchoredPosition = new Vector2(0, 200f);
        }

        IEnumerator Sparkle(Vector3 from, float angle)
        {
            var sr = MakeSprite("Sparkle", boardRoot, sprSparkle, 0.45f, 110, from);
            var dir = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle));
            var baseScale = sr.transform.localScale;
            for (float k = 0; k < 1f && sr; k += Time.deltaTime / 0.55f)
            {
                sr.transform.localPosition = from + dir * (0.9f * Mathf.Sqrt(k));
                sr.transform.localScale = baseScale * (1f - k * 0.7f);
                sr.color = new Color(1f, 0.95f, 0.6f, 1f - k);
                yield return null;
            }
            if (sr) Destroy(sr.gameObject);
        }

        static readonly string[] PraiseWords = { "Tuyệt!", "Giỏi lắm!", "Hay quá!", "Nice!" };

        float lastPraiseTime = -99f;
        int combo;
        GameObject praiseGo;

        // A praise word in the strip above the board (never over cats or blocks); cats arriving quickly one after
        // another count up a combo. The word has a real outline: 8 dark copies around it plus a drop shadow.
        IEnumerator Praise()
        {
            combo = Time.time - lastPraiseTime < 3f ? combo + 1 : 1;
            lastPraiseTime = Time.time;
            string word = combo > 1 ? $"Combo x{combo}!" : PraiseWords[UnityEngine.Random.Range(0, PraiseWords.Length)];
            if (praiseGo) Destroy(praiseGo);
            var root = new GameObject("Praise").transform;
            praiseGo = root.gameObject;
            root.SetParent(boardRoot, false);
            var pos = new Vector3(0, h / 2f + 0.75f);
            var outline = new Color(0.35f, 0.16f, 0.04f);
            var parts = new List<TextMesh>();
            for (int d = 0; d < 9; d++)
            {
                var off = d == 8 ? new Vector3(0.03f, -0.08f) : Quaternion.Euler(0, 0, d * 45f) * new Vector3(0.045f, 0);
                var tm = MakeText("Outline", root, off, 120);
                tm.color = outline;
                parts.Add(tm);
            }
            var text = MakeText("Word", root, Vector3.zero, 121);
            text.color = combo > 1 ? new Color(1f, 0.55f, 0.85f) : new Color(1f, 0.9f, 0.3f);
            parts.Add(text);
            foreach (var tm in parts) tm.text = word;
            for (float k = 0; k < 1f && root; k += Time.deltaTime / 1.1f)
            {
                float pop = k < 0.15f ? Mathf.Lerp(0.3f, 1.25f, k / 0.15f) : k < 0.3f ? Mathf.Lerp(1.25f, 1f, (k - 0.15f) / 0.15f) : 1f;
                root.localPosition = pos + new Vector3(0, k * 0.25f);
                root.localScale = Vector3.one * pop * 1.5f;
                float a = k < 0.75f ? 1f : 1f - (k - 0.75f) / 0.25f;
                foreach (var tm in parts) tm.color = new Color(tm.color.r, tm.color.g, tm.color.b, a);
                yield return null;
            }
            if (root) Destroy(root.gameObject);
        }

        int flowLit = -1, flowReady; // last lit cell count / finished roads (-1: level just loaded, stay quiet)

        void UpdateFlow()
        {
            foreach (var p in pieces) p.ResetFlow();
            foreach (var n in nests) n.ready = false;
            int lit = 0, ready = 0;
            foreach (var cat in cats)
            {
                if (cat.done || cat.waiting) continue;
                var tr = TraceFrom(cat);
                if (tr.success) { tr.nest.ready = true; ready++; }
                var cells = tr.cells;
                lit += cells.Count;
                for (int k = 0; k < cells.Count; k++)
                {
                    var p = pieces[occ[cells[k].x, cells[k].y]];
                    p.SetFlow(p.CellIndex(cells[k]), cat.color, k);
                }
            }
            // sound: a road just finished chimes; a road that got longer gives a soft rising note
            if (flowLit >= 0 && playing)
            {
                if (ready > flowReady) SfxAt("path_done", 0.5f, 1f);
                else if (lit > flowLit) SfxAt("path_grow", 0.35f, Mathf.Min(1.35f, 1f + 0.03f * lit));
            }
            flowLit = lit; flowReady = ready;
        }

        // ---------------------------------------------------------------- debug (internal testing)

        // Reloads the level and plays the scramble backwards (always a valid solution).
        IEnumerator AutoSolve()
        {
            LoadLevel(levelIndex);
            introPanel.SetActive(false);
            for (int s = scrambleSteps.Count - 1; s >= 0 && playing; s--)
            {
                while (busy || pieces.Any(p => p.gate && !p.gateOpen)) yield return null; // the solver assumes gates are open, so move like a player who waits for them
                var st = scrambleSteps[s];
                var piece = pieces[st.piece];
                if (st.rot) { piece.Rotate(true); OnDrop(st.piece, true); yield return new WaitForSeconds(0.2f); continue; }
                yield return ScriptedDrag(st.piece, st.from, 0.25f, null);
                OnDrop(st.piece);
            }
            for (float t = 0; t < 60f && playing; t += Time.deltaTime) yield return null;
        }

        // A drag played by code, with the same lift / hold / land as a finger. `shot` (testing) gets the phase names.
        IEnumerator ScriptedDrag(int i, Vector2Int to, float dur, Action<string> shot)
        {
            var piece = pieces[i];
            Vector3 a = piece.transform.localPosition, b = CellToWorld(to);
            piece.SetDragging(true);
            Sfx("block_pick", 0.5f);
            for (float k = 0; k < 1f; k += Time.deltaTime / dur)
            {
                piece.Hold(Vector3.Lerp(a, b, Mathf.SmoothStep(0f, 1f, k)));
                if (shot != null && k > 0.45f) { shot("mid"); shot = s => { }; }
                yield return null;
            }
            for (float t = 0; t < 0.05f; t += Time.deltaTime) { piece.Hold(b); yield return null; }
            var held = piece.HeldBase;
            Lift(i);
            Put(i, to);
            piece.Land(held);
            Sfx("drop", 0.75f);
        }

        // -drag: one drag of level 1 filmed in phases (lift, mid-drag, landing, settled) for checking the motion.
        IEnumerator FilmDrag(string dir)
        {
            LoadLevel(0);
            introPanel.SetActive(false);
            yield return new WaitForSeconds(1.2f);
            var st = scrambleSteps[scrambleSteps.Count - 1];
            var piece = pieces[st.piece];
            piece.SetDragging(true);
            yield return new WaitForSeconds(0.1f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "shot_drag_lift.png"));
            yield return new WaitForSeconds(0.2f);
            yield return ScriptedDrag(st.piece, st.from, 0.5f, n => ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "shot_drag_" + n + ".png")));
            yield return new WaitForSeconds(0.05f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "shot_drag_land.png"));
            OnDrop(st.piece);
            yield return new WaitForSeconds(0.6f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "shot_drag_end.png"));
            yield return new WaitForSeconds(0.5f);
            Application.Quit();
        }

        // Run the player with -autotest: solves every level and saves screenshots next to the exe.
        IEnumerator Start()
        {
            if (!Environment.GetCommandLineArgs().Contains("-autotest")) yield break;
            autotest = true; // don't save progress while testing
            string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, ".."));
            var args = Environment.GetCommandLineArgs().ToList();
            if (args.Contains("-drag")) { yield return FilmDrag(dir); yield break; }
            if (args.Contains("-front")) // loading + home screenshots only
            {
                front.gameObject.SetActive(true);
                StartCoroutine(front.RunLoading(Unlocked));
                yield return new WaitForSeconds(1.4f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "shot_loading.png"));
                yield return new WaitForSeconds(1.95f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "shot_loading_end.png"));
                yield return new WaitForSeconds(1.5f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "shot_home.png"));
                yield return new WaitForSeconds(0.3f);
                front.Hide();
                ShowMap();
                yield return new WaitForSeconds(0.5f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "shot_map.png"));
                yield return new WaitForSeconds(0.3f);
                Application.Quit();
                yield break;
            }
            int Arg(string name, int def) { int k = args.IndexOf(name); return k >= 0 && k + 1 < args.Count && int.TryParse(args[k + 1], out int v) ? v : def; }
            if (args.Contains("-demo")) // the New feature card for every mechanic, two moments each
            {
                LoadLevel(0);
                yield return new WaitForSeconds(1.5f);
                foreach (var f in new[] { "cross", "dead", "junction", "special", "rotate", "multicat", "oneway", "ice", "keylock", "inner", "frozen", "color", "fixed", "axis", "mystery", "bomb" })
                {
                    ShowIntro(f, f, "paw", "");
                    yield return new WaitForSeconds(1.6f);
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"shot_demo_{f}_a.png"));
                    yield return new WaitForSeconds(2.2f);
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"shot_demo_{f}_b.png"));
                    yield return new WaitForSeconds(0.2f);
                    introPanel.SetActive(false);
                    yield return new WaitForSeconds(0.3f);
                }
                Application.Quit();
                yield break;
            }
            if (args.Contains("-ui")) // settings + booster screenshots on one level, then quit
            {
                void Shot(string n) => ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"shot_ui_{n}.png"));
                LoadLevel(Arg("-from", 22) - 1);
                yield return new WaitForSeconds(2.2f);
                introPanel.SetActive(false);
                OpenSettings();
                yield return new WaitForSecondsRealtime(0.5f);
                Shot("settings");
                yield return new WaitForSecondsRealtime(0.3f);
                CloseSettings();
                // a move that leaves the solution, then the hint steps back and shows the right move
                var st0 = scrambleSteps[scrambleSteps.Count - 1];
                for (int i = 0; i < pieces.Count; i++)
                {
                    if (!pieces[i].Movable(keysCollected)) continue;
                    var opts = Reachable(i).Where(c => c != pieces[i].anchor && !(i == st0.piece && c == st0.from)).ToList();
                    if (opts.Count == 0) continue;
                    var alt = opts[0];
                    Lift(i); Put(i, alt); pieces[i].transform.localPosition = CellToWorld(alt); OnDrop(i);
                    Debug.Log($"AUTOTEST ui: wrong move piece {i} -> {alt}, progress {SolutionProgress()}");
                    break;
                }
                yield return new WaitForSeconds(1.0f);
                StartCoroutine(Hint());
                yield return new WaitForSeconds(2.0f);
                Debug.Log($"AUTOTEST ui: after hint progress {SolutionProgress()} hand {(hand && hand.gameObject.activeSelf)}");
                Shot("hint");
                yield return new WaitForSeconds(0.5f);
                SetPickCat(true);
                yield return new WaitForSeconds(0.3f);
                Shot("pick");
                var cat = cats.FirstOrDefault(c => !c.done && !c.waiting);
                SetPickCat(false);
                if (cat != null) StartCoroutine(SendHome(cat));
                yield return new WaitForSeconds(0.45f);
                Shot("fly");
                yield return new WaitForSeconds(1.5f);
                Shot("send");
                Debug.Log($"AUTOTEST ui: cats done {cats.Count(c => c.done)}/{cats.Count}");
                yield return new WaitForSeconds(0.3f);
                Application.Quit();
                yield break;
            }
            int from = Mathf.Max(1, Arg("-from", 1)), to = Mathf.Min(levels.Length, Arg("-to", levels.Length)); // optional level range, 1-based
            for (int i = from - 1; i < to; i++)
            {
                LoadLevel(i);
                yield return new WaitForSeconds(0.6f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"shot_{i + 1:00}_a.png"));
                yield return new WaitForSeconds(0.2f);
                var solving = StartCoroutine(AutoSolve());
                yield return new WaitForSeconds(2.5f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"shot_{i + 1:00}_m.png"));
                yield return solving;
                yield return new WaitForSeconds(1.6f);
                Debug.Log($"AUTOTEST level {i + 1}: {(!playing && panelIsWin ? "WIN" : "FAIL")} steps={scrambleSteps.Count} timeLeft={timeLeft:0}");
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"shot_{i + 1:00}_b.png"));
                yield return new WaitForSeconds(0.3f);
                if (args.Contains("-stars")) // the second part of the win screen
                {
                    yield return new WaitForSeconds(2.8f);
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"shot_{i + 1:00}_c.png"));
                    yield return new WaitForSeconds(0.3f);
                }
            }
            LoadLevel(0); // lose screen check: run out of time
            timeLeft = 0.3f;
            yield return new WaitForSeconds(1.5f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "shot_lose.png"));
            yield return new WaitForSeconds(0.3f);
            Application.Quit();
        }

        // ---------------------------------------------------------------- UI

        void UpdateHud()
        {
            titleText.text = "Màn " + (levelIndex + 1);
            int sec = Mathf.Max(0, Mathf.CeilToInt(timeLeft));
            timerText.text = $"{sec / 60}:{sec % 60:00}";
            timerText.color = sec <= 10 ? new Color(1f, 0.3f, 0.3f) : Color.white;
            timerPill.localScale = Vector3.one * (sec <= 10 && playing ? 1f + 0.08f * Mathf.Abs(Mathf.Sin(Time.time * 6f)) : 1f);
        }

        // ---------------------------------------------------------------- result screens (like Catdom: no frame)
        // Win: dark screen, "Hoàn thành!", three cats jumping with fireworks; then the stars (win / moves / time)
        // and "Tiếp tục". Lose: the reason, a broken heart (-1), hearts left, "Chơi lại" / "Về nhà".

        Text resultTitle, loseHeartsText, retryBtnText;
        RectTransform catsRoot, winGroup, loseGroup, brokenHeart;
        readonly Text[] starLabels = new Text[3];
        Sprite sprStarOn, sprStarOff;
        const float WinCatScale = 2.6f;

        IEnumerator ResultIn(bool win)
        {
            var title = resultTitle.rectTransform;
            for (float t = 0; t < 1f; t += Time.deltaTime / 0.35f)
            {
                title.localScale = Vector3.one * Mathf.LerpUnclamped(0.3f, 1f, 1f - Mathf.Pow(1f - t, 3f) + Mathf.Sin(t * Mathf.PI) * 0.2f);
                yield return null;
            }
            title.localScale = Vector3.one;
            if (!win)
            {
                loseGroup.gameObject.SetActive(true);
                yield return Pop(loseGroup, 0.3f);
                for (float t = 0; t < 1f && panel.activeSelf; t += Time.deltaTime / 0.6f) // the heart cracks and shakes
                {
                    brokenHeart.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * 30f) * 8f * (1f - t));
                    yield return null;
                }
                brokenHeart.localRotation = Quaternion.identity;
                yield break;
            }
            StartCoroutine(WinCatsJump());
            StartCoroutine(Fireworks());
            var colors = new[] { new Color(1f, 0.4f, 0.4f), new Color(1f, 0.85f, 0.3f), new Color(0.4f, 0.7f, 1f), new Color(0.5f, 0.9f, 0.4f), new Color(1f, 0.6f, 0.85f), new Color(0.75f, 0.55f, 1f) };
            for (int i = 0; i < 70; i++) StartCoroutine(Confetti(colors[i % colors.Length]));
            yield return new WaitForSeconds(1.9f);
            // the cats bow out, the stars come in
            for (float t = 0; t < 1f && panel.activeSelf; t += Time.deltaTime / 0.3f)
            {
                catsRoot.localScale = Vector3.one * (1f - t);
                yield return null;
            }
            catsRoot.gameObject.SetActive(false);
            backlight.gameObject.SetActive(false);
            panelBurst.enabled = false;
            winGroup.gameObject.SetActive(true);
            bool[] earned = { true, curPar <= 0 || movesMade <= curPar, curParTime <= 0 || timeTotal - timeLeft <= curParTime };
            int count = earned.Count(e => e);
            if (!autotest) { string key = "CatPipe_Stars_" + levelIndex; PlayerPrefs.SetInt(key, Mathf.Max(PlayerPrefs.GetInt(key, 0), count)); PlayerPrefs.Save(); }
            for (int k = 0; k < 3; k++)
            {
                panelStars[k].sprite = sprStarOff;
                panelStars[k].color = Color.white;
                starLabels[k].color = earned[k] ? Color.white : new Color(1f, 1f, 1f, 0.55f);
            }
            yield return Pop(winGroup, 0.3f);
            for (int k = 0; k < 3; k++)
            {
                if (!earned[k]) continue;
                yield return new WaitForSeconds(0.15f);
                SfxAt("star", 0.75f, 1f + 0.12f * k); // a coin chime per star, each one higher
                var rt = panelStars[k].rectTransform;
                panelStars[k].sprite = sprStarOn;
                for (float t = 0; t < 1f && panel.activeSelf; t += Time.deltaTime / 0.3f)
                {
                    rt.localScale = Vector3.one * (t < 0.6f ? Mathf.Lerp(0f, 1.35f, t / 0.6f) : Mathf.Lerp(1.35f, 1f, (t - 0.6f) / 0.4f));
                    yield return null;
                }
                rt.localScale = Vector3.one;
                for (int i = 0; i < 6; i++) StartCoroutine(StarSpark(rt, i * 60f));
            }
        }

        IEnumerator Pop(RectTransform rt, float dur)
        {
            for (float t = 0; t < 1f; t += Time.deltaTime / dur)
            {
                rt.localScale = Vector3.one * Mathf.LerpUnclamped(0.5f, 1f, 1f - Mathf.Pow(1f - t, 3f) + Mathf.Sin(t * Mathf.PI) * 0.12f);
                yield return null;
            }
            rt.localScale = Vector3.one;
        }

        // The three cats hop in a wave, each landing a little after the one before.
        IEnumerator WinCatsJump()
        {
            var basePos = winCats.Select(c => c.rectTransform.anchoredPosition).ToArray();
            for (int k = 0; k < winCats.Count; k++) winCats[k].AnimationState.SetAnimation(0, "jump1", true).TrackTime = k * 0.2f;
            for (float t = 0; panel.activeSelf && winCats.Count > 0 && winCats[0] && catsRoot.gameObject.activeSelf; t += Time.deltaTime)
            {
                for (int k = 0; k < winCats.Count; k++)
                {
                    float ph = Mathf.Max(0f, t - k * 0.18f) * Mathf.PI / 0.62f;
                    float hop = Mathf.Abs(Mathf.Sin(ph));
                    winCats[k].rectTransform.anchoredPosition = basePos[k] + new Vector2(0, 160f * hop);
                    winCats[k].rectTransform.localScale = new Vector3(1f, 1f + 0.08f * (hop - 0.5f), 1f) * WinCatScale; // stretch up, squash on landing
                }
                yield return null;
            }
        }

        // Firework bursts over the dark screen for a few seconds.
        IEnumerator Fireworks()
        {
            var colors = new[] { new Color(1f, 0.35f, 0.4f), new Color(1f, 0.85f, 0.25f), new Color(0.4f, 0.75f, 1f), new Color(0.55f, 1f, 0.45f), new Color(1f, 0.55f, 0.95f), new Color(0.8f, 0.6f, 1f) };
            for (float t = 0; t < 4f && panel.activeSelf; )
            {
                var at = new Vector2(UnityEngine.Random.Range(-470f, 470f), UnityEngine.Random.Range(250f, 820f));
                var col = colors[UnityEngine.Random.Range(0, colors.Length)];
                StartCoroutine(FireworkFlash(at, col));
                for (int i = 0; i < 28; i++) StartCoroutine(FireworkSpark(at, i * Mathf.PI * 2f / 28f, col));
                float wait = UnityEngine.Random.Range(0.3f, 0.6f);
                yield return new WaitForSeconds(wait);
                t += wait;
            }
        }

        Image BehindResult(Sprite sp, Vector2 at, float size)
        {
            var img = MakeImage(panel.transform, sp, new Vector2(0.5f, 0.5f), at, new Vector2(size, size));
            img.raycastTarget = false;
            img.rectTransform.SetSiblingIndex(0); // behind the cats and texts
            return img;
        }

        IEnumerator FireworkFlash(Vector2 at, Color col)
        {
            var img = BehindResult(sprGlow, at, 260f);
            for (float t = 0; t < 1f && panel.activeSelf; t += Time.deltaTime / 0.35f)
            {
                img.color = new Color(Mathf.Lerp(1f, col.r, t), Mathf.Lerp(1f, col.g, t), Mathf.Lerp(1f, col.b, t), 0.9f * (1f - t));
                img.rectTransform.localScale = Vector3.one * (0.5f + t);
                yield return null;
            }
            Destroy(img.gameObject);
        }

        IEnumerator FireworkSpark(Vector2 at, float angle, Color col)
        {
            var img = BehindResult(sprDisc, at, 26f);
            var vel = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * UnityEngine.Random.Range(330f, 480f);
            var pos = at;
            for (float t = 0; t < 1.2f && panel.activeSelf; t += Time.deltaTime)
            {
                vel *= 1f - 2.2f * Time.deltaTime; // air drag
                vel.y -= 260f * Time.deltaTime;  // then the sparks fall
                pos += vel * Time.deltaTime;
                img.rectTransform.anchoredPosition = pos;
                float a = 1f - t / 1.2f;
                img.color = new Color(col.r, col.g, col.b, a);
                img.rectTransform.localScale = Vector3.one * (0.6f + 0.6f * a) * (Mathf.Sin(t * 40f + angle * 5f) > -0.6f ? 1f : 0.6f); // twinkle
                yield return null;
            }
            Destroy(img.gameObject);
        }

        IEnumerator StarSpark(RectTransform star, float angle)
        {
            var img = MakeImage(star.parent, sprSparkle, new Vector2(0.5f, 0.5f), star.anchoredPosition, new Vector2(46, 46));
            img.raycastTarget = false;
            var at = star.anchoredPosition;
            var dir = (Vector2)(Quaternion.Euler(0, 0, angle) * Vector3.right);
            for (float t = 0; t < 1f && panel.activeSelf; t += Time.deltaTime / 0.45f)
            {
                img.rectTransform.anchoredPosition = at + dir * (130f * Mathf.Sqrt(t));
                img.color = new Color(1f, 0.95f, 0.6f, 1f - t);
                yield return null;
            }
            Destroy(img.gameObject);
        }

        IEnumerator Confetti(Color col)
        {
            var img = MakeImage(panel.transform, sprWhite, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(18, 30));
            img.color = col;
            img.raycastTarget = false;
            var rt = img.rectTransform;
            var pos = new Vector2(UnityEngine.Random.Range(-560f, 560f), UnityEngine.Random.Range(50f, 400f));
            var vel = new Vector2(UnityEngine.Random.Range(-120f, 120f), UnityEngine.Random.Range(-500f, -250f));
            float spin = UnityEngine.Random.Range(-400f, 400f), sway = UnityEngine.Random.Range(0f, 6f);
            for (float t = 0; t < 3.5f && panel.activeSelf; t += Time.deltaTime)
            {
                pos += (vel + new Vector2(Mathf.Sin(t * 4f + sway) * 120f, 0)) * Time.deltaTime;
                rt.anchoredPosition = pos;
                rt.Rotate(0, 0, spin * Time.deltaTime);
                rt.localScale = new Vector3(Mathf.Cos(t * 6f + sway), 1, 1);
                yield return null;
            }
            Destroy(img.gameObject);
        }

        void ShowPanel(bool win, string title)
        {
            playing = false;
            if (dragIdx >= 0)
            {
                Put(dragIdx, pieces[dragIdx].anchor);
                pieces[dragIdx].SetDragging(false);
                if (ghost) Destroy(ghost.transform.parent.gameObject);
                dragIdx = -1;
            }
            panelIsWin = win;
            if (win && !autotest && levelIndex + 2 > Unlocked) { PlayerPrefs.SetInt(UnlockedKey, levelIndex + 2); PlayerPrefs.Save(); }
            if (!win) LoseHeart();
            if (avatar) avatar.AnimationState.SetAnimation(0, win ? "ip_cheer" : "ip_worried_loop", true);
            resultTitle.text = win ? "Hoàn thành!" : title;
            resultTitle.color = win ? new Color(1f, 0.85f, 0.25f) : new Color(1f, 0.55f, 0.5f);
            foreach (var c in winCats) if (c) Destroy(c.gameObject);
            winCats.Clear();
            catsRoot.gameObject.SetActive(win);
            catsRoot.localScale = Vector3.one;
            winGroup.gameObject.SetActive(false);
            loseGroup.gameObject.SetActive(false);
            if (win)
            {
                // the level's own cat colors first, then random ones, three different cats in all
                var cols = cats.Select(c => c.color).Distinct().Take(3).ToList();
                while (cols.Count < 3) { int r = UnityEngine.Random.Range(1, 10); if (!cols.Contains(r)) cols.Add(r); }
                for (int k = 0; k < 3; k++)
                    winCats.Add(front.MakeCat(catsRoot, CatSkins[cols[k]], new Vector2((k - 1) * 280, k == 1 ? 40 : -20), WinCatScale));
                starLabels[0].text = "Qua màn";
                starLabels[1].text = curPar > 0 ? $"≤ {curPar} nước" : "Nước đi";
                starLabels[2].text = curParTime > 0 ? $"≤ {curParTime / 60}:{curParTime % 60:00}" : "Thời gian";
            }
            else
            {
                int hearts = Hearts;
                loseHeartsText.text = hearts > 0 ? $"Còn {hearts} tim" : "Hết tim rồi!";
                retryBtnText.text = hearts > 0 ? "Chơi lại" : "Nạp tim";
            }
            panelBurst.enabled = win;
            backlight.gameObject.SetActive(win);
            panel.SetActive(true);
            StartCoroutine(ResultIn(win));
            Sfx(win ? "win" : "lose", 0.9f, 0f); // one sound only: confetti / firework clips on top of it were too much
        }

        // ---------------------------------------------------------------- hearts (5, one back every minute while testing)
        const int MaxHearts = 5;
        const double HeartRefillSec = 60;
        const string HeartsKey = "CatPipe_Hearts", HeartTimeKey = "CatPipe_HeartTime";
        GameObject heartsWidget, noHeartsPanel;
        Text heartsCountText, heartsTimerText, noHeartsTimerText;

        static double NowSec() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

        double HeartClock => double.TryParse(PlayerPrefs.GetString(HeartTimeKey, ""), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : NowSec();

        int Hearts
        {
            get
            {
                int h = PlayerPrefs.GetInt(HeartsKey, MaxHearts);
                if (h >= MaxHearts) return MaxHearts;
                double t0 = HeartClock, now = NowSec();
                int gained = (int)((now - t0) / HeartRefillSec);
                if (gained > 0)
                {
                    h = Mathf.Min(MaxHearts, h + gained);
                    PlayerPrefs.SetInt(HeartsKey, h);
                    PlayerPrefs.SetString(HeartTimeKey, (t0 + gained * HeartRefillSec).ToString(System.Globalization.CultureInfo.InvariantCulture));
                    PlayerPrefs.Save();
                }
                return h;
            }
        }

        // Seconds until the next heart comes back (0 when full).
        int NextHeartIn => Hearts >= MaxHearts ? 0 : Mathf.Max(0, (int)Math.Ceiling(HeartRefillSec - (NowSec() - HeartClock)));

        void LoseHeart()
        {
            if (autotest) return;
            int h = Hearts;
            if (h >= MaxHearts) PlayerPrefs.SetString(HeartTimeKey, NowSec().ToString(System.Globalization.CultureInfo.InvariantCulture));
            PlayerPrefs.SetInt(HeartsKey, Mathf.Max(0, h - 1));
            PlayerPrefs.Save();
        }

        void FillHearts()
        {
            PlayerPrefs.SetInt(HeartsKey, MaxHearts);
            PlayerPrefs.Save();
        }

        // Every way into a level goes through here: no hearts, no play.
        void TryPlay(int idx)
        {
            if (!autotest && Hearts <= 0) { ShowNoHearts(); return; }
            LoadLevel(idx);
        }

        // The retry button inside a level: giving up a level you already started costs a heart.
        void RetryLevel()
        {
            if (playing && movesMade > 0) LoseHeart();
            TryPlay(levelIndex);
        }

        void ShowNoHearts()
        {
            noHeartsPanel.SetActive(true);
            StartCoroutine(Pop((RectTransform)noHeartsPanel.transform.GetChild(0), 0.3f));
        }

        static string Clock(int sec) => $"{sec / 60}:{sec % 60:00}";

        void UpdateHearts()
        {
            bool show = (map && map.gameObject.activeInHierarchy) || (front && front.gameObject.activeInHierarchy && front.HomeVisible);
            if (heartsWidget.activeSelf != show) heartsWidget.SetActive(show);
            if (!show && !noHeartsPanel.activeSelf) return;
            int h = Hearts, next = NextHeartIn;
            heartsCountText.text = h.ToString();
            heartsTimerText.text = h >= MaxHearts ? "Đầy" : Clock(next);
            if (noHeartsPanel.activeSelf)
            {
                noHeartsTimerText.text = h > 0 ? "Đã có tim, chơi tiếp thôi!" : $"Tim mới sau {Clock(next)}";
            }
        }

        void BuildHeartsUI(Transform root)
        {
            // pill at the top right of the map and home: heart, count, refill timer, "+" fills up (internal testing)
            var pill = MakeImage(root, Load("ui_timer"), new Vector2(1, 1), new Vector2(-215, -95), new Vector2(330, 100));
            pill.type = Image.Type.Sliced;
            heartsWidget = pill.gameObject;
            MakeImage(pill.transform, Load("ui_heart"), new Vector2(0, 0.5f), new Vector2(10, 4), new Vector2(110, 100)).preserveAspect = true;
            heartsCountText = MakeUIText(pill.transform, "5", 52, TextAnchor.MiddleCenter, new Vector2(0, 0.5f), new Vector2(10, 8), new Vector2(110, 80));
            heartsCountText.rectTransform.pivot = new Vector2(0.5f, 0.5f); // the count sits inside the heart
            heartsCountText.GetComponent<Outline>().effectColor = new Color(0.55f, 0.05f, 0.1f);
            heartsTimerText = MakeUIText(pill.transform, "Đầy", 44, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(30, 0), new Vector2(170, 80));
            var plus = MakeImage(pill.transform, Load("ui_heart_plus"), new Vector2(1, 0.5f), new Vector2(0, 0), new Vector2(84, 84));
            plus.preserveAspect = true;
            plus.gameObject.AddComponent<Button>().onClick.AddListener(() => { Sfx("click"); FillHearts(); });

            noHeartsPanel = new GameObject("NoHearts", typeof(RectTransform), typeof(Image));
            noHeartsPanel.transform.SetParent(root, false);
            var nrt = (RectTransform)noHeartsPanel.transform;
            nrt.anchorMin = Vector2.zero; nrt.anchorMax = Vector2.one; nrt.offsetMin = nrt.offsetMax = Vector2.zero;
            noHeartsPanel.GetComponent<Image>().color = new Color(0, 0, 0, 0.6f);
            var card = MakeImage(noHeartsPanel.transform, sprPopup, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(780, 820));
            card.type = Image.Type.Sliced;
            var title = MakeUIText(card.transform, "Hết tim!", 90, TextAnchor.MiddleCenter, new Vector2(0.5f, 1), new Vector2(0, -60), new Vector2(700, 130));
            title.color = new Color(0.85f, 0.25f, 0.3f);
            title.GetComponent<Outline>().effectColor = new Color(1f, 1f, 1f, 0.8f);
            MakeImage(card.transform, Load("ui_heart_broken"), new Vector2(0.5f, 0.5f), new Vector2(0, 100), new Vector2(280, 230)).preserveAspect = true;
            noHeartsTimerText = MakeUIText(card.transform, "", 48, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0, -60), new Vector2(700, 80));
            noHeartsTimerText.color = new Color(0.45f, 0.25f, 0.15f);
            noHeartsTimerText.GetComponent<Outline>().effectColor = new Color(1f, 1f, 1f, 0.6f);
            var fill = MakeImage(card.transform, Load("ui_btn_play"), new Vector2(0.5f, 0), new Vector2(0, 230), new Vector2(560, 150));
            fill.type = Image.Type.Sliced;
            fill.gameObject.AddComponent<Button>().onClick.AddListener(() => { Sfx("click"); FillHearts(); noHeartsPanel.SetActive(false); });
            MakeUIText(fill.transform, "Nạp đầy tim (test)", 50, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0, 6), new Vector2(540, 120))
                .GetComponent<Outline>().effectColor = new Color(0.1f, 0.35f, 0f, 0.9f);
            var close = MakeImage(card.transform, Load("ui_btn_orange"), new Vector2(0.5f, 0), new Vector2(0, 90), new Vector2(360, 110));
            close.type = Image.Type.Sliced;
            close.gameObject.AddComponent<Button>().onClick.AddListener(() => { Sfx("click"); noHeartsPanel.SetActive(false); });
            MakeUIText(close.transform, "Đóng", 50, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0, 4), new Vector2(340, 100));
            noHeartsPanel.SetActive(false);
            heartsWidget.SetActive(false);
        }

        // ---------------------------------------------------------------- boosters (free while testing)

        RectTransform hintBtn, sendBtn;
        bool pickCat; // "Về tổ" armed: the next tap on a cat sends it home
        string levelHint; // the bottom bar's text for this level
        Vector2Int[] startAt, prevAt;
        int[] startMask;
        readonly List<Step> history = new List<Step>(); // every move made, so the hint can step back to the solution

        // Round Catdom booster button on the bottom bar: icon, name, and a green "free" tag.
        RectTransform BoosterButton(Transform parent, Sprite icon, string label, float x, Action onTap)
        {
            var bg = MakeImage(parent, Load("ui_booster_bg"), new Vector2(0.5f, 0.5f), new Vector2(x, 30), new Vector2(160, 160));
            bg.gameObject.AddComponent<Button>().onClick.AddListener(() => { Sfx("click"); onTap(); });
            var ic = MakeImage(bg.transform, icon, new Vector2(0.5f, 0.5f), new Vector2(0, 8), new Vector2(105, 105));
            ic.preserveAspect = true;
            ic.raycastTarget = false;
            var lbl = MakeUIText(bg.transform, label, 38, TextAnchor.MiddleCenter, new Vector2(0.5f, 0), new Vector2(0, 0), new Vector2(200, 60));
            lbl.GetComponent<Outline>().effectColor = new Color(0.35f, 0.16f, 0.04f);
            lbl.raycastTarget = false;
            var tag = MakeImage(bg.transform, sprDisc, new Vector2(1, 1), new Vector2(-14, -14), new Vector2(58, 58));
            tag.color = new Color(0.3f, 0.75f, 0.15f);
            tag.raycastTarget = false;
            var inf = MakeUIText(tag.transform, "∞", 44, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0, 2), new Vector2(60, 60));
            inf.raycastTarget = false;
            return bg.rectTransform;
        }

        // The hint booster is locked while it is reworked: a tap only says so.
        IEnumerator HintLocked()
        {
            if (!playing || pickCat) yield break;
            Sfx("block_locked", 0.6f);
            hintBtn.localScale = Vector3.one;
            hintText.text = "Gợi ý tạm khóa";
            for (float t = 0; t < 0.4f; t += Time.deltaTime)
            {
                hintBtn.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * 50f) * 8f * (1f - t / 0.4f));
                yield return null;
            }
            hintBtn.localRotation = Quaternion.identity;
            yield return new WaitForSeconds(1.2f);
            if (hintText.text == "Gợi ý tạm khóa") hintText.text = levelHint;
        }

        static int Rotl(int m) => ((m << 1) | (m >> 3)) & 15;

        // How many solution moves the board is into (-1: the player left the solution).
        int SolutionProgress()
        {
            var at = startAt.ToArray();
            var mk = startMask.ToArray();
            bool Same()
            {
                for (int i = 0; i < pieces.Count; i++)
                    if (pieces[i].anchor != at[i] || (pieces[i].rotatable && pieces[i].masks[0] != mk[i])) return false;
                return true;
            }
            int best = Same() ? 0 : -1;
            for (int j = 0; j < scrambleSteps.Count; j++)
            {
                var st = scrambleSteps[scrambleSteps.Count - 1 - j];
                if (st.rot) mk[st.piece] = Rotl(mk[st.piece]);
                else at[st.piece] = st.from;
                if (Same()) best = j + 1;
            }
            return best;
        }

        // Hint: if the board left the solution, step the last moves back until it is on it again; then the hand shows the next move.
        IEnumerator Hint()
        {
            if (!playing || busy || dragIdx >= 0 || tutorialOn || introPanel.activeSelf) yield break;
            SetPickCat(false);
            int k = SolutionProgress();
            if (k < 0)
            {
                busy = true;
                while (k < 0 && history.Count > 0)
                {
                    var st = history[history.Count - 1];
                    history.RemoveAt(history.Count - 1);
                    var p = pieces[st.piece];
                    if (st.rot) { p.Rotate(false); p.Rotate(false); p.Rotate(true); } // three quarters = one back
                    else
                    {
                        Vector3 a = p.transform.localPosition, b = CellToWorld(st.from);
                        for (float t = 0; t < 1f; t += Time.deltaTime / 0.18f)
                        {
                            p.transform.localPosition = Vector3.Lerp(a, b, Mathf.SmoothStep(0, 1, t));
                            yield return null;
                        }
                        Lift(st.piece);
                        Put(st.piece, st.from);
                    }
                    prevAt[st.piece] = st.from;
                    Sfx("drop", 0.5f);
                    UpdateFlow();
                    k = SolutionProgress();
                    yield return new WaitForSeconds(0.08f);
                }
                busy = false;
                RequestResolve();
                if (k < 0) yield break;
            }
            if (k >= scrambleSteps.Count || !playing) yield break;
            tutStep = scrambleSteps.Count - 1 - k;
            tutorialOn = hintOnce = true;
            ShowHand();
        }

        void SetPickCat(bool on)
        {
            if (hintText == null || pickCat == on) return;
            pickCat = on;
            hintText.text = on ? "Chạm một chú mèo để đưa về tổ" : levelHint;
        }

        void HandlePickCat()
        {
            if (!Input.GetMouseButtonDown(0) || busy) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            var wp = cam.ScreenToWorldPoint(Input.mousePosition);
            wp.z = 0;
            Cat best = null;
            float bestD = 0.75f;
            foreach (var c in cats)
            {
                if (c.done || c.waiting) continue;
                float dd = Vector3.Distance(wp, c.tr.position + new Vector3(0, 0.35f));
                if (dd < bestD) { bestD = dd; best = c; }
            }
            if (best == null) { Sfx("block_locked", 0.6f); return; }
            SetPickCat(false);
            StartCoroutine(SendHome(best));
        }

        // The cat leaps straight into a free hole of its color.
        IEnumerator SendHome(Cat cat)
        {
            var nest = nests.FirstOrDefault(n => !n.occupied && n.color == cat.color);
            if (nest == null) yield break;
            busy = true;
            if (cat.ice > 0)
            {
                cat.ice = 0;
                if (cat.iceText) cat.iceText.text = "";
                if (cat.iceBadge) cat.iceBadge.enabled = false;
            }
            yield return Walk(cat, new Trace { nest = nest, success = true }, true);
            busy = false;
            UpdateFlow();
            RequestResolve(); // also checks the win
        }

        // ---------------------------------------------------------------- settings popup (pauses the level)

        GameObject settingsPanel;
        Image soundBtn;
        Text soundText;
        const string SoundKey = "CatPipe_Sound";
        bool SoundOn => PlayerPrefs.GetInt(SoundKey, 1) == 1;

        void OpenSettings()
        {
            if (!playing || panel.activeSelf) return;
            SetPickCat(false);
            RefreshSound();
            settingsPanel.SetActive(true);
            Time.timeScale = 0f;
        }

        void CloseSettings()
        {
            if (settingsPanel) settingsPanel.SetActive(false);
            Time.timeScale = 1f;
        }

        void RefreshSound()
        {
            soundBtn.sprite = Load(SoundOn ? "ui_btn_play" : "ui_btn_orange");
            soundText.text = SoundOn ? "Âm thanh: Bật" : "Âm thanh: Tắt";
        }

        // Leaving a level you already started costs a heart, like retrying it.
        void LeaveLevel(Action go)
        {
            if (playing && movesMade > 0) LoseHeart();
            CloseSettings();
            go();
        }

        void BuildSettingsUI(Transform root)
        {
            var center = new Vector2(0.5f, 0.5f);
            settingsPanel = new GameObject("Settings", typeof(RectTransform), typeof(Image), typeof(Button));
            settingsPanel.transform.SetParent(root, false);
            var srt = (RectTransform)settingsPanel.transform;
            srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one; srt.offsetMin = srt.offsetMax = Vector2.zero;
            settingsPanel.GetComponent<Image>().color = new Color(0, 0, 0, 0.6f);
            settingsPanel.GetComponent<Button>().onClick.AddListener(() => { Sfx("click"); CloseSettings(); }); // tap outside = resume
            var card = MakeImage(settingsPanel.transform, sprPopup, center, new Vector2(0, 0), new Vector2(780, 1100));
            card.type = Image.Type.Sliced;
            card.gameObject.AddComponent<Button>().transition = Selectable.Transition.None; // taps on the card don't close it
            var ribbon = MakeImage(card.transform, sprRibbonBlue, new Vector2(0.5f, 1), new Vector2(0, 40), new Vector2(880, 300));
            ribbon.preserveAspect = true;
            ribbon.raycastTarget = false;
            MakeUIText(ribbon.transform, "Cài đặt", 76, TextAnchor.MiddleCenter, center, new Vector2(0, 30), new Vector2(700, 120)).raycastTarget = false;

            Image Row(string sprite, float y, string text, Action onTap, out Text label)
            {
                var b = MakeImage(card.transform, Load(sprite), center, new Vector2(0, y), new Vector2(560, 140));
                b.type = Image.Type.Sliced;
                b.gameObject.AddComponent<Button>().onClick.AddListener(() => { Sfx("click"); onTap(); });
                label = MakeUIText(b.transform, text, 58, TextAnchor.MiddleCenter, center, new Vector2(0, 6), new Vector2(540, 120));
                label.raycastTarget = false;
                return b;
            }
            soundBtn = Row("ui_btn_play", 270, "", () =>
            {
                PlayerPrefs.SetInt(SoundKey, SoundOn ? 0 : 1);
                PlayerPrefs.Save();
                AudioListener.volume = SoundOn ? 1f : 0f;
                RefreshSound();
                if (SoundOn) Sfx("click");
            }, out soundText);
            var spk = MakeImage(soundBtn.transform, Load("ui_sound"), new Vector2(0, 0.5f), new Vector2(75, 6), new Vector2(64, 64));
            spk.preserveAspect = true;
            spk.raycastTarget = false;
            soundText.rectTransform.anchoredPosition = new Vector2(55, 6); // text right of the speaker, not over it
            soundText.rectTransform.sizeDelta = new Vector2(400, 120);
            soundText.fontSize = 52;
            Row("ui_btn_play", 110, "Tiếp tục", CloseSettings, out _);
            Row("ui_btn_orange", -50, "Chơi lại", () => { CloseSettings(); RetryLevel(); }, out _);
            Row("ui_btn_orange", -210, "Chọn màn", () => LeaveLevel(ShowMap), out _);
            Row("ui_btn_orange", -370, "Về nhà", () => LeaveLevel(ShowHome), out _);
            MakeUIText(card.transform, "Chơi lại hoặc thoát giữa màn: mất 1 tim", 36, TextAnchor.MiddleCenter, center, new Vector2(0, -475), new Vector2(700, 60)).color = new Color(0.55f, 0.3f, 0.2f);
            settingsPanel.SetActive(false);
        }


        void BuildUI()
        {
            var canvasGo = new GameObject("UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            var root = canvasGo.transform;
            uiRoot = root;

            if (FindObjectOfType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            // HUD like Catdom: cat avatar, purple level pill with the timer under it, orange retry button.
            var levelPill = MakeImage(root, Load("ui_level"), new Vector2(0.5f, 1), new Vector2(0, -105), new Vector2(400, 140));
            levelPill.type = Image.Type.Sliced;
            titleText = MakeUIText(levelPill.transform, "", 62, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0, 6), new Vector2(380, 120));
            var timerImg = MakeImage(root, Load("ui_timer"), new Vector2(0.5f, 1), new Vector2(15, -222), new Vector2(250, 88));
            timerImg.type = Image.Type.Sliced;
            timerPill = timerImg.rectTransform;
            MakeImage(timerPill.transform, Load("ui_clock"), new Vector2(0, 0.5f), new Vector2(0, 6), new Vector2(100, 110)).preserveAspect = true;
            timerText = MakeUIText(timerPill.transform, "", 52, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(25, 0), new Vector2(200, 80));
            var gear = MakeImage(root, Load("ui_btn_orange"), new Vector2(1, 1), new Vector2(-115, -120), new Vector2(140, 140));
            gear.type = Image.Type.Sliced;
            gear.gameObject.AddComponent<Button>().onClick.AddListener(() => { Sfx("click"); OpenSettings(); });
            MakeImage(gear.transform, Load("ui_setting"), new Vector2(0.5f, 0.5f), new Vector2(0, 4), new Vector2(92, 92)).raycastTarget = false;
            var bottom = MakeImage(root, Load("ui_bottom"), new Vector2(0.5f, 0), new Vector2(0, 95), new Vector2(1180, 190));
            bottom.raycastTarget = false;
            hintIcon = MakeImage(bottom.transform, sprPaw, new Vector2(0.5f, 0.5f), new Vector2(-190, 10), new Vector2(90, 90));
            hintIcon.preserveAspect = true;
            hintIcon.raycastTarget = false;
            hintText = MakeUIText(bottom.transform, "", 48, TextAnchor.MiddleLeft, new Vector2(0.5f, 0.5f), new Vector2(90, 8), new Vector2(430, 120));
            hintBtn = BoosterButton(bottom.transform, Load("ui_wand"), "Gợi ý", -430, () => StartCoroutine(HintLocked()));
            // hint is locked for now: grey icon, padlock, no "free" tag
            hintBtn.GetChild(0).GetComponent<Image>().color = new Color(0.55f, 0.55f, 0.55f, 0.8f);
            hintBtn.GetChild(2).gameObject.SetActive(false);
            var hintLock = MakeImage(hintBtn, sprLock, new Vector2(0.5f, 0.5f), new Vector2(38, 30), new Vector2(70, 70));
            hintLock.preserveAspect = true;
            hintLock.raycastTarget = false;
            sendBtn = BoosterButton(bottom.transform, sprCatWin, "Về tổ", 430, () => SetPickCat(!pickCat));
            hintText.color = new Color(1f, 0.96f, 0.86f);
            hintText.horizontalOverflow = HorizontalWrapMode.Wrap;
            hintText.GetComponent<Outline>().effectColor = new Color(0.3f, 0.15f, 0.05f, 0.9f);
            var bannerImg = MakeImage(root, Load("ui_banner"), new Vector2(0.5f, 1), new Vector2(0, 200), new Vector2(1000, 142));
            bannerImg.raycastTarget = false;
            banner = bannerImg.rectTransform;
            bannerText = MakeUIText(banner, "", 54, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0, 4), new Vector2(940, 120));

            panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(root, false);
            var prt = (RectTransform)panel.transform;
            prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one; prt.offsetMin = prt.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(0.05f, 0.03f, 0.1f, 0.86f);
            sprRibbonBlue = Load("ui_ribbon_blue");
            var center = new Vector2(0.5f, 0.5f);
            panelBurst = MakeImage(panel.transform, Load("ui_burst"), center, new Vector2(0, 100), new Vector2(1000, 1000));
            panelBurst.color = new Color(1f, 0.92f, 0.6f, 0.5f);
            panelBurst.raycastTarget = false;
            backlight = MakeImage(panel.transform, Load("ui_backlight"), center, new Vector2(0, 100), new Vector2(900, 900)).rectTransform;
            backlight.GetComponent<Image>().color = new Color(1f, 0.9f, 0.5f, 0.6f);
            backlight.GetComponent<Image>().raycastTarget = false;
            resultTitle = MakeUIText(panel.transform, "", 124, TextAnchor.MiddleCenter, center, new Vector2(0, 620), new Vector2(1000, 180));
            resultTitle.verticalOverflow = VerticalWrapMode.Overflow;
            var titleOutline = resultTitle.GetComponent<Outline>();
            titleOutline.effectColor = new Color(0.45f, 0.2f, 0.55f);
            titleOutline.effectDistance = new Vector2(6, -6);
            var titleShadow = resultTitle.gameObject.AddComponent<Shadow>();
            titleShadow.effectColor = new Color(0.25f, 0.1f, 0.35f, 0.9f);
            titleShadow.effectDistance = new Vector2(0, -12);
            catsRoot = Group("WinCats", new Vector2(0, -60));

            // after the cats: the three stars (win / moves / time) and Continue
            winGroup = Group("WinGroup", Vector2.zero);
            sprStarOn = Load("map_star"); sprStarOff = Load("ui_star_off");
            for (int k = 0; k < 3; k++)
            {
                panelStars[k] = MakeImage(winGroup, sprStarOff, center, new Vector2((k - 1) * 290, k == 1 ? 250 : 210), Vector2.one * (k == 1 ? 240 : 200));
                panelStars[k].preserveAspect = true;
                panelStars[k].raycastTarget = false;
                starLabels[k] = MakeUIText(winGroup, "", 50, TextAnchor.MiddleCenter, center, new Vector2((k - 1) * 290, 60), new Vector2(290, 80));
                starLabels[k].GetComponent<Outline>().effectColor = new Color(0.2f, 0.1f, 0.3f, 0.9f);
            }
            var cont = MakeImage(winGroup, Load("ui_btn_play"), center, new Vector2(0, -230), new Vector2(560, 200));
            cont.type = Image.Type.Sliced;
            cont.gameObject.AddComponent<Button>().onClick.AddListener(() => { Sfx("click"); ShowMap(); });
            MakeUIText(cont.transform, "Tiếp tục", 80, TextAnchor.MiddleCenter, center, new Vector2(0, 8), new Vector2(540, 140))
                .GetComponent<Outline>().effectColor = new Color(0.1f, 0.35f, 0f, 0.9f);

            // lose: a broken heart (-1), hearts left, retry / home
            loseGroup = Group("LoseGroup", Vector2.zero);
            var heartImg = MakeImage(loseGroup, Load("ui_heart_broken"), center, new Vector2(0, 250), new Vector2(400, 330));
            heartImg.preserveAspect = true;
            brokenHeart = heartImg.rectTransform;
            var minus = MakeUIText(loseGroup, "-1", 120, TextAnchor.MiddleCenter, center, new Vector2(230, 380), new Vector2(240, 160));
            minus.verticalOverflow = VerticalWrapMode.Overflow;
            minus.GetComponent<Outline>().effectColor = new Color(0.6f, 0.05f, 0.1f);
            minus.GetComponent<Outline>().effectDistance = new Vector2(5, -5);
            loseHeartsText = MakeUIText(loseGroup, "", 64, TextAnchor.MiddleCenter, center, new Vector2(0, 30), new Vector2(800, 100));
            var retryB = MakeImage(loseGroup, Load("ui_btn_play"), center, new Vector2(0, -190), new Vector2(560, 190));
            retryB.type = Image.Type.Sliced;
            retryB.gameObject.AddComponent<Button>().onClick.AddListener(() => { Sfx("click"); TryPlay(levelIndex); });
            retryBtnText = MakeUIText(retryB.transform, "Chơi lại", 76, TextAnchor.MiddleCenter, center, new Vector2(0, 8), new Vector2(540, 140));
            retryBtnText.GetComponent<Outline>().effectColor = new Color(0.1f, 0.35f, 0f, 0.9f);
            var homeB = MakeImage(loseGroup, Load("ui_btn_orange"), center, new Vector2(0, -390), new Vector2(420, 140));
            homeB.type = Image.Type.Sliced;
            homeB.gameObject.AddComponent<Button>().onClick.AddListener(() => { Sfx("click"); ShowHome(); });
            MakeUIText(homeB.transform, "Về nhà", 60, TextAnchor.MiddleCenter, center, new Vector2(0, 4), new Vector2(400, 120));
            panel.SetActive(false);

            // "New feature" card: icon, name, one line on how it works, Play (or tap anywhere)
            introPanel = new GameObject("IntroPanel", typeof(RectTransform), typeof(Image));
            introPanel.transform.SetParent(root, false);
            var irt = (RectTransform)introPanel.transform;
            irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one; irt.offsetMin = irt.offsetMax = Vector2.zero;
            introPanel.GetComponent<Image>().color = new Color(0.04f, 0.02f, 0.08f, 0.72f);
            // "Thử cái mới": the mechanic's name on a purple pill with its icon, a live demo window (real blocks and cats
            // filmed by the demo stage), two numbered tips that light up as the demo reaches them, and "Thử ngay!".
            introGlow = MakeImage(introPanel.transform, Load("ui_backlight"), center, new Vector2(0, 160), new Vector2(1300, 1300)).rectTransform;
            introGlow.GetComponent<Image>().color = new Color(1f, 0.85f, 0.45f, 0.35f);
            introGlow.GetComponent<Image>().raycastTarget = false;
            var card = MakeImage(introPanel.transform, sprPopup, center, new Vector2(0, 0), new Vector2(880, 1300));
            card.type = Image.Type.Sliced;
            introCard = card.rectTransform;
            // title: purple pill, icon in a white bubble on its left, a red "MỚI" tag
            var pill = MakeImage(card.transform, Load("ui_level"), new Vector2(0.5f, 1), new Vector2(-40, -20), new Vector2(660, 150));
            pill.type = Image.Type.Sliced;
            introName = MakeUIText(pill.transform, "", 74, TextAnchor.MiddleCenter, center, new Vector2(40, 6), new Vector2(540, 120));
            introName.GetComponent<Outline>().effectColor = new Color(0.25f, 0.08f, 0.4f);
            introName.resizeTextForBestFit = true;
            introName.resizeTextMinSize = 44; introName.resizeTextMaxSize = 74;
            var bubble = MakeImage(pill.transform, sprDisc, new Vector2(0, 0.5f), new Vector2(10, 4), new Vector2(170, 170));
            bubble.color = new Color(1f, 0.97f, 0.9f);
            bubble.raycastTarget = false;
            introIconImg = MakeImage(bubble.transform, sprPaw, center, Vector2.zero, new Vector2(115, 115));
            introIconImg.preserveAspect = true;
            introIconImg.raycastTarget = false;
            var tag = MakeImage(pill.transform, sprDisc, new Vector2(0, 1), new Vector2(150, 0), new Vector2(96, 96));
            tag.color = new Color(0.92f, 0.25f, 0.3f);
            tag.raycastTarget = false;
            introTag = tag.rectTransform;
            MakeUIText(tag.transform, "MỚI", 32, TextAnchor.MiddleCenter, center, new Vector2(0, 2), new Vector2(110, 60)).raycastTarget = false;
            // the demo window: a wooden frame like the board's
            var frame = MakeImage(card.transform, sprBoardFrame, center, new Vector2(0, 175), new Vector2(790, 600));
            frame.type = Image.Type.Sliced;
            frame.pixelsPerUnitMultiplier = 0.5f;
            frame.color = new Color(0.72f, 0.45f, 0.26f);
            frame.raycastTarget = false;
            var demoGo = new GameObject("Demo", typeof(RectTransform), typeof(RawImage));
            demoGo.transform.SetParent(frame.transform, false);
            demoImage = demoGo.GetComponent<RawImage>();
            demoImage.texture = demo.Texture;
            demoImage.raycastTarget = false;
            ((RectTransform)demoGo.transform).sizeDelta = new Vector2(744, 558);
            // two tips: number disc + short line; the current one is bright
            for (int k = 0; k < 2; k++)
            {
                var row = new GameObject("Tip" + (k + 1), typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
                row.SetParent(card.transform, false);
                row.anchoredPosition = new Vector2(0, -215 - k * 125);
                row.sizeDelta = new Vector2(760, 110);
                tipRows[k] = row.GetComponent<CanvasGroup>();
                var num = MakeImage(row, sprDisc, new Vector2(0, 0.5f), new Vector2(0, 0), new Vector2(92, 92));
                num.color = new Color(1f, 0.6f, 0.15f);
                num.raycastTarget = false;
                tipNums[k] = num.rectTransform;
                MakeUIText(num.transform, (k + 1).ToString(), 56, TextAnchor.MiddleCenter, center, new Vector2(0, 3), new Vector2(92, 92))
                    .GetComponent<Outline>().effectColor = new Color(0.55f, 0.25f, 0f);
                tipTexts[k] = MakeUIText(row, "", 54, TextAnchor.MiddleLeft, new Vector2(0, 0.5f), new Vector2(115, 4), new Vector2(645, 110));
                tipTexts[k].rectTransform.pivot = new Vector2(0, 0.5f);
                tipTexts[k].color = new Color(0.42f, 0.22f, 0.1f);
                tipTexts[k].GetComponent<Outline>().effectColor = new Color(1f, 1f, 1f, 0.5f);
                tipTexts[k].horizontalOverflow = HorizontalWrapMode.Wrap;
                tipTexts[k].resizeTextForBestFit = true;
                tipTexts[k].resizeTextMinSize = 36; tipTexts[k].resizeTextMaxSize = 54;
                tipTexts[k].raycastTarget = false;
            }
            demo.OnStep = SetTip;
            var playBtn = MakeImage(card.transform, Load("ui_btn_play"), new Vector2(0.5f, 0), new Vector2(0, 120), new Vector2(500, 165));
            playBtn.type = Image.Type.Sliced;
            playBtn.gameObject.AddComponent<Button>().onClick.AddListener(() => { Sfx("click"); CloseIntro(); });
            MakeUIText(playBtn.transform, "Thử ngay!", 64, TextAnchor.MiddleCenter, center, new Vector2(0, 6), new Vector2(460, 120))
                .GetComponent<Outline>().effectColor = new Color(0.1f, 0.35f, 0f, 0.9f);
            introPanel.SetActive(false);
        }

        RectTransform introGlow, introTag;
        readonly CanvasGroup[] tipRows = new CanvasGroup[2];
        readonly RectTransform[] tipNums = new RectTransform[2];
        readonly Text[] tipTexts = new Text[2];
        int tipOn;
        Spine.Unity.SkeletonGraphic introCat;

        void SetTip(int k)
        {
            tipOn = k;
            for (int i = 0; i < 2; i++) tipRows[i].alpha = i + 1 == k ? 1f : 0.4f;
        }

        // Two short tips per mechanic: what you see, what to do.
        static readonly Dictionary<string, string[]> Tips = new Dictionary<string, string[]>
        {
            ["cross"] = new[] { "Nối cả 4 phía", "Mèo đi thẳng qua" },
            ["dead"] = new[] { "Kéo thùng ra", "Đặt đường vào" },
            ["junction"] = new[] { "Vòng tròn = chỗ rẽ", "Mèo rẽ về hộp" },
            ["special"] = new[] { "Ô có đường riêng", "Ghép thành lối đi" },
            ["rotate"] = new[] { "Chạm để xoay", "Nối đường cho mèo" },
            ["multicat"] = new[] { "Ổ nhiều mèo", "Mỗi chú một hộp" },
            ["oneway"] = new[] { "Ngược chiều: chặn", "Theo mũi tên: đi" },
            ["ice"] = new[] { "Băng: chưa kéo được", "Kéo khối khác cho tan" },
            ["keylock"] = new[] { "Khóa chặn đường", "Qua chìa là mở" },
            ["inner"] = new[] { "Mèo ngồi trên bàn", "Mở lối ra hộp" },
            ["frozen"] = new[] { "Mèo băng phải chờ", "Mèo khác về là tan" },
            ["color"] = new[] { "Khác màu: chặn", "Cùng màu: đi qua" },
            ["fixed"] = new[] { "Khối xám đứng yên", "Dùng khối khác" },
            ["axis"] = new[] { "Sai hướng: đứng yên", "Trượt theo mũi tên" },
            ["mystery"] = new[] { "Khối ? giấu đường", "Kéo là lộ ra" },
            ["bomb"] = new[] { "Bom đếm ngược", "Mèo qua là gỡ bom" },
        };

        void ShowIntro(string feat, string name, string icon, string text)
        {
            introFeat = feat;
            introShownAt = Time.time;
            introName.text = name;
            introIconImg.sprite = Load(string.IsNullOrEmpty(icon) ? "paw" : icon);
            var tips = feat != null && Tips.TryGetValue(feat, out var t) ? t : new[] { text, "" };
            tipTexts[0].text = tips[0];
            tipTexts[1].text = tips[1];
            tipRows[1].gameObject.SetActive(!string.IsNullOrEmpty(tips[1]));
            SetTip(1);
            introPanel.SetActive(true);
            if (!introCat && front) // the orange cat sits on the card's top-right corner
            {
                introCat = front.MakeCat(introCard, "vang", Vector2.zero, 1.2f);
                introCat.rectTransform.anchorMin = introCat.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                introCat.rectTransform.anchoredPosition = new Vector2(370, 585); // sits on the card's top-right corner
            }
            if (introCat) { introCat.AnimationState.SetAnimation(0, "select_to_sit", false); introCat.AnimationState.AddAnimation(0, "front_idle1", true, 0); }
            demo.Play(feat, demoImage.gameObject);
            StartCoroutine(IntroPop());
        }

        Text introName;
        RawImage demoImage;
        DemoStage demo;
        string introFeat;
        float introShownAt;

        // The intro card closes; the level's blocks that carry the new mechanic glow a few times so you find them.
        void CloseIntro()
        {
            introPanel.SetActive(false);
            if (!string.IsNullOrEmpty(introFeat)) StartCoroutine(FeatureGlow(introFeat));
        }

        IEnumerator FeatureGlow(string feat)
        {
            var mine = pieces.Where(p => p.Has(feat)).ToList();
            for (int k = 0; k < 3 && mine.Count > 0; k++)
            {
                foreach (var p in mine) if (p) p.Flash(new Color(1f, 0.95f, 0.45f));
                yield return new WaitForSeconds(0.45f);
            }
        }

        // An empty, centered holder in the result panel.
        RectTransform Group(string name, Vector2 pos)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(panel.transform, false);
            rt.anchoredPosition = pos;
            rt.sizeDelta = Vector2.zero;
            return rt;
        }

        RectTransform introCard;

        IEnumerator IntroPop()
        {
            for (float t = 0; t < 1f; t += Time.deltaTime / 0.3f)
            {
                introCard.localScale = Vector3.one * Mathf.LerpUnclamped(0.6f, 1f, 1f - Mathf.Pow(1f - t, 3f) + Mathf.Sin(t * Mathf.PI) * 0.1f);
                yield return null;
            }
            introCard.localScale = Vector3.one;
        }

        internal Text MakeUIText(Transform parent, string txt, int size, TextAnchor align, Vector2 anchor, Vector2 pos, Vector2 sizeDelta)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(Outline));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos; rt.sizeDelta = sizeDelta;
            var t = go.GetComponent<Text>();
            t.font = font; t.fontSize = size; t.alignment = align; t.text = txt; t.color = Color.white;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            go.GetComponent<Outline>().effectColor = new Color(0, 0, 0, 0.5f);
            return t;
        }

        internal Image MakeImage(Transform parent, Sprite sp, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var go = new GameObject("Image", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.sprite = sp;
            return img;
        }

        Text MakeButton(Transform parent, string label, Vector2 anchor, Vector2 pos, Vector2 size, UnityAction onClick)
        {
            var img = MakeImage(parent, sprBtn, anchor, pos, size);
            img.type = Image.Type.Sliced;
            img.gameObject.AddComponent<Button>().onClick.AddListener(onClick);
            return MakeUIText(img.transform, label, 52, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero, size);
        }

        // ---------------------------------------------------------------- world-space helpers

        // Solid white circle with a 1-pixel soft edge (badges, groove joints).
        static Sprite MakeDisc(int n)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float r = n / 2f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                    tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(r - d)));
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
        }

        public static SpriteRenderer MakeSprite(string name, Transform parent, Sprite sp, float size, int order, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sp;
            sr.sortingOrder = order;
            if (sp != null)
            {
                var b = sp.bounds.size;
                go.transform.localScale = Vector3.one * (size / Mathf.Max(b.x, b.y));
            }
            return sr;
        }

        public TextMesh MakeText(string name, Transform parent, Vector3 pos, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var tm = go.AddComponent<TextMesh>();
            tm.font = font; tm.fontSize = 64; tm.characterSize = 0.06f;
            tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = font.material;
            mr.sortingOrder = order;
            return tm;
        }
    }
}
