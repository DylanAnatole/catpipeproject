"""Cat Pipe rules in Python, kept in sync with GameController.TraceFrom / Piece.PathMask.
Used by check_levels.py and gen_levels.py to prove every level can be finished."""
from collections import deque

STEP = [(0, -1), (1, 0), (0, 1), (-1, 0)]  # N E S W, y grows downward
NESW = "NESW"
SHAPES = ["1,0 0,1 1,1 2,1", "1,0 0,1 1,1 1,2", "0,0 1,0 2,0 1,1", "0,0 0,1 1,1 0,2", "0,0 1,0 2,0 2,1", "0,0 1,0 2,0 0,1",
          "0,0 0,1 1,1 2,1", "2,0 0,1 1,1 2,1", "0,0 1,0 1,1 1,2", "0,0 1,0 0,1 0,2", "0,0 0,1 0,2 1,2", "1,0 1,1 0,2 1,2",
          "0,0", "0,0 1,0 0,1 1,1", "1,0 0,1 1,1 2,1 1,2", "0,0 0,1 1,1", "1,0 0,1 1,1", "0,0 1,0 1,1", "0,0 1,0 0,1",
          "0,0 0,1 0,2", "0,0 1,0 2,0", "0,0 1,0", "0,0 0,1"]


def xy(s):
    return tuple(int(v) for v in s.split(','))


SHAPE_SETS = [frozenset(xy(c) for c in s.split()) for s in SHAPES]
NO_PATH = {SHAPE_SETS[i] for i in (0, 1, 2, 3, 13)}  # T blocks and the 2x2 square: crates, no path


def opp(d):
    return (d + 2) % 4


def popcount(m):
    return bin(m).count("1")


def mask_str(m):
    return "".join(NESW[d] for d in range(4) if (m >> d) & 1) or "-"


def parse_mask(s):
    return 0 if s == "-" else sum(1 << NESW.index(ch) for ch in s)


def normalize(offs):
    mx, my = min(o[0] for o in offs), min(o[1] for o in offs)
    return frozenset((o[0] - mx, o[1] - my) for o in offs)


def is_shape(offs):
    return normalize(offs) in SHAPE_SETS


def derived_mask(cells, o):
    """Shape = path: straight runs go straight, an L corner turns, T/+ junctions and lone cells are crossings."""
    n, e, s, w = [(o[0] + d[0], o[1] + d[1]) in cells for d in STEP]
    h, v = e or w, n or s
    if h and v and not (e and w) and not (n and s):
        return n * 1 | e * 2 | s * 4 | w * 8
    if h and not v: return 0b1010
    if v and not h: return 0b0101
    return 0b1111


def out_dirs(offs, masks, oneway):
    """One-way block: the only side each cell may be left by (walk back from the exit end)."""
    out = [-1] * len(offs)
    cur = next(i for i, o in enumerate(offs) if masks[i] >> oneway & 1 and (o[0] + STEP[oneway][0], o[1] + STEP[oneway][1]) not in offs)
    d_out = oneway
    for _ in offs:
        out[cur] = d_out
        back = [d for d in range(4) if d != d_out and masks[cur] >> d & 1]
        if not back: break
        b = back[-1]
        nxt = (offs[cur][0] + STEP[b][0], offs[cur][1] + STEP[b][1])
        if nxt not in offs: break  # the entry end of the path
        cur = offs.index(nxt)
        d_out = opp(b)
    return out


def piece_masks(p):
    offs = [xy(c) for c in p["cells"].split()]
    cs = set(offs)
    masks = [0] * len(offs) if normalize(offs) in NO_PATH else [derived_mask(cs, o) for o in offs]
    junction = [False] * len(offs)
    if p.get("paths"):
        for i, t in enumerate(p["paths"].split()):
            masks[i] = parse_mask(t)
            junction[i] = popcount(masks[i]) >= 3
    if p.get("oneway"):
        return offs, masks, junction, out_dirs(offs, masks, NESW.index(p["oneway"]))
    return offs, masks, junction, None


def edge(side, i, W, H):
    return {"L": ((-1, i), 1), "R": ((W, i), 3), "T": ((i, -1), 2), "B": ((i, H), 0)}[side]


