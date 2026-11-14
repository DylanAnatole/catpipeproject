using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace CatPipe
{
    // A multi-cell block whose shape is its path. Slides on the board, never rotates.
    // Drawn with the original Catdom block sprites; the path is shown as a row of soft dots.
    public class Piece : MonoBehaviour
    {
        // Cell layouts of the original block sprites blocks/b{family}_{1..23}, top-left origin.
        static readonly string[] Shapes =
        {
            "1,0 0,1 1,1 2,1", "1,0 0,1 1,1 1,2", "0,0 1,0 2,0 1,1", "0,0 0,1 1,1 0,2", "0,0 1,0 2,0 2,1", "0,0 1,0 2,0 0,1",
            "0,0 0,1 1,1 2,1", "2,0 0,1 1,1 2,1", "0,0 1,0 1,1 1,2", "0,0 1,0 0,1 0,2", "0,0 0,1 0,2 1,2", "1,0 1,1 0,2 1,2",
            "0,0", "0,0 1,0 0,1 1,1", "1,0 0,1 1,1 2,1 1,2", "0,0 0,1 1,1", "1,0 0,1 1,1", "0,0 1,0 1,1", "0,0 1,0 0,1",
            "0,0 0,1 0,2", "0,0 1,0 2,0", "0,0 1,0", "0,0 0,1",
        };

        static readonly Color NeutralTint = new Color(1f, 0.86f, 0.62f);
        static readonly Color FixedTint = new Color(0.62f, 0.62f, 0.66f);
        static readonly Color DeadTint = new Color(0.86f, 0.62f, 0.42f); // crate (grey sh_ sprite tinted): a block with no path at all
        // T blocks and the 2x2 square carry no path (crate look); every other shape is its own path.
        static readonly int[] NoPathShapes = { 0, 1, 2, 3, 13 };

        Color BaseTint => isFixed ? FixedTint : masks.All(m => m == 0) ? DeadTint : color == 0 ? NeutralTint : Color.white;
        static readonly Color LineIdle = new Color(1f, 0.9f, 0.72f, 0.5f); // quiet until a cat's route lights it
        static readonly Color GrooveIdle = new Color(0.27f, 0.14f, 0.06f, 1f);
        // the path is a continuous rail (a cream line in a dark groove), not a row of dots
        const float LineWidth = 0.065f, LitLineWidth = 0.13f, GlowSize = 0.46f, GrooveWidth = 0.24f;

        // Path dot color per cat color (1-9: red, yellow, blue, green, pink, purple, orange, cyan, white).
        public static readonly Color[] CatColors =
        {
            Color.clear,
            new Color(1f, 0.35f, 0.35f), new Color(1f, 0.85f, 0.25f), new Color(0.40f, 0.65f, 1f),
            new Color(0.50f, 0.92f, 0.35f), new Color(1f, 0.55f, 0.80f), new Color(0.75f, 0.52f, 1f),
            new Color(1f, 0.62f, 0.25f), new Color(0.35f, 0.95f, 0.95f), new Color(1f, 1f, 1f),
        };

        // Sprite family for a color: white (9) uses the original white blocks, shared with neutral (0).
        public static int Family(int color) => color == 9 ? 0 : color;

        public Vector2Int anchor;
        public Vector2Int[] offs;
        public int[] masks;
        public bool[] junction; // special 3/4-way cell: cats may turn there (a derived crossing only goes straight)
        public int color, ice;
        public float bomb;
        public bool isFixed, key, isLock, gate, gateOpen, bombLive, mystery, rotatable;
        public int turns; // rotating block: quarter turns clockwise from its layout in the level file
        Transform pathSpin;
        Coroutine rotCo;
        public int axis = -1;   // -1 free, 0 horizontal only, 1 vertical only
        public int oneWay = -1; // -1 both ways, else the direction cats leave the path's end (Dir index)
        public int[] outDir;    // one-way blocks: the only side a cat may leave each cell by

        GameController gc;
        Sprite blockSprite;
        Vector3 blockCenter;
        Transform shadowTr;
        SpriteRenderer blockSr, highlightSr;
        Coroutine flashCo;
        SpriteRenderer[][] cellDots, cellGlows, cellGrooves; // cellDots = the rail pieces of each cell
        Vector3[][] lineIdle, lineLit;                         // their scale when idle / lit (lit = thicker)
        int[] flowOrder; // position along the cat's route, -1 = no route here
        SpriteRenderer gateSr, keySr, lockSr, bombSr, bombBadge, iceBadge; // badges: dark discs behind the counters
        static readonly Color BadgeColor = new Color(0.22f, 0.1f, 0.04f, 0.85f);
        SpriteRenderer[] iceSrs;
        List<GameObject> mysteryCovers;
        TextMesh iceText, bombText;
        SortingGroup group;

        public bool Movable(bool keys) => !isFixed && ice == 0 && !(isLock && !keys);

        // Does this block show the given mechanic (intro glow after the "New feature" card)?
        public bool Has(string feat)
        {
            switch (feat)
            {
                case "cross": return offs.Length == 5;
                case "dead": return masks.All(m => m == 0);
                case "junction": return junction.Any(j => j);
                case "special": return offs.Length == 1 && masks[0] != 0;
                case "rotate": return rotatable;
                case "oneway": return oneWay >= 0;
                case "ice": return ice > 0;
                case "keylock": return key || isLock;
                case "color": return color > 0;
                case "fixed": return isFixed;
                case "axis": return axis >= 0;
                case "mystery": return mystery;
                case "bomb": return bombLive;
                default: return false;
            }
        }

        public int CellIndex(Vector2Int cell)
        {
            var o = cell - anchor;
            for (int i = 0; i < offs.Length; i++) if (offs[i] == o) return i;
            return -1;
        }

        public void Build(PieceJson j, GameController gc)
        {
            this.gc = gc;
            offs = j.cells.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(Dir.ParseXY).ToArray();
            masks = offs.Select(PathMask).ToArray();
            if (NoPathShapes.Contains(ShapeIndex())) masks = new int[offs.Length];
            junction = new bool[offs.Length];
            if (!string.IsNullOrEmpty(j.paths)) // special block: explicit path per cell
            {
                var tok = j.paths.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < offs.Length && i < tok.Length; i++)
                {
                    masks[i] = 0;
                    foreach (char ch in tok[i]) { int d = "NESW".IndexOf(ch); if (d >= 0) masks[i] |= 1 << d; }
                    int open = 0;
                    for (int d = 0; d < 4; d++) open += (masks[i] >> d) & 1;
                    junction[i] = open >= 3;
                }
            }
            color = j.color; ice = j.ice; bomb = j.bomb; bombLive = j.bomb > 0;
            key = j.key; isLock = j.@lock; gate = j.gate; gateOpen = true;
            isFixed = j.@fixed || j.gate;
            axis = j.axis == "H" ? 0 : j.axis == "V" ? 1 : -1;
            oneWay = string.IsNullOrEmpty(j.oneway) ? -1 : "NESW".IndexOf(j.oneway[0]);
            if (oneWay >= 0) BuildOutDirs();
            mystery = j.mystery;
            rotatable = j.rotate && offs.Length == 1;

            group = gameObject.AddComponent<SortingGroup>();
            group.sortingOrder = 5;
            BuildBody();

            cellDots = new SpriteRenderer[offs.Length][];
            lineIdle = new Vector3[offs.Length][];
            lineLit = new Vector3[offs.Length][];
            cellGlows = new SpriteRenderer[offs.Length][];
            cellGrooves = new SpriteRenderer[offs.Length][];
            flowOrder = new int[offs.Length];
            for (int i = 0; i < offs.Length; i++) BuildDots(i, new Vector2(offs[i].x, -offs[i].y));
            if (rotatable) // only the path turns; body, shadow and badges stay put
            {
                pathSpin = new GameObject("PathSpin").transform;
                pathSpin.SetParent(transform, false);
                foreach (Transform ch in transform.Cast<Transform>().ToList())
                    if (ch.name == "Dot" || ch.name == "Glow" || ch.name == "Groove" || ch.name == "Hub") ch.SetParent(pathSpin, true);
            }

            var first = new Vector3(offs[0].x, -offs[0].y);
            if (gate)
            {
                bool verticalPath = (masks[0] & 0b0101) != 0;
                gateSr = GameController.MakeSprite("Gate", transform, gc.sprGateBar, 0.95f, 4, first);
                if (!verticalPath) gateSr.transform.localRotation = Quaternion.Euler(0, 0, 90);
            }
            if (axis >= 0) // two chevrons pointing along the allowed axis
                for (int k = 0; k < 2; k++)
                {
                    float ang = (axis == 0 ? 0f : 90f) + k * 180f;
                    var dir = Quaternion.Euler(0, 0, ang) * Vector3.right;
                    var a = GameController.MakeSprite("Axis", transform, gc.sprAxisArrow, 0.26f, 5, blockCenter + dir * 0.2f);
                    a.transform.localRotation = Quaternion.Euler(0, 0, ang);
                }
            if (oneWay >= 0)
                for (int i = 0; i < offs.Length; i++)
                {
                    var a = GameController.MakeSprite("OneWay", transform, gc.sprOneWay, 0.36f, 4, new Vector3(offs[i].x, -offs[i].y));
                    a.transform.localRotation = Quaternion.Euler(0, 0, -90f * outDir[i]);
                }
            if (mystery) // the original "hidden" block of the same shape covers it: path or crate, you only see once it moves
            {
                var cover = GameController.MakeSprite("Mystery", transform, gc.sprHid[ShapeIndex() + 1], 1f, 6, blockCenter);
                cover.transform.localScale = Vector3.one * 0.97f;
                mysteryCovers = new List<GameObject> { cover.gameObject };
            }
            if (rotatable) // round-arrow badge in the corner: tap to turn
            {
                var badge = GameController.MakeSprite("RotateBadge", transform, gc.sprDisc, 0.36f, 5, new Vector3(0.27f, 0.27f));
                badge.color = new Color(0.2f, 0.45f, 0.95f);
                var arrow = GameController.MakeSprite("RotateIcon", badge.transform, gc.sprRotate, 0.24f / badge.transform.localScale.x, 6, Vector3.zero);
                arrow.color = Color.white;
            }
            if (key) keySr = GameController.MakeSprite("Key", transform, gc.sprKey, 0.55f, 5, first);
            if (isLock) lockSr = GameController.MakeSprite("Lock", transform, gc.sprLock, 0.55f, 5, first);
            if (bombLive)
            {
                bombSr = GameController.MakeSprite("Bomb", transform, gc.sprBomb, 0.5f, 5, first + new Vector3(-0.15f, 0.12f));
                bombBadge = GameController.MakeSprite("BombBadge", transform, gc.sprDisc, 0.4f, 5, first + new Vector3(0.14f, -0.18f));
                bombBadge.color = BadgeColor;
                bombBadge.transform.localScale = Vector3.Scale(bombBadge.transform.localScale, new Vector3(1.6f, 1.05f, 1f)); // pill: fits 3 digits
                bombText = gc.MakeText("BombCount", transform, first + new Vector3(0.14f, -0.18f), 6);
                bombText.transform.localScale = Vector3.one * 0.9f;
            }
            if (ice > 0)
            {
                iceSrs = new SpriteRenderer[offs.Length];
                for (int i = 0; i < offs.Length; i++)
                {
                    iceSrs[i] = GameController.MakeSprite("Ice", transform, gc.sprIce, 1f, 7, new Vector3(offs[i].x, -offs[i].y));
                    iceSrs[i].color = new Color(1, 1, 1, 0.8f);
                }
                iceBadge = GameController.MakeSprite("IceBadge", transform, gc.sprDisc, 0.46f, 8, first);
                iceBadge.color = BadgeColor;
                iceText = gc.MakeText("IceCount", transform, first, 9);
                iceText.transform.localScale = Vector3.one * 1.15f;
            }
            ResetFlow();
            Refresh(false);
        }

        // The block's shape is its path: straight runs of cells carry the cat straight through (and out of the ends),
        // an L corner turns, a T/+ junction is a crossing, a lone cell is open on all sides.
        int PathMask(Vector2Int o)
        {
            bool n = offs.Contains(o + Dir.Step[0]), e = offs.Contains(o + Dir.Step[1]);
            bool s = offs.Contains(o + Dir.Step[2]), w = offs.Contains(o + Dir.Step[3]);
            bool h = e || w, v = n || s;
            if (h && v && !(e && w) && !(n && s)) return (n ? 1 : 0) | (e ? 2 : 0) | (s ? 4 : 0) | (w ? 8 : 0); // corner
            if (h && !v) return 0b1010;
            if (v && !h) return 0b0101;
            return 0b1111;
        }

        int ShapeIndex()
        {
            int minX = offs.Min(o => o.x), minY = offs.Min(o => o.y);
            var cells = new HashSet<Vector2Int>(offs.Select(o => new Vector2Int(o.x - minX, o.y - minY)));
            return Array.FindIndex(Shapes, sh =>
            {
                var c = sh.Split(' ').Select(Dir.ParseXY).ToList();
                return c.Count == cells.Count && c.All(cells.Contains);
            });
        }

        // One-way: start at the cell whose open end points out of the block in the one-way direction,
        // then walk the path backwards; each cell may only be left toward the exit.
        void BuildOutDirs()
        {
            outDir = new int[offs.Length];
            int cur = Array.FindIndex(offs, o => ((masks[Array.IndexOf(offs, o)] >> oneWay) & 1) != 0 && !offs.Contains(o + Dir.Step[oneWay]));
            if (cur < 0) { Debug.LogError("CatPipe: one-way block has no exit " + oneWay); cur = 0; }
            int dOut = oneWay;
            for (int n = 0; n < offs.Length && cur >= 0; n++) // stops at the entry end (cur = -1)
            {
                outDir[cur] = dOut;
                int back = -1;
                for (int d = 0; d < 4; d++)
                    if (d != dOut && ((masks[cur] >> d) & 1) != 0) back = d;
                if (back < 0) break;
                int prev = Array.IndexOf(offs, offs[cur] + Dir.Step[back]);
                dOut = Dir.Opp(back);
                cur = prev;
            }
        }

        // The original block sprite whose cell layout matches this piece, with a soft drop shadow.
        void BuildBody()
        {
            int minX = offs.Min(o => o.x), minY = offs.Min(o => o.y), maxX = offs.Max(o => o.x), maxY = offs.Max(o => o.y);
            var cells = new HashSet<Vector2Int>(offs.Select(o => new Vector2Int(o.x - minX, o.y - minY)));
            int shape = Array.FindIndex(Shapes, sh =>
            {
                var c = sh.Split(' ').Select(Dir.ParseXY).ToList();
                return c.Count == cells.Count && c.All(cells.Contains);
            });
            if (shape < 0)
            {
                Debug.LogError("CatPipe: no original block sprite for piece shape " + string.Join(" ", offs));
                return;
            }
            var sprite = masks.All(m => m == 0) ? gc.sprSh[shape + 1] : gc.sprBlocks[isFixed ? 0 : Family(color), shape + 1];
            var center = new Vector3((minX + maxX) / 2f, -(minY + maxY) / 2f);
            blockSprite = sprite;
            blockCenter = center;
            var shadow = GameController.MakeSprite("Shadow", transform, sprite, 1f, -3, center + new Vector3(0.05f, -0.09f));
            shadow.transform.localScale = Vector3.one * 0.97f;
            shadow.color = new Color(0, 0, 0, 0.35f);
            shadowTr = shadow.transform;
            var side = GameController.MakeSprite("Side", transform, sprite, 1f, -1, center + new Vector3(0, -0.07f)); // thick lower edge: the block reads as a raised slab
            side.transform.localScale = Vector3.one * 0.97f;
            side.color = BaseTint * new Color(0.5f, 0.42f, 0.36f, 1f);
            highlightSr = GameController.MakeSprite("Highlight", transform, sprite, 1f, -2, center);
            highlightSr.transform.localScale = Vector3.one * 1.04f;
            highlightSr.color = new Color(1f, 1f, 0.85f, 0f);
            var sr = GameController.MakeSprite("Block", transform, sprite, 1f, 0, center);
            sr.transform.localScale = Vector3.one * 0.97f; // sprites are imported at 1 unit per cell
            sr.color = BaseTint;
            blockSr = sr;
        }

        // Each path cell: a dark groove from its center to every open edge, and inside it a continuous rail
        // (straight strips with a round joint at the center, so corners read as one smooth line).
        // Open ends cut through the block's rim and stop at its outline (the body is drawn at 0.97 scale).
        void BuildDots(int i, Vector2 c)
        {
            var glowAt = new List<Vector2>();
            var grooves = new List<SpriteRenderer>();
            var rails = new List<SpriteRenderer>();
            var idle = new List<Vector3>();
            var lit = new List<Vector3>();
            if (masks[i] != 0) // a dead cell (no path) shows nothing; a path cell gets round joints
            {
                glowAt.Add(c);
                grooves.Add(GameController.MakeSprite("Groove", transform, gc.sprDisc, GrooveWidth, 1, c));
                var joint = GameController.MakeSprite("Dot", transform, gc.sprDisc, LineWidth, 3, c);
                rails.Add(joint);
                idle.Add(joint.transform.localScale);
                lit.Add(joint.transform.localScale * (LitLineWidth / LineWidth));
            }
            if (junction[i]) // a ring marks a cell where cats can turn
            {
                var ring = GameController.MakeSprite("Hub", transform, gc.sprHubRing, 0.42f, 4, c);
                ring.color = new Color(1f, 1f, 1f, 0.85f);
            }
            for (int d = 0; d < 4; d++)
            {
                if (((masks[i] >> d) & 1) == 0) continue;
                var dir = new Vector2(Dir.Step[d].x, -Dir.Step[d].y);
                bool shared = offs.Contains(offs[i] + Dir.Step[d]);
                float len = shared ? 0.5f : 0.46f;
                glowAt.Add(c + dir * 0.3f);
                var g = GameController.MakeSprite("Groove", transform, gc.sprWhite, 1f, 1, c + dir * (len / 2f));
                g.transform.localScale = d % 2 == 1 ? new Vector3(len, GrooveWidth, 1f) : new Vector3(GrooveWidth, len, 1f);
                grooves.Add(g);
                var r = GameController.MakeSprite("Dot", transform, gc.sprWhite, 1f, 3, c + dir * (len / 2f));
                bool horiz = d % 2 == 1;
                idle.Add(horiz ? new Vector3(len, LineWidth, 1f) : new Vector3(LineWidth, len, 1f));
                lit.Add(horiz ? new Vector3(len, LitLineWidth, 1f) : new Vector3(LitLineWidth, len, 1f));
                r.transform.localScale = idle[idle.Count - 1];
                rails.Add(r);
            }
            cellGrooves[i] = grooves.ToArray();
            cellDots[i] = rails.ToArray();
            lineIdle[i] = idle.ToArray();
            lineLit[i] = lit.ToArray();
            cellGlows[i] = glowAt.Select(p => GameController.MakeSprite("Glow", transform, gc.sprGlow, GlowSize, 2, p)).ToArray();
        }

        public void Refresh(bool keysCollected)
        {
            if (gateSr) gateSr.color = gateOpen ? new Color(1f, 1f, 1f, 0.25f) : Color.white;
            if (keySr) keySr.enabled = key;
            if (lockSr) lockSr.enabled = isLock && !keysCollected;
            if (bombSr)
            {
                bombSr.enabled = bombBadge.enabled = bombLive;
                bombText.text = bombLive ? Mathf.CeilToInt(bomb).ToString() : "";
                bombText.color = bomb <= 10 ? Color.red : Color.white;
            }
            if (iceSrs != null)
            {
                foreach (var s in iceSrs) s.enabled = ice > 0;
                iceBadge.enabled = ice > 0;
                iceText.text = ice > 0 ? ice.ToString() : "";
            }
        }

        // Rotating block: its path turns a quarter clockwise (the whole block turns; a single cell looks the same).
        public void Rotate(bool animate)
        {
            masks[0] = ((masks[0] << 1) | (masks[0] >> 3)) & 15;
            turns = (turns + 1) % 4;
            var to = Quaternion.Euler(0, 0, -90f * turns);
            if (rotCo != null) StopCoroutine(rotCo);
            if (animate) rotCo = StartCoroutine(AnimRotate(pathSpin.localRotation, to));
            else pathSpin.localRotation = to;
        }

        IEnumerator AnimRotate(Quaternion from, Quaternion to)
        {
            for (float t = 0; t < 1f; t += Time.deltaTime / 0.18f)
            {
                pathSpin.localRotation = Quaternion.SlerpUnclamped(from, to, 1f - (1f - t) * (1f - t) + Mathf.Sin(t * Mathf.PI) * 0.15f);
                yield return null;
            }
            pathSpin.localRotation = to;
        }

        // Removes the "?" covers (first drag, or a cat walking through).
        public void Reveal()
        {
            if (!mystery) return;
            mystery = false;
            foreach (var c in mysteryCovers) Destroy(c);
        }

        public void ResetFlow()
        {
            for (int i = 0; i < offs.Length; i++) SetFlow(i, 0, -1);
        }

        int[] flowColor;

        // Where a cat's route reaches, the rail turns the cat's color, gets a little thicker and glows;
        // order = step along the route (drives the light running toward the hole).
        public void SetFlow(int cellIndex, int catColor, int order)
        {
            if (flowColor == null) flowColor = new int[offs.Length];
            flowOrder[cellIndex] = catColor > 0 ? order : -1;
            flowColor[cellIndex] = catColor;
            var lit = catColor > 0 ? Color.Lerp(GrooveIdle, CatColors[catColor], 0.35f) : GrooveIdle;
            // the groove takes a hint of the cat's color along its route (opaque: overlapping pieces don't darken)
            foreach (var g in cellGrooves[cellIndex]) g.color = lit;
            var rails = cellDots[cellIndex];
            for (int k = 0; k < rails.Length; k++)
            {
                rails[k].color = catColor > 0 ? CatColors[catColor] : LineIdle;
                rails[k].transform.localScale = catColor > 0 ? lineLit[cellIndex][k] : lineIdle[cellIndex][k];
            }
            foreach (var g in cellGlows[cellIndex])
            {
                g.enabled = catColor > 0;
                if (catColor > 0) g.color = new Color(CatColors[catColor].r, CatColors[catColor].g, CatColors[catColor].b, 0.4f);
            }
        }

        // A soft gleam runs along a lit route toward the hole: each cell brightens in turn.
        void Update()
        {
            if (flowColor == null) return;
            for (int i = 0; i < offs.Length; i++)
            {
                if (flowOrder[i] < 0) continue;
                float w = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Time.time * 4.5f - flowOrder[i] * 0.75f)), 6f);
                var baseC = CatColors[flowColor[i]];
                var c = Color.Lerp(baseC, Color.white, 0.55f * w);
                foreach (var d in cellDots[i]) d.color = c;
                foreach (var g in cellGlows[i]) g.color = new Color(baseC.r, baseC.g, baseC.b, 0.32f + 0.3f * w);
            }
        }

        // ---- lift / hold / land. Picked up, the block rises (grows, longer shadow, warm rim) with a little spring;
        // held, it follows the finger smoothly and leans into the motion; released, it glides into its cell with a
        // slight overshoot, settles upright and squashes with a puff of dust. Scale and lean pivot on the block's
        // center, so a long block doesn't swing around its first cell.
        float lift, tilt;           // lift 0..1 (springs a bit past 1), lean in degrees
        Vector2 squash;             // landing squash: x widens, y flattens
        Vector3 basePos;            // where the block sits without the pose
        bool posing;                // the game drives the position (Hold / Land); the demo stage moves the transform itself
        Coroutine poseCo;

        public Vector3 HeldBase => posing ? basePos : transform.localPosition;

        public void SetDragging(bool on)
        {
            if (poseCo != null) StopCoroutine(poseCo);
            if (on)
            {
                group.sortingOrder = 50;
                basePos = transform.localPosition;
                poseCo = StartCoroutine(AnimLift());
            }
            else
            {
                if (posing) basePos = transform.localPosition; // put back in its cell (Put) without a glide
                poseCo = StartCoroutine(AnimDown(false, basePos, basePos));
            }
        }

        // Called every frame while held: follow the target smoothly and lean with the horizontal speed.
        public void Hold(Vector3 target)
        {
            posing = true;
            float dt = Time.deltaTime;
            var prev = basePos;
            basePos = Vector3.Lerp(basePos, target, 1f - Mathf.Exp(-28f * dt));
            float vx = dt > 0f ? (basePos.x - prev.x) / dt : 0f;
            tilt = Mathf.Lerp(tilt, Mathf.Clamp(-vx * 1.4f, -6f, 6f), 1f - Mathf.Exp(-12f * dt));
            ApplyPose();
        }

        // Released: Put() has already moved the transform to its cell; glide there from where it was held.
        public void Land(Vector3 from)
        {
            if (poseCo != null) StopCoroutine(poseCo);
            var to = transform.localPosition;
            posing = true;
            poseCo = StartCoroutine(AnimDown(true, from, to));
        }

        static float EaseOutBack(float t, float c = 1.7f)
        {
            t = Mathf.Clamp01(t) - 1f;
            return 1f + (c + 1f) * t * t * t + c * t * t;
        }

        IEnumerator AnimLift()
        {
            float l0 = lift;
            for (float t = 0; t < 1f; t += Time.deltaTime / 0.14f)
            {
                lift = Mathf.LerpUnclamped(l0, 1f, EaseOutBack(t, 2.2f));
                ApplyPose();
                yield return null;
            }
            lift = 1f;
            ApplyPose();
        }

        IEnumerator AnimDown(bool glide, Vector3 from, Vector3 to)
        {
            float l0 = lift, t0 = tilt;
            for (float t = 0; t < 1f; t += Time.deltaTime / 0.17f)
            {
                if (glide) basePos = Vector3.LerpUnclamped(from, to, EaseOutBack(t, 1.3f));
                lift = Mathf.Lerp(l0, 0f, t);
                tilt = Mathf.Lerp(t0, 0f, t);
                ApplyPose();
                yield return null;
            }
            basePos = to; lift = 0f; tilt = 0f;
            ApplyPose();
            foreach (var o in offs.Where(o => !offs.Contains(o + Dir.Step[2])))
                for (int k = -1; k <= 1; k += 2)
                    StartCoroutine(Puff(to + new Vector3(o.x + k * 0.3f, -o.y - 0.45f)));
            for (float t = 0; t < 1f; t += Time.deltaTime / 0.2f)
            {
                float s = Mathf.Sin(t * Mathf.PI) * (1f - t * 0.4f);
                squash = new Vector2(0.05f * s, -0.07f * s);
                ApplyPose();
                yield return null;
            }
            squash = Vector2.zero;
            ApplyPose();
            posing = false;
            group.sortingOrder = 5;
            poseCo = null;
        }

        void ApplyPose()
        {
            float s = 1f + 0.07f * lift;
            var scale = new Vector3(s * (1f + squash.x), s * (1f + squash.y), 1f);
            var q = Quaternion.Euler(0, 0, tilt);
            transform.localScale = scale;
            transform.localRotation = q;
            if (posing) transform.localPosition = basePos + blockCenter - q * Vector3.Scale(blockCenter, scale);
            float l = Mathf.Clamp01(lift);
            if (shadowTr) shadowTr.localPosition = blockCenter + Vector3.Lerp(new Vector3(0.05f, -0.09f), new Vector3(0.15f, -0.28f), l);
            if (highlightSr) highlightSr.color = new Color(1f, 1f, 0.85f, 0.7f * l);
        }

        IEnumerator Puff(Vector3 pos)
        {
            var p = GameController.MakeSprite("Puff", transform.parent, gc.sprGlow, 0.3f, 4, pos);
            var s0 = p.transform.localScale;
            for (float t = 0; t < 1f; t += Time.deltaTime / 0.35f)
            {
                p.transform.localScale = s0 * (1f + t * 0.8f);
                p.transform.localPosition = pos + new Vector3(0, t * 0.08f);
                p.color = new Color(1f, 0.95f, 0.85f, 0.7f * (1f - t));
                yield return null;
            }
            Destroy(p.gameObject);
        }

        // Brief color flash on the block (red when it cannot move).
        public void Flash(Color c)
        {
            if (!blockSr) return;
            if (flashCo != null) StopCoroutine(flashCo);
            flashCo = StartCoroutine(AnimFlash(c));
        }

        IEnumerator AnimFlash(Color c)
        {
            var baseCol = BaseTint;
            for (float t = 0; t < 1f; t += Time.deltaTime / 0.35f)
            {
                blockSr.color = Color.Lerp(c, baseCol, t);
                yield return null;
            }
            blockSr.color = baseCol;
        }

        // Faint copy of the block, shown where a dragged block will land.
        public SpriteRenderer MakeGhost(Transform parent)
        {
            var root = new GameObject("Ghost").transform;
            root.SetParent(parent, false);
            var sr = GameController.MakeSprite("GhostBlock", root, blockSprite, 1f, 4, blockCenter);
            sr.transform.localScale = Vector3.one * 0.97f;
            sr.color = new Color(1f, 1f, 1f, 0.35f);
            return sr;
        }

        // Pop in at level start, staggered by delay.
        public IEnumerator PopIn(float delay)
        {
            transform.localScale = Vector3.zero;
            yield return new WaitForSeconds(delay);
            for (float t = 0; t < 1f; t += Time.deltaTime / 0.3f)
            {
                transform.localScale = Vector3.one * (1f + Mathf.Sin(t * Mathf.PI) * 0.15f) * Mathf.Min(1f, t * 1.6f);
                yield return null;
            }
            transform.localScale = Vector3.one;
        }

        public void Shake() => StartCoroutine(AnimShake());

        IEnumerator AnimShake()
        {
            var basePos = transform.localPosition;
            for (float t = 0; t < 0.25f; t += Time.deltaTime)
            {
                transform.localPosition = basePos + new Vector3(Mathf.Sin(t * 70f) * 0.06f, 0);
                yield return null;
            }
            transform.localPosition = basePos;
        }
    }
}
