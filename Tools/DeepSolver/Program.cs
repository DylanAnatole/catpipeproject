// DeepSolver: picks the start layout of a Cat Pipe level so that the fewest drags to win falls in a band.
// Usage: DeepSolver <level.json> <lo> <hi> <seed> [minUse=0.0] [cap=3000000]
//  1. Explores every layout the blocks can reach from the solved one (blocks only) and its slide distance.
//  2. Takes far layouts as candidates, plays each with the exact solver (same rules as the game:
//     after each drag every cat with a route goes home, in order; frozen cats, keys/locks, block ice, one-way).
//  3. Prints JSON: start anchors + optimal solution, or ok=false with the component stats.
using System.Text.Json;
using System.Text.Json.Nodes;

static class P
{
    static readonly int[] SX = { 0, 1, 0, -1 }, SY = { -1, 0, 1, 0 };
    static readonly string[] Shapes = {
        "1,0 0,1 1,1 2,1", "1,0 0,1 1,1 1,2", "0,0 1,0 2,0 1,1", "0,0 0,1 1,1 0,2", "0,0 1,0 2,0 2,1", "0,0 1,0 2,0 0,1",
        "0,0 0,1 1,1 2,1", "2,0 0,1 1,1 2,1", "0,0 1,0 1,1 1,2", "0,0 1,0 0,1 0,2", "0,0 0,1 0,2 1,2", "1,0 1,1 0,2 1,2",
        "0,0", "0,0 1,0 0,1 1,1", "1,0 0,1 1,1 2,1 1,2", "0,0 0,1 1,1", "1,0 0,1 1,1", "0,0 1,0 1,1", "0,0 1,0 0,1",
        "0,0 0,1 0,2", "0,0 1,0 2,0", "0,0 1,0", "0,0 0,1" };
    static readonly int[] NoPath = { 0, 1, 2, 3, 13 };

    class Pc { public (int x, int y)[] offs = null!; public int[] masks = null!; public bool[] junc = null!; public int[]? outd;
        public int color, ice, axis = -1; public bool movable, lck, key; public int[]?[] fit = null!; public int mov;
        public int rotIdx = -1, period = 1; } // rotatable single cell: index into the 2-bit rotation field, distinct turns
    class CatD { public int cell = -1; public int[] sx = null!, sy = null!, sd = null!; public ulong vis; public int color, ice; }

    static int W, H, N;
    static Pc[] pcs = null!;
    static CatD[] cats = null!;
    static Dictionary<(int, int), (int k, int color)> nests = new();
    static int[] walls = Array.Empty<int>(), holes = Array.Empty<int>();
    static int maxIce, nRot;
    static bool consume; // "consume" levels: the blocks a cat walks over vanish once it is home
    static int curRots; // rotation state used by Route/Flood (2 bits per rotatable piece = quarter turns clockwise)
    static int Rotl(int m, int q) { for (; q > 0; q--) m = ((m << 1) | (m >> 3)) & 15; return m; }
    static int Mask(Pc p, int ci) => p.rotIdx < 0 ? p.masks[ci] : Rotl(p.masks[ci], curRots >> (2 * p.rotIdx) & 3);

    static (int, int) XY(string s) { var p = s.Split(','); return (int.Parse(p[0]), int.Parse(p[1])); }

    static int Derived(HashSet<(int, int)> cs, (int x, int y) o)
    {
        bool n = cs.Contains((o.x, o.y - 1)), e = cs.Contains((o.x + 1, o.y)), s = cs.Contains((o.x, o.y + 1)), w = cs.Contains((o.x - 1, o.y));
        bool h = e || w, v = n || s;
        if (h && v && !(e && w) && !(n && s)) return (n ? 1 : 0) | (e ? 2 : 0) | (s ? 4 : 0) | (w ? 8 : 0);
        if (h && !v) return 10;
        if (v && !h) return 5;
        return 15;
    }

    static int ShapeIndex((int x, int y)[] offs)
    {
        int mx = offs.Min(o => o.x), my = offs.Min(o => o.y);
        var cs = offs.Select(o => (o.x - mx, o.y - my)).ToHashSet();
        for (int i = 0; i < Shapes.Length; i++)
        {
            var c = Shapes[i].Split(' ').Select(XY).ToList();
            if (c.Count == cs.Count && c.All(cs.Contains)) return i;
        }
        return -1;
    }

