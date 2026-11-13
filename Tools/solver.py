"""Exact Cat Pipe solver: the fewest drags from a start layout to "every cat home", playing like the game
(one drag = slide one block to any spot it can reach; after each drag every cat with a route goes home, in order;
frozen cats wait for N other cats; keys open locks; block ice melts by 1 per drag; gates count as open).
Used by gen_levels.py to pick start layouts that are exactly as hard as the level should be."""
from collections import deque
from catpipe_logic import STEP, NESW, opp, piece_masks, xy, edge


class Level:
    def __init__(self, L):
        self.L = L
        W, H = self.W, self.H = L["w"], L["h"]
        self.walls = {xy(s)[1] * W + xy(s)[0] for s in (L.get("walls") or "").split()}
        self.pcs = []
        for p in L["pieces"]:
            offs, masks, junc = piece_masks(p)
            # every anchor where the piece fits inside the board -> tuple of cell indices
            fit = {}
            for ay in range(H):
                for ax in range(W):
                    cells = []
                    for ox, oy in offs:
                        x, y = ax + ox, ay + oy
                        if not (0 <= x < W and 0 <= y < H): break
                        cells.append(y * W + x)
                    else:
                        fit[ay * W + ax] = tuple(cells)
            self.pcs.append(dict(
                offs=offs, masks=masks, junc=junc, fit=fit, color=p.get("color", 0),
                oneway=NESW.index(p["oneway"]) if p.get("oneway") else -1,
                movable=not p.get("fixed") and not p.get("gate"), ice=p.get("ice", 0),
                lock=p.get("lock", False), key=p.get("key", False),
                axis={"H": 0, "V": 1}.get(p.get("axis"), -1)))
        self.max_ice = max([q["ice"] for q in self.pcs] + [0])
        self.cats = []
        for c in L["cats"]:
            out, d = edge(c["side"], c["index"], W, H)
            self.cats.append((out[0] + STEP[d][0], out[1] + STEP[d][1], d, c["color"], c.get("ice", 0)))
        self.nests = {}
        for k, n in enumerate(L["nests"]):
            self.nests[edge(n["side"], n["index"], W, H)[0]] = (k, n["color"])

    def anchors(self, at_key="at"):
        return tuple(xy(p[at_key])[1] * self.W + xy(p[at_key])[0] for p in self.L["pieces"])

    # ---------------------------------------------------------------- board
    def occ(self, anchors):
        g = [-1] * (self.W * self.H)
        for c in self.walls: g[c] = -2
        for i, a in enumerate(anchors):
            for k, c in enumerate(self.pcs[i]["fit"][a]): g[c] = i * 8 + k  # piece index and cell index
        return g

    def reach(self, g, anchors, i):
        """Anchors piece i can slide to (flood fill through free cells)."""
        p, W = self.pcs[i], self.W
        start = anchors[i]
        seen, todo = {start}, [start]
        steps = []
        if p["axis"] != 1: steps += [1, -1]
        if p["axis"] != 0: steps += [W, -W]
        while todo:
            a = todo.pop()
            for s in steps:
                b = a + s
                if b in seen: continue
                if s in (1, -1) and b // W != a // W: continue
                cells = p["fit"].get(b)
                if cells is None: continue
                if any(g[c] != -1 and g[c] // 8 != i for c in cells): continue
                seen.add(b); todo.append(b)
        seen.discard(start)
        return seen

    # ---------------------------------------------------------------- cats
    def route(self, g, cat, keys, ice_gone, taken):
        """Same as the game: BFS that never revisits a cell; returns (nest index, picked key) or None."""
        W, H = self.W, self.H
        x0, y0, d0, color, _ = cat
        q = deque([(x0, y0, d0, keys, 0)])
        seen = set()
        nodes = 0
        while q and nodes < 60000:
            x, y, d, key, visited = q.popleft()
            nodes += 1
            if not (0 <= x < W and 0 <= y < H):
                n = self.nests.get((x, y))
                if n and n[1] == color and n[0] not in taken: return n[0], key
                continue
            cell = y * W + x
            bit = 1 << cell
            if visited & bit: continue
            v = g[cell]
            if v < 0: continue
            pi, ci = v // 8, v % 8
            p = self.pcs[pi]
            m, enter = p["masks"][ci], opp(d)
            if not (m >> enter) & 1: continue
            if p["color"] and p["color"] != color: continue
            if p["oneway"] >= 0 and d != p["oneway"]: continue
            if p["ice"] > ice_gone or (p["lock"] and not key): continue
            key2 = key or p["key"]
            vis2 = visited | bit
            for nd in range(4):
                if nd == enter or not (m >> nd) & 1: continue
                if m == 15 and not p["junc"][ci] and nd != d: continue
                s = (x + STEP[nd][0], y + STEP[nd][1], nd, key2, vis2)
                if s[:4] + (vis2,) in seen: continue
                seen.add(s)
                q.append(s)
        return None

    def flood(self, g, cat, keys, ice_gone):
        """Cells a cat can reach (revisits allowed: a cheap superset of its real routes)."""
        W, H = self.W, self.H
        x0, y0, d0, color, _ = cat
        seen, todo, cells = set(), [(x0, y0, d0)], set()
        while todo:
            x, y, d = todo.pop()
            if (x, y, d) in seen or not (0 <= x < W and 0 <= y < H): continue
            seen.add((x, y, d))
            v = g[y * W + x]
            if v < 0: continue
            pi, ci = v // 8, v % 8
            p = self.pcs[pi]
            m, enter = p["masks"][ci], opp(d)
            if not (m >> enter) & 1 or (p["color"] and p["color"] != color): continue
            if p["oneway"] >= 0 and d != p["oneway"]: continue
            if p["ice"] > ice_gone: continue  # locks ignored: this is only a superset
            cells.add(y * W + x)
            for nd in range(4):
                if nd != enter and (m >> nd) & 1: todo.append((x + STEP[nd][0], y + STEP[nd][1], nd))
        return cells

    def deliver(self, g, done, taken, keys, moves):
        """Send home every cat that can go (in order, like Resolve). Returns new (done, taken, keys)."""
        progress = True
        while progress:
            progress = False
            for k, cat in enumerate(self.cats):
                if done >> k & 1 or cat[4] > bin(done).count("1"): continue
                r = self.route(g, cat, keys, moves, taken)
                if r:
                    done |= 1 << k
                    taken = taken | {r[0]}
                    keys = keys or r[1]
                    progress = True
                    break
        return done, frozenset(taken), keys

    # ---------------------------------------------------------------- search
    def solve(self, start, cap=60000):
        """BFS on drags. Returns (moves list [(piece, to_anchor)], explored) or (None, depth_reached)."""
        allc = (1 << len(self.cats)) - 1
        g = self.occ(start)
        done, taken, keys = self.deliver(g, 0, frozenset(), False, 0)
        s0 = (start, done, taken, keys, 0)
        if done == allc: return [], 0
        key = lambda s: (s[0], s[1], s[2], s[3], min(s[4], self.max_ice))
        prev = {key(s0): None}
        frontier, depth = [s0], 0
        while frontier:
            depth += 1
            nxt = []
            for s in frontier:
                anchors, done, taken, keys, moves = s
                g = self.occ(anchors)
                W = self.W
                # a new route can only appear through the moved block's new cells (or when ice melts)
                touch = set()
                for k, cat in enumerate(self.cats):
                    if done >> k & 1: continue
                    touch.add(cat[1] * W + cat[0] if 0 <= cat[0] < W and 0 <= cat[1] < self.H else -1)
                    for c in self.flood(g, cat, keys, moves):
                        touch.add(c)
                        if c % W: touch.add(c - 1)
                        if c % W < W - 1: touch.add(c + 1)
                        touch.add(c - W); touch.add(c + W)
                melts = any(p["ice"] == moves + 1 for p in self.pcs)
                for i, p in enumerate(self.pcs):
                    if not p["movable"] or p["ice"] > moves or (p["lock"] and not keys): continue
                    for b in self.reach(g, anchors, i):
                        a2 = anchors[:i] + (b,) + anchors[i + 1:]
                        g2 = list(g)
                        for c in p["fit"][anchors[i]]: g2[c] = -1
                        for k, c in enumerate(p["fit"][b]): g2[c] = i * 8 + k
                        if melts or any(c in touch for c in p["fit"][b]):
                            d2, t2, k2 = self.deliver(g2, done, taken, keys, moves + 1)
                        else:
                            d2, t2, k2 = done, taken, keys
                        s2 = (a2, d2, t2, k2, moves + 1)
                        ks = key(s2)
                        if ks in prev: continue
                        prev[ks] = (key(s), i, b)
                        if d2 == allc:
                            path, cur = [], ks
                            while prev[cur]: pk, pi, pb = prev[cur]; path.append((pi, pb)); cur = pk
                            return path[::-1], len(prev)
                        nxt.append(s2)
                        if len(prev) > cap: return None, depth
            frontier = nxt
        return None, depth

    def scramble(self, solved, steps, rng):
        """Random legal drags away from the solved layout, favouring blocks that carry a path, and going on
        until no cat could walk home right away."""
        anchors = solved
        movable = [i for i, p in enumerate(self.pcs) if p["movable"] and not p["ice"] and not p["lock"]]
        if not movable: return anchors
        weights = [3 if any(m for m in self.pcs[i]["masks"]) else 1 for i in movable]
        for s in range(steps + 80):
            g = self.occ(anchors)
            if s >= steps and not self.deliver(g, 0, frozenset(), False, 0)[0]: break
            i = rng.choices(movable, weights)[0]
            opts = sorted(self.reach(g, anchors, i))
            if opts: anchors = anchors[:i] + (rng.choice(opts),) + anchors[i + 1:]
        return anchors