def board(L):
    """cell -> (piece index, mask, junction); also returns errors (overlap / bounds / bad shape)."""
    W, H = L["w"], L["h"]
    occ, errors = {}, []
    walls = {xy(s) for s in (L.get("walls") or "").split()}
    for c in walls:
        occ[c] = None
    for e in L["cats"] + L["nests"]:  # cats / holes sitting on board cells
        if e.get("at"): occ[xy(e["at"])] = None
    for pi, p in enumerate(L["pieces"]):
        offs, masks, junc, outs = piece_masks(p)
        if not is_shape(offs):
            errors.append(f"piece {pi} is not an original block shape")
        a = xy(p["at"])
        for k, (o, m, j) in enumerate(zip(offs, masks, junc)):
            c = (a[0] + o[0], a[1] + o[1])
            if not (0 <= c[0] < W and 0 <= c[1] < H) or c in occ:
                errors.append(f"piece {pi} out of bounds / overlap at {c}")
            occ[c] = (pi, m, j, outs[k] if outs else -1)
    return occ, errors


def route(L, occ, cat, keys, occupied, ignore=False):
    """BFS like the game: returns (cells, nest_pos, picked_key) to a free hole of the cat's color, or None."""
    W, H = L["w"], L["h"]
    nests = {(xy(n["at"]) if n.get("at") else edge(n["side"], n["index"], W, H)[0]): n["color"] for n in L["nests"]}
    # states carry the cells already on the route: a cat never passes the same cell twice (no loops)
    if cat.get("at"):
        c0 = xy(cat["at"])
        starts = [((c0[0] + STEP[d][0], c0[1] + STEP[d][1]), d, keys, 1 << (c0[1] * W + c0[0])) for d in range(4)]
    else:
        out, d0 = edge(cat["side"], cat["index"], W, H)
        starts = [((out[0] + STEP[d0][0], out[1] + STEP[d0][1]), d0, keys, 0)]
    prev = {s0: None for s0 in starts}
    q = deque(starts)
    while q:
        st = q.popleft()
        pos, d, key, visited = st
        if not (0 <= pos[0] < W and 0 <= pos[1] < H) or pos in nests:
            if nests.get(pos) == cat["color"] and (ignore or pos not in occupied):
                cells, cur = [], prev[st]
                picked = False
                while cur is not None:
                    cells.append(cur[0])
                    picked = picked or occ[cur[0]] and L["pieces"][occ[cur[0]][0]].get("key", False)
                    cur = prev[cur]
                return cells[::-1], pos, picked
            continue
        bit = 1 << (pos[1] * W + pos[0])
        if visited & bit:
            continue
        cell = occ.get(pos)
        if not cell:
            continue
        pi, m, junc, out1 = cell
        p = L["pieces"][pi]
        enter = opp(d)
        if not (m >> enter) & 1:
            continue
        if p.get("color", 0) and p["color"] != cat["color"]:
            continue
        if not ignore and p.get("lock") and not key:
            continue
        key2 = key or p.get("key", False)
        for nd in range(4):
            if nd == enter or not (m >> nd) & 1:
                continue
            if m == 15 and not junc and nd != d:
                continue  # plain crossing: straight only
            if out1 >= 0 and nd != out1:
                continue  # one-way: only toward the exit
            ns = ((pos[0] + STEP[nd][0], pos[1] + STEP[nd][1]), nd, key2, visited | bit)
            if ns not in prev:
                prev[ns] = st
                q.append(ns)
    return None


def all_cats(L):
    """Cats one by one: a bed with `count` holds that many cats of the same color."""
    return [c for c in L["cats"] for _ in range(c.get("count", 1))]


def simulate(L):
    """Play the SOLVED layout like the game does: any cat with a route goes home; frozen cats wait for
    `ice` other cats; keys open locks. Returns (all_home, order). Gates count as open, block ice as melted
    (both happen with time / drops while the scramble is undone)."""
    occ, _ = board(L)
    cats = all_cats(L)
    done, order, occupied = set(), [], set()
    keys = False
    progress = True
    while progress:
        progress = False
        for i, cat in enumerate(cats):
            if i in done or cat.get("ice", 0) > len(done):
                continue
            r = route(L, occ, cat, keys, occupied)
            if r:
                cells, nest, picked = r
                done.add(i)
                order.append(i)
                occupied.add(nest)
                keys = keys or picked
                progress = True
                break
    return len(done) == len(cats), order


def validate(L):
    occ, errors = board(L)
    ok, order = simulate(L)
    if not ok:
        errors.append(f"not every cat can get home (delivered {order} of {len(all_cats(L))})")
    if len(L["nests"]) != len(all_cats(L)):
        errors.append("holes and cats differ in number")
    for i, cat in enumerate(L["cats"]):
        if not route(L, occ, cat, True, set(), ignore=True):
            errors.append(f"cat {i} (color {cat['color']}) has no route in the solved layout")
    return errors