    static void Load(JsonNode L)
    {
        W = (int)L["w"]!; H = (int)L["h"]!; N = W * H;
        consume = (bool?)L["consume"] ?? false;
        if (N > 64) throw new Exception("board too big");
        walls = ((string?)L["walls"] ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(s => { var (x, y) = XY(s); return y * W + x; }).ToArray();
        var pl = new List<Pc>();
        foreach (var p in L["pieces"]!.AsArray())
        {
            var q = new Pc();
            q.offs = ((string)p!["cells"]!).Split(' ').Select(XY).ToArray();
            var cs = q.offs.ToHashSet();
            q.masks = NoPath.Contains(ShapeIndex(q.offs)) ? new int[q.offs.Length] : q.offs.Select(o => Derived(cs, o)).ToArray();
            q.junc = new bool[q.offs.Length];
            var paths = (string?)p["paths"];
            if (!string.IsNullOrEmpty(paths))
            {
                var tok = paths.Split(' ');
                for (int i = 0; i < tok.Length; i++)
                {
                    q.masks[i] = tok[i] == "-" ? 0 : tok[i].Sum(ch => 1 << "NESW".IndexOf(ch));
                    q.junc[i] = System.Numerics.BitOperations.PopCount((uint)q.masks[i]) >= 3;
                }
            }
            var ow = (string?)p["oneway"];
            if (!string.IsNullOrEmpty(ow))
            {
                int dOut = "NESW".IndexOf(ow[0]);
                q.outd = new int[q.offs.Length];
                int cur = Array.FindIndex(q.offs, o => (q.masks[Array.IndexOf(q.offs, o)] >> dOut & 1) != 0 && !cs.Contains((o.x + SX[dOut], o.y + SY[dOut])));
                for (int n = 0; n < q.offs.Length && cur >= 0; n++)
                {
                    q.outd[cur] = dOut;
                    int back = -1;
                    for (int d = 0; d < 4; d++) if (d != dOut && (q.masks[cur] >> d & 1) != 0) back = d;
                    if (back < 0) break;
                    cur = Array.IndexOf(q.offs, (q.offs[cur].x + SX[back], q.offs[cur].y + SY[back]));
                    dOut = (back + 2) % 4;
                }
            }
            if ((bool?)p["rotate"] ?? false)
            {
                q.rotIdx = nRot++;
                q.period = q.masks[0] == 15 ? 1 : (q.masks[0] == 5 || q.masks[0] == 10) ? 2 : 4;
            }
            q.color = (int?)p["color"] ?? 0; q.ice = (int?)p["ice"] ?? 0;
            q.movable = !((bool?)p["fixed"] ?? false) && !((bool?)p["gate"] ?? false);
            q.lck = (bool?)p["lock"] ?? false; q.key = (bool?)p["key"] ?? false;
            var ax = (string?)p["axis"]; q.axis = ax == "H" ? 0 : ax == "V" ? 1 : -1;
            q.fit = new int[]?[N];
            for (int a = 0; a < N; a++)
            {
                int axx = a % W, ayy = a / W; var cells = new int[q.offs.Length]; bool ok = true;
                for (int k = 0; k < q.offs.Length; k++)
                {
                    int x = axx + q.offs[k].x, y = ayy + q.offs[k].y;
                    if (x < 0 || y < 0 || x >= W || y >= H) { ok = false; break; }
                    cells[k] = y * W + x;
                }
                if (ok) q.fit[a] = cells;
            }
            pl.Add(q);
        }
        pcs = pl.ToArray();
        if (pcs.Length > 32 || nRot > 16) throw new Exception("too many pieces");
        maxIce = pcs.Select(q => q.ice).DefaultIfEmpty(0).Max();
        var hl = new List<int>();
        int ni = 0;
        foreach (var n in L["nests"]!.AsArray())
        {
            (int x, int y) pos;
            if (n!["at"] != null) { pos = XY((string)n["at"]!); hl.Add(pos.y * W + pos.x); }
            else pos = Edge((string)n["side"]!, (int)n["index"]!).pos;
            nests[pos] = (ni++, (int)n["color"]!);
        }
        holes = hl.ToArray();
        var cl = new List<CatD>();
        foreach (var c in L["cats"]!.AsArray())
        {
            var cd = new CatD { color = (int)c!["color"]!, ice = (int?)c["ice"] ?? 0 };
            if (c["at"] != null)
            {
                var (x, y) = XY((string)c["at"]!);
                cd.cell = y * W + x; cd.vis = 1UL << cd.cell;
                cd.sx = new int[4]; cd.sy = new int[4]; cd.sd = new int[4];
                for (int d = 0; d < 4; d++) { cd.sx[d] = x + SX[d]; cd.sy[d] = y + SY[d]; cd.sd[d] = d; }
            }
            else
            {
                var (pos, d) = Edge((string)c["side"]!, (int)c["index"]!);
                cd.sx = new[] { pos.x + SX[d] }; cd.sy = new[] { pos.y + SY[d] }; cd.sd = new[] { d };
            }
            int count = (int?)c["count"] ?? 1; // a bed holding several cats of the same color
            for (int k = 0; k < count; k++) cl.Add(cd);
        }
        cats = cl.ToArray();
    }

    static ((int x, int y) pos, int d) Edge(string side, int i) => side switch
    {
        "L" => ((-1, i), 1), "R" => ((W, i), 3), "T" => ((i, -1), 2), _ => ((i, H), 0)
    };

    // ------------------------------------------------------------ board
    static int[] Occ(byte[] an, int done, int gone = 0)
    {
        var g = new int[N];
        Array.Fill(g, -1);
        foreach (var c in walls) g[c] = -2;
        foreach (var c in holes) g[c] = -4;
        for (int k = 0; k < cats.Length; k++) if (cats[k].cell >= 0 && (done >> k & 1) == 0) g[cats[k].cell] = -3;
        for (int i = 0; i < pcs.Length; i++)
        {
            if ((gone >> i & 1) != 0) continue; // consumed: no longer on the board
            var f = pcs[i].fit[an[i]]!; for (int k = 0; k < f.Length; k++) g[f[k]] = i * 8 + k;
        }
        return g;
    }

    static readonly List<int> reachBuf = new();
    static List<int> Reach(int[] g, byte[] an, int i)
    {
        var p = pcs[i];
        reachBuf.Clear();
        var seen = new HashSet<int> { an[i] };
        var todo = new Stack<int>(); todo.Push(an[i]);
        while (todo.Count > 0)
        {
            int a = todo.Pop();
            for (int d = 0; d < 4; d++)
            {
                if (p.axis == 0 && d % 2 == 0) continue; // horizontal only
                if (p.axis == 1 && d % 2 == 1) continue;
                int x = a % W + SX[d], y = a / W + SY[d];
                if (x < 0 || y < 0 || x >= W || y >= H) continue;
                int b = y * W + x;
                if (seen.Contains(b)) continue;
                var cells = p.fit[b];
                if (cells == null) continue;
                bool ok = true;
                foreach (var c in cells) if (g[c] != -1 && (g[c] < 0 || g[c] / 8 != i)) { ok = false; break; }
                if (!ok) continue;
                seen.Add(b); todo.Push(b); reachBuf.Add(b);
            }
        }
        return reachBuf;
    }

    // ------------------------------------------------------------ cats
    static readonly HashSet<(int, int, bool, ulong)> rseen = new();
    static readonly Queue<(int x, int y, int d, bool key, ulong vis)> rq = new();

    // Returns the hole, whether a key was picked up, and the cells of the route (as a bit set).
    static (int nest, bool key, ulong cells)? Route(int[] g, CatD cat, bool keys, int iceGone, int taken)
    {
        rseen.Clear(); rq.Clear();
        for (int s = 0; s < cat.sx.Length; s++) rq.Enqueue((cat.sx[s], cat.sy[s], cat.sd[s], keys, cat.vis));
        int nodes = 0;
        while (rq.Count > 0 && nodes++ < 60000)
        {
            var (x, y, d, key, vis) = rq.Dequeue();
            bool inside = x >= 0 && y >= 0 && x < W && y < H;
            if (!inside || g[y * W + x] == -4)
            {
                if (nests.TryGetValue((x, y), out var n) && n.color == cat.color && (taken >> n.k & 1) == 0) return (n.k, key, vis & ~cat.vis);
                continue;
            }
            int cell = y * W + x;
            ulong bit = 1UL << cell;
            if ((vis & bit) != 0) continue;
            int v = g[cell];
            if (v < 0) continue;
            var p = pcs[v / 8]; int ci = v % 8;
            int m = Mask(p, ci), enter = (d + 2) % 4;
            if ((m >> enter & 1) == 0) continue;
            if (p.color != 0 && p.color != cat.color) continue;
            if (p.ice > iceGone || (p.lck && !key)) continue;
            bool key2 = key || p.key;
            ulong vis2 = vis | bit;
            for (int nd = 0; nd < 4; nd++)
            {
                if (nd == enter || (m >> nd & 1) == 0) continue;
                if (m == 15 && !p.junc[ci] && nd != d) continue;
                if (p.outd != null && nd != p.outd[ci]) continue;
                int nx = x + SX[nd], ny = y + SY[nd];
                if (!rseen.Add((ny * (W + 2) + nx, nd, key2, vis2))) continue;
                rq.Enqueue((nx, ny, nd, key2, vis2));
            }
        }
        return null;
    }

    // Cells a cat could reach (revisits allowed, locks ignored): a superset of its routes.
    static void Flood(int[] g, CatD cat, int iceGone, bool[] mark)
    {
        var seen = new HashSet<int>();
        var todo = new Stack<(int x, int y, int d)>();
        for (int s = 0; s < cat.sx.Length; s++) todo.Push((cat.sx[s], cat.sy[s], cat.sd[s]));
        if (cat.cell >= 0) MarkAround(cat.cell, mark);
        while (todo.Count > 0)
        {
            var (x, y, d) = todo.Pop();
            if (x < 0 || y < 0 || x >= W || y >= H) continue;
            int cell = y * W + x;
            if (!seen.Add(cell * 4 + d)) continue;
            int v = g[cell];
            if (v < 0) { if (v == -1) MarkAround(cell, mark); continue; }
            var p = pcs[v / 8]; int ci = v % 8, m = Mask(p, ci), enter = (d + 2) % 4;
            if ((m >> enter & 1) == 0 || (p.color != 0 && p.color != cat.color) || p.ice > iceGone) continue;
            MarkAround(cell, mark);
            for (int nd = 0; nd < 4; nd++)
                if (nd != enter && (m >> nd & 1) != 0) todo.Push((x + SX[nd], y + SY[nd], nd));
        }
    }

    static void MarkAround(int c, bool[] mark)
    {
        mark[c] = true;
        int x = c % W, y = c / W;
        if (x > 0) mark[c - 1] = true;
        if (x < W - 1) mark[c + 1] = true;
        if (y > 0) mark[c - W] = true;
        if (y < H - 1) mark[c + W] = true;
    }

    static (int done, int taken, bool keys) Deliver(int[] g, int done, int taken, bool keys, int moves) =>
        Deliver(g, done, taken, keys, moves, ref dummyGone);
    static int dummyGone;

    // Sends home every cat that can go, in order. On consume levels the blocks of each route vanish (gone).
    static (int done, int taken, bool keys) Deliver(int[] g, int done, int taken, bool keys, int moves, ref int gone)
    {
        bool progress = true;
        while (progress)
        {
            progress = false;
            for (int k = 0; k < cats.Length; k++)
            {
                if ((done >> k & 1) != 0 || cats[k].ice > System.Numerics.BitOperations.PopCount((uint)done)) continue;
                var r = Route(g, cats[k], keys, moves, taken);
                if (r == null) continue;
                done |= 1 << k; taken |= 1 << r.Value.nest; keys = keys || r.Value.key;
                if (cats[k].cell >= 0) g[cats[k].cell] = -1; // its cell is free now
                if (consume)
                {
                    int used = 0;
                    for (int c = 0; c < N; c++) if ((r.Value.cells >> c & 1) != 0 && g[c] >= 0) used |= 1 << (g[c] / 8);
                    gone |= used;
                    for (int c = 0; c < N; c++) if (g[c] >= 0 && (used >> (g[c] / 8) & 1) != 0) g[c] = -1;
                }
                progress = true;
                break;
            }
        }
        return (done, taken, keys);
    }

    // ------------------------------------------------------------ keys
    record struct K(ulong a, ulong b, ulong c, ulong d, uint e);

    static K Pack(byte[] an, int done = 0, int taken = 0, bool keys = false, int moves = 0, int rots = 0, int gone = 0)
    {
        Span<ulong> u = stackalloc ulong[4];
        for (int i = 0; i < an.Length; i++)
        {
            if ((gone >> i & 1) != 0) continue; // a consumed block's anchor no longer matters
            int bit = i * 6; u[bit / 64] |= (ulong)an[i] << (bit % 64); if (bit % 64 > 58) u[bit / 64 + 1] |= (ulong)an[i] >> (64 - bit % 64);
        }
        u[3] |= (uint)rots; // anchors fill at most 192 bits (32 pieces)
        u[3] |= (ulong)done << 32 | (ulong)taken << 44 | (keys ? 1UL << 56 : 0) | (ulong)Math.Min(moves, maxIce) << 57;
        return new K(u[0], u[1], u[2], u[3], (uint)gone);
    }

    // ------------------------------------------------------------ search
    class St { public byte[] an = null!; public int done, taken, moves, parent, piece, to, rots, gone; public bool keys; } // to = -1: a tap (rotate)

    // Rotations after one more clockwise tap of piece p (wraps at its period).
    static int TapRot(int rots, Pc p, int dir = 1)
    {
        int sh = 2 * p.rotIdx, q = rots >> sh & 3;
        q = ((q + dir) % p.period + p.period) % p.period;
        return (rots & ~(3 << sh)) | q << sh;
    }

    static List<(int piece, int to)>? Solve(byte[] start, int rots0, int maxDepth, int cap)
    {
        int all = (1 << cats.Length) - 1;
        curRots = rots0;
        var g0 = Occ(start, 0);
        int gone0 = 0;
        var (d0, t0, k0) = Deliver(g0, 0, 0, false, 0, ref gone0);
        if (d0 == all) return new();
        var states = new List<St> { new St { an = start, rots = rots0, done = d0, taken = t0, keys = k0, gone = gone0, parent = -1 } };
        var seen = new HashSet<K> { Pack(start, d0, t0, k0, 0, rots0, gone0) };
        int head = 0;
        var mark = new bool[N];
        while (head < states.Count)
        {
            var s = states[head];
            if (s.moves >= maxDepth) break;
            curRots = s.rots;
            var g = Occ(s.an, s.done, s.gone);
            Array.Clear(mark);
            for (int k = 0; k < cats.Length; k++) if ((s.done >> k & 1) == 0) Flood(g, cats[k], s.moves, mark);
            bool melts = pcs.Any(p => p.ice == s.moves + 1);
            for (int i = 0; i < pcs.Length; i++)
            {
                var p = pcs[i];
                if ((s.gone >> i & 1) != 0) continue;
                if (!p.movable || p.ice > s.moves || (p.lck && !s.keys)) continue;
                var targets = Reach(g, s.an, i).ToList();
                if (p.rotIdx >= 0 && p.period > 1) targets.Add(-1); // a tap turns it a quarter clockwise
                foreach (var b in targets)
                {
                    var an2 = s.an; int r2 = s.rots;
                    int d2 = s.done, t2 = s.taken, gone2 = s.gone; bool k2 = s.keys;
                    var g2 = g;
                    bool recheck;
                    if (b < 0)
                    {
                        r2 = TapRot(s.rots, p);
                        recheck = true;
                    }
                    else
                    {
                        an2 = (byte[])s.an.Clone(); an2[i] = (byte)b;
                        recheck = melts || p.fit[b]!.Any(c => mark[c]);
                        if (recheck)
                        {
                            g2 = (int[])g.Clone();
                            foreach (var c in p.fit[s.an[i]]!) g2[c] = -1;
                            var cells = p.fit[b]!;
                            for (int k = 0; k < cells.Length; k++) g2[cells[k]] = i * 8 + k;
                        }
                    }
                    if (recheck)
                    {
                        if (b < 0) g2 = (int[])g.Clone();
                        curRots = r2;
                        (d2, t2, k2) = Deliver(g2, s.done, s.taken, s.keys, s.moves + 1, ref gone2);
                        curRots = s.rots;
                    }
                    if (!seen.Add(Pack(an2, d2, t2, k2, s.moves + 1, r2, gone2))) continue;
                    states.Add(new St { an = an2, rots = r2, done = d2, taken = t2, keys = k2, gone = gone2, moves = s.moves + 1, parent = head, piece = i, to = b });
                    if (d2 == all)
                    {
                        var path = new List<(int, int)>();
                        for (int cur = states.Count - 1; states[cur].parent >= 0; cur = states[cur].parent) path.Add((states[cur].piece, states[cur].to));
                        path.Reverse();
                        return path;
                    }
                    if (states.Count > cap) return null;
                }
            }
            head++;
        }
        return null;
    }

    // Every layout the blocks can reach from the solved one, with its distance (slides, and rotations turned
    // back counter-clockwise so the distance matches the taps needed to undo them). Cats stay put.
    static Dictionary<K, (byte[] an, int rots, int dist)> Component(byte[] solved, int cap)
    {
        var res = new Dictionary<K, (byte[], int, int)> { [Pack(solved)] = (solved, 0, 0) };
        var frontier = new List<(byte[] an, int rots)> { (solved, 0) };
        for (int dist = 1; frontier.Count > 0 && res.Count < cap; dist++)
        {
            var next = new List<(byte[], int)>();
            foreach (var (an, rots) in frontier)
            {
                var g = Occ(an, 0);
                for (int i = 0; i < pcs.Length; i++)
                {
                    var p = pcs[i];
                    if (!p.movable) continue;
                    foreach (var b in Reach(g, an, i))
                    {
                        var an2 = (byte[])an.Clone(); an2[i] = (byte)b;
                        var k = Pack(an2, rots: rots);
                        if (res.ContainsKey(k)) continue;
                        res[k] = (an2, rots, dist);
                        next.Add((an2, rots));
                    }
                    if (p.rotIdx >= 0 && p.period > 1)
                    {
                        int r2 = TapRot(rots, p, -1);
                        var k = Pack(an, rots: r2);
                        if (res.ContainsKey(k)) continue;
                        res[k] = (an, r2, dist);
                        next.Add((an, r2));
                    }
                }
            }
            frontier = next;
        }
        return res;
    }

    static int Main(string[] args)
    {
        var L = JsonNode.Parse(File.ReadAllText(args[0]))!;
        int lo = int.Parse(args[1]), hi = int.Parse(args[2]);
        var rng = new Random(int.Parse(args[3]));
        double minUse = args.Length > 4 ? double.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture) : 0;
        int cap = args.Length > 5 ? int.Parse(args[5]) : 3000000;
        Load(L);
        if (consume) return ConsumeMain(L, lo, hi, rng, minUse, cap);
        var solved = L["pieces"]!.AsArray().Select(p => { var (x, y) = XY((string)p!["at"]!); return (byte)(y * W + x); }).ToArray();
        var comp = Component(solved, cap);
        int maxDist = comp.Values.Max(v => v.dist);
        int movable = pcs.Count(p => p.movable);
        var o = new JsonObject { ["comp"] = comp.Count, ["maxdist"] = maxDist, ["ok"] = false };
        // candidates: layouts whose slide distance is near the top of the band first, a little shuffled
        var cands = comp.Values.Where(v => v.dist >= lo).OrderBy(v => Math.Abs(v.dist - hi) + rng.NextDouble() * 3).ToList();
        int measured = 0, bestMoves = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var (an, rots, dist) in cands)
        {
            if (measured >= 60 || sw.Elapsed.TotalSeconds > 240) break;
            curRots = rots;
            var g = Occ(an, 0);
            if (Deliver(g, 0, 0, false, 0).done != 0) continue; // a cat could walk home right away
            measured++;
            var path = Solve(an, rots, hi, cap);
            if (path == null) continue; // needs more than hi drags (or too big): try a nearer one
            bestMoves = Math.Max(bestMoves, path.Count);
            if (path.Count < lo) continue;
            int used = path.Select(m => m.piece).Distinct().Count();
            if (used < minUse * movable) continue;
            o["ok"] = true;
            o["start"] = string.Join(" ", an.Select(a => $"{a % W},{a / W}"));
            o["solution"] = string.Join(" ", path.Select(m => m.to < 0 ? $"{m.piece}:R" : $"{m.piece}:{m.to % W},{m.to / W}"));
            o["rot"] = string.Join(" ", Enumerable.Range(0, pcs.Length).Where(i => pcs[i].rotIdx >= 0 && (rots >> 2 * pcs[i].rotIdx & 3) != 0)
                .Select(i => $"{i}:{rots >> 2 * pcs[i].rotIdx & 3}"));
            o["moves"] = path.Count; o["used"] = used; o["movable"] = movable; o["dist"] = dist;
            break;
        }
        o["measured"] = measured; o["best"] = bestMoves; o["secs"] = Math.Round(sw.Elapsed.TotalSeconds, 1);
        Console.WriteLine(o.ToJsonString());
        return 0;
    }

    // ------------------------------------------------------------ consume levels
    // The routes of the cats overlap, so there is no single solved layout: the blocks are dropped at random free
    // spots (fixed blocks stay where the file puts them) and each layout is played with the exact solver.
    static byte[]? RandomLayout(Random rng, JsonNode L)
    {
        var g = Occ(new byte[pcs.Length], 0, -1); // walls, holes and cats only
        var an = new byte[pcs.Length];
        var order = Enumerable.Range(0, pcs.Length).OrderBy(i => pcs[i].movable ? 1 : 0).ThenBy(_ => rng.Next()).ToList();
        foreach (int i in order)
        {
            var p = pcs[i];
            var opts = new List<int>();
            if (!p.movable)
            {
                var (x, y) = XY((string)L["pieces"]![i]!["at"]!); opts.Add(y * W + x);
            }
            else
                for (int a = 0; a < N; a++) if (p.fit[a] != null) opts.Add(a);
            opts = opts.Where(a => p.fit[a] != null && p.fit[a]!.All(c => g[c] == -1)).ToList();
            if (opts.Count == 0) return null;
            int pick = opts[rng.Next(opts.Count)];
            an[i] = (byte)pick;
            var f = p.fit[pick]!;
            for (int k = 0; k < f.Length; k++) g[f[k]] = i * 8 + k;
        }
        return an;
    }

    static int ConsumeMain(JsonNode L, int lo, int hi, Random rng, double minUse, int cap)
    {
        int movable = pcs.Count(p => p.movable);
        var o = new JsonObject { ["ok"] = false };
        int measured = 0, tried = 0, solved = 0, bestMoves = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < 240 && measured < 400)
        {
            tried++;
            var an = RandomLayout(rng, L);
            if (an == null) { if (tried > 2000 && measured == 0) break; continue; }
            int rots = 0;
            for (int i = 0; i < pcs.Length; i++)
                if (pcs[i].rotIdx >= 0 && pcs[i].period > 1) rots |= rng.Next(pcs[i].period) << 2 * pcs[i].rotIdx;
            curRots = rots;
            var g = Occ(an, 0);
            int gone = 0;
            if (Deliver(g, 0, 0, false, 0, ref gone).done != 0) continue; // a cat could walk home right away
            measured++;
            var path = Solve(an, rots, hi, cap);
            if (path == null) continue;
            solved++;
            bestMoves = Math.Max(bestMoves, path.Count);
            if (path.Count < lo) continue;
            int used = path.Select(m => m.piece).Distinct().Count();
            if (used < minUse * movable) continue;
            o["ok"] = true;
            o["start"] = string.Join(" ", an.Select(a => $"{a % W},{a / W}"));
            o["solution"] = string.Join(" ", path.Select(m => m.to < 0 ? $"{m.piece}:R" : $"{m.piece}:{m.to % W},{m.to / W}"));
            o["rot"] = string.Join(" ", Enumerable.Range(0, pcs.Length).Where(i => pcs[i].rotIdx >= 0 && (rots >> 2 * pcs[i].rotIdx & 3) != 0)
                .Select(i => $"{i}:{rots >> 2 * pcs[i].rotIdx & 3}"));
            o["moves"] = path.Count; o["used"] = used; o["movable"] = movable; o["comp"] = 0; o["dist"] = 0;
            break;
        }
        o["measured"] = measured; o["solved"] = solved; o["best"] = bestMoves; o["secs"] = Math.Round(sw.Elapsed.TotalSeconds, 1);
        Console.WriteLine(o.ToJsonString());
        return 0;
    }
}
