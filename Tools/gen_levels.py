"""Generates the 50 Cat Pipe levels (v5: designer rules of 2026-11-11).
Usage: python Tools/gen_levels.py <out dir> [level numbers...]

A level = a solved layout (every cat has a road home) with just enough blocks (road blocks plus a few crates /
decoys), then a start layout far from it, measured by Tools/DeepSolver (C#): the fewest moves to win must fall in
the level's band and the solution must move (almost) every block, so no block is filler.
Curriculum: levels 1-10 onboarding (hand on 1-3 only; a new idea gets the "Mới" card with a live demo).
From 11: cycles of 5 levels = 3 easy + 1 medium + 1 hard / very hard / super hard (rotating). Hard levels use few
blocks (10-12) on a tight board (spare cells become walls) that each move several times, with 2-3 mechanics mixed.
Every level has at most 5 minutes.
Path rules: straights and L blocks carry their own shape as path (both ways or one-way); single cells are
straight / corner / T / cross; T blocks and the 2x2 square are crates. A rotating single turns 90 degrees per tap.
A bed can hold several cats of one color; they split at a junction to several holes of that color."""
import json, os, random, subprocess, sys, tempfile
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from catpipe_logic import STEP, NESW, opp, popcount, mask_str, derived_mask, is_shape, validate, all_cats

SOLVER = os.path.join(os.path.dirname(os.path.abspath(__file__)), "bin", "deepsolver", "DeepSolver.exe")

# ------------------------------------------------------------------ curriculum
LEVELS = 50   # the game has 50 levels
ONB_END = 10  # levels 1..ONB_END are onboarding; the cycles of 5 start right after
INTRO = {  # level: (feature, icon, name, one-line explanation for the "New!" card)
    # onboarding: 1-3 basics (drag, turn, two cats), then one new idea per level
    4: ("cross", "blocks/b0_15", "Ngã tư", "Mèo đi thẳng qua ngã tư, không rẽ."),
    5: ("dead", "blocks/sh_14", "Thùng gỗ", "Thùng gỗ không có đường: kéo nó ra khỏi lối đi."),
    6: ("junction", "hub_ring", "Ngã ba", "Ở ô có vòng tròn, mèo được rẽ sang lối khác."),
    7: ("special", "blocks/b0_13", "Ô đơn", "Ô đơn có đường riêng: thẳng, góc hoặc ngã ba."),
    8: ("rotate", "ui_retry", "Khối xoay", "Chạm vào khối có mũi tên tròn để xoay đường đi."),
    9: ("multicat", "pad", "Ổ nhiều mèo", "Một ổ có nhiều mèo: mỗi hộp cùng màu nhận một con."),
    10: ("oneway", "oneway_arrow", "Một chiều", "Mèo chỉ đi theo hướng mũi tên."),
    # after onboarding: one new idea on the first (easy) level of each cycle of 5
    11: ("ice", "ice", "Khối băng", "Khối băng tan dần sau mỗi lần kéo, tan hết mới đi được."),
    16: ("keylock", "key", "Chìa và khóa", "Mèo đi qua chìa thì ổ khóa mở ra."),
    21: ("inner", "hole", "Mèo trong ô", "Mèo ngồi trong bàn: mở lối cho mèo ra hộp."),
    26: ("frozen", "ice", "Mèo đóng băng", "Mèo băng chờ đủ số mèo khác về hộp mới tan."),
    31: ("color", "blocks/b1_13", "Khối màu", "Khối màu chỉ cho mèo cùng màu đi qua."),
    36: ("fixed", "wall", "Khối cố định", "Khối xám không kéo được, hãy tìm lối khác."),
    41: ("axis", "axis_arrow", "Khóa trục", "Khối có mũi tên chỉ trượt theo một hướng."),
    46: ("mystery", "blocks/hid_13", "Khối ẩn", "Kéo khối ẩn để lộ đường bên trong."),
    48: ("bomb", "bomb", "Bom", "Đưa mèo đi qua bom trước khi bom đếm về 0."),
}
ICON = {f: (icon, name) for _, (f, icon, name, _) in INTRO.items()}
HEAVY = {"color", "fixed", "ice", "keylock", "frozen", "oneway", "axis", "mystery", "bomb", "inner", "rotate", "multicat"}
BANDS = {"easy": (5, 8), "medium": (10, 14), "hard": (18, 22), "vhard": (23, 27), "super": (28, 35)}
DIFF = {"hard": 1, "vhard": 2, "super": 3}
DIFF_NAME = {1: "Màn khó", 2: "Cực khó", 3: "Siêu khó"}


def tier(n):
    if n <= ONB_END: return "onb"
    k = (n - ONB_END - 1) % 5
    if k < 3: return "easy"
    if k == 3: return "medium"
    return ("hard", "vhard", "super")[((n - ONB_END - 1) // 5) % 3]


def target(n):
    """(fewest moves min, max) and the share of movable blocks the solution must move."""
    t = tier(n)
    if t == "onb":
        if n <= 2: return (1, 2), 1.0
        if n == 3: return (2, 3), 1.0
        if n in INTRO: return (2, 4), 1.0
        return ((3, 5) if n <= 14 else (4, 6)), 1.0
    if n in INTRO: return (4, 6), 1.0
    return BANDS[t], 1.0  # hard levels too: few blocks, and every one of them has to move


# Demo set (the project became a 10-level demo): no onboarding, every level simply uses its mechanics.
# Built as late-game levels (internal numbers 101-110, so every rule for experienced players applies), saved as 1-10.
DEMO = {
    101: ("easy", ["rotate"]),
    102: ("easy", ["multicat", "junction"]),
    103: ("medium", ["ice", "oneway"]),
    104: ("easy", ["keylock"]),
    105: ("hard", ["color", "rotate", "multicat"]),
    106: ("medium", ["inner", "fixed"]),
    107: ("easy", ["mystery", "cross"]),
    108: ("vhard", ["frozen", "axis", "ice"]),
    109: ("medium", ["bomb", "keylock"]),
    110: ("super", ["oneway", "rotate", "color"]),
}


def demo_config(n, rng):
    t, feats = DEMO[n]
    f = feats[0]
    icon, name = ICON[f]
    c = dict(n=n, tier=t, feats=set(feats) | {"dead"}, tutorial=False, intro=None, walls=0, diff=DIFF.get(t, 0), dup=0,
             inner=0, pad=0, target=BANDS[t], use=1.0)
    size = {"easy": (5, 5), "medium": (5, 6), "hard": (5, 6), "vhard": (5, 6), "super": (5, 6)}[t]
    ncat = {"easy": rng.randint(2, 3), "medium": 3, "hard": 3, "vhard": 3, "super": 3}[t]
    free = {"easy": (3, 4), "medium": (3, 4), "hard": (2, 3), "vhard": (1, 2), "super": (1, 2)}[t]
    c["cap"] = {"hard": 10, "vhard": 11, "super": 12}.get(t)
    c.update(w=size[0], h=size[1], cats=ncat, turns=(0, 3) if t == "easy" else (1, 3), free=free)
    if c["diff"]: c["hint"], c["hint_icon"] = DIFF_NAME[c["diff"]], ("map_hard" if c["diff"] == 1 else "bomb")
    else: c["hint"], c["hint_icon"] = name, icon
    fs = c["feats"]
    c["cross"] = c["special"] = True
    c["merge"] = "junction" in fs or "multicat" in fs or rng.random() < 0.5
    if "multicat" in fs: c["pad"] = 2 if t == "easy" else rng.randint(2, 3)
    if "inner" in fs: c["inner"] = rng.randint(1, 2)
    if c["diff"] and rng.random() < 0.5: c["dup"] = 1
    return c


def config(n, rng):
    if n in DEMO: return demo_config(n, rng)
    t = tier(n)
    unlocked = [f for lv, (f, *_r) in INTRO.items() if lv <= n]
    newest = max((lv for lv in INTRO if lv <= n), default=None)
    c = dict(n=n, tier=t, feats=set(), tutorial=False, intro=None, turns=(0, 2), merge=False, cross=False, special=False,
             walls=0, diff=DIFF.get(t, 0), dup=0, inner=0, pad=0)
    c["target"], c["use"] = target(n)
    if n <= 3:  # first steps: drag, a turn, two cats
        c.update(w=4, h=4, cats=[1, 1, 2][n - 1], turns=[(0, 0), (1, 1), (0, 2)][n - 1], free=(3, 5), tutorial=True,
                 hint=["Kéo khối", "Rẽ góc", "2 chú mèo"][n - 1], hint_icon="paw")
        return c
    if n in INTRO:  # intro: small board, the new idea is what you have to use
        f, icon, name, desc = INTRO[n]
        c.update(intro=(icon, name, desc), hint=name, hint_icon=icon, feats={f}, free=(3, 4),  # no hand: the popup demo teaches it
                 w=4 + (n > 8), h=5 + (n > ONB_END), cats=2)
    elif t == "onb":  # practice the newest idea
        f = INTRO[newest][0]
        c.update(feats={f}, hint=INTRO[newest][2], hint_icon=INTRO[newest][1], free=(3, 4),
                 w=4 + (n > 8), h=5 + (n > 14), cats=2 + (n > 10))
    else:
        f = INTRO[newest][0]
        pool = sorted(x for x in unlocked if x in HEAVY and x != f)
        icon, name = ICON[f]
        size = {"easy": (5, 5), "medium": (5, 6), "hard": (5, 6), "vhard": (5, 6), "super": (5, 6)}[t]
        ncat = {"easy": rng.randint(2, 3), "medium": 3, "hard": 3, "vhard": 3, "super": 3}[t]
        free = {"easy": (3, 4), "medium": (3, 4), "hard": (2, 3), "vhard": (1, 2), "super": (1, 2)}[t]
        extra = {"easy": rng.randint(0, 1), "medium": 1, "hard": 2, "vhard": rng.randint(2, 3), "super": 3}[t]
        # hard = few blocks that each move several times (not a crowded board): block cap per tier
        c["cap"] = {"hard": 10, "vhard": 11, "super": 12}.get(t)
        c.update(w=size[0], h=size[1], cats=ncat, turns=(0, 3) if t == "easy" else (1, 3), free=free)
        c["feats"] = {f} | set(rng.sample(pool, k=min(len(pool), extra)))
        if c["diff"]: c["hint"], c["hint_icon"] = DIFF_NAME[c["diff"]], ("map_hard" if c["diff"] == 1 else "bomb")
        else: c["hint"], c["hint_icon"] = name, icon
        c["feats"].add("dead")
        if rng.random() < (0.3 if t == "easy" else 0.5): c["feats"].add("multicat")
        if c["diff"] and rng.random() < 0.5: c["dup"] = 1
    fs = c["feats"]
    c["cross"] = "cross" in fs or n > 4
    c["special"] = "special" in fs or "rotate" in fs or n > 7
    c["merge"] = "junction" in fs or "multicat" in fs or (n > 6 and rng.random() < 0.5)
    if "multicat" in fs: c["pad"] = 2 if t in ("onb", "easy") else rng.randint(2, 3)
    if "inner" in fs: c["inner"] = 1 if n in INTRO else rng.randint(1, 2)
    if n > ONB_END and rng.random() < 0.3: c["walls"] = 1
    return c


# ------------------------------------------------------------------ routes

def inside(c, W, H):
    return 0 <= c[0] < W and 0 <= c[1] < H


def manhattan(a, b, horiz_first):
    out, (x, y) = [], a
    for axis in ((0, 1) if horiz_first else (1, 0)):
        if axis == 0:
            while x != b[0]: x += 1 if b[0] > x else -1; out.append((x, y))
        else:
            while y != b[1]: y += 1 if b[1] > y else -1; out.append((x, y))
    return out


def make_route(start, d_in, end, d_out, W, H, rng, turns, blocked):
    """Random Manhattan path start -> end through 0-2 waypoints. Returns list of (cell, mask, out dir) or None."""
    for _ in range(40):
        pts = [start] + [(rng.randrange(W), rng.randrange(H)) for _ in range(rng.randint(0, 2))] + [end]
        cells = [start]
        for a, b in zip(pts, pts[1:]):
            cells += manhattan(a, b, rng.random() < 0.5)
        if len(set(cells)) != len(cells) or any(x in blocked for x in cells): continue
        moves = [d_in] + [next(d for d in range(4) if (cells[i][0] + STEP[d][0], cells[i][1] + STEP[d][1]) == cells[i + 1])
                          for i in range(len(cells) - 1)] + [d_out]
        masks = [(1 << opp(moves[i])) | (1 << moves[i + 1]) for i in range(len(cells))]
        if any(popcount(m) != 2 for m in masks): continue
        t = sum(moves[i] != moves[i + 1] for i in range(len(moves) - 1))
        if not (turns[0] <= t <= turns[1]): continue
        return [(cells[i], masks[i], moves[i + 1]) for i in range(len(cells))]
    return None


def straight(m):
    return m in (0b0101, 0b1010)


def compatible(old, new, c):
    if not old: return True
    union = new
    for m in old: union |= m
    if c["merge"]: return True
    return c["cross"] and len(old) == 1 and straight(old[0]) and straight(new) and old[0] != new and union == 15


def split_prefix(r2, others):
    """Routes from one bed: r2 may share only a common beginning with each other route, then must split."""
    cells2 = [x for x, _, _ in r2]
    for o in others:
        co = [x for x, _, _ in o]
        k = 0
        while k < len(cells2) and k < len(co) and cells2[k] == co[k]: k += 1
        if k == 0 or k == len(cells2) or k == len(co): return False
        if set(cells2[k:]) & set(co): return False
    return True


EDGE = lambda side, i, W, H: {"L": ((-1, i), 1), "R": ((W, i), 3), "T": ((i, -1), 2), "B": ((i, H), 0)}[side]

# fill shapes: crates and decoy roads, biggest first so few blocks fill the gaps
CRATES = ["1,0 0,1 1,1 2,1", "1,0 0,1 1,1 1,2", "0,0 1,0 2,0 1,1", "0,0 0,1 1,1 0,2", "0,0 1,0 0,1 1,1"]
ROADS = ["0,0 1,0 2,0 2,1", "0,0 1,0 2,0 0,1", "0,0 0,1 1,1 2,1", "2,0 0,1 1,1 2,1",
         "0,0 1,0 1,1 1,2", "0,0 1,0 0,1 0,2", "0,0 0,1 0,2 1,2", "1,0 1,1 0,2 1,2",
         "0,0 1,0 2,0", "0,0 0,1 0,2", "0,0 0,1 1,1", "1,0 0,1 1,1", "0,0 1,0 1,1", "0,0 1,0 0,1", "0,0 1,0", "0,0 0,1"]


def parse(sh):
    return [tuple(int(v) for v in s.split(",")) for s in sh.split()]


def abs_cells(p):
    ax, ay = (int(v) for v in p["at"].split(","))
    return [(ax + o[0], ay + o[1]) for o in parse(p["cells"])]


# ------------------------------------------------------------------ level

def build(n, c, rng):
    W, H, ncats = c["w"], c["h"], c["cats"]
    base = rng.sample(range(1, 10), ncats - c["dup"])
    colors = base + base[:c["dup"]]
    rng.shuffle(colors)
    used_edges, cats, nests, routes, route_color = set(), [], [], [], []
    cell_masks, reserved = {}, set()
    shared_total = 0
    sides = ["L", "R", "T", "B"]
    inner_left = c["inner"]

    def pick_hole(cat, cat_in, cc, hole_in_ok):
        hole_in = hole_in_ok
        if hole_in:
            hc = (rng.randrange(W), rng.randrange(H)); e = rng.randrange(4)
            end = (hc[0] + STEP[e][0], hc[1] + STEP[e][1])
            if hc in reserved or hc in cell_masks or not inside(end, W, H) or (cat_in and hc == cc): return None
            return dict(at=f"{hc[0]},{hc[1]}", color=cat["color"], side="L", index=0), end, opp(e), hc
        n_side = rng.choice(sides); ni = rng.randrange(H if n_side in "LR" else W)
        if (n_side, ni) in used_edges or (not cat_in and n_side == cat["side"]): return None
        out_n, d_back = EDGE(n_side, ni, W, H)
        end = (out_n[0] + STEP[d_back][0], out_n[1] + STEP[d_back][1])
        return dict(side=n_side, index=ni, color=cat["color"]), end, opp(d_back), None

    for k in range(ncats):
        bed = c["pad"] if (k == 0 and c["pad"]) else 1  # the first cat's bed may hold several cats
        for _ in range(150):
            # where the cat sits and where its hole is: on the edge, or (inner levels) on a board cell
            cat_in = bed == 1 and inner_left > 0 and rng.random() < 0.7
            hole_in = bed == 1 and (inner_left > 0 and not cat_in or (c["inner"] and rng.random() < 0.25))
            cc = None
            if cat_in:
                cc = (rng.randrange(W), rng.randrange(H)); d_in = rng.randrange(4)
                start = (cc[0] + STEP[d_in][0], cc[1] + STEP[d_in][1])
                if cc in reserved or cc in cell_masks or not inside(start, W, H): continue
                cat = dict(at=f"{cc[0]},{cc[1]}", color=colors[k], side="L", index=0)
            else:
                s_side = rng.choice(sides); si = rng.randrange(H if s_side in "LR" else W)
                if (s_side, si) in used_edges: continue
                out_s, d_in = EDGE(s_side, si, W, H)
                start = (out_s[0] + STEP[d_in][0], out_s[1] + STEP[d_in][1])
                cat = dict(side=s_side, index=si, color=colors[k])
            hole = pick_hole(cat, cat_in, cc, hole_in)
            if not hole: continue
            nest, end, d_out, hc = hole
            blocked = reserved | ({cc} if cat_in else set()) | ({hc} if hc else set())
            r = make_route(start, d_in, end, d_out, W, H, rng, c["turns"], blocked)
            if not r or not all(compatible(cell_masks.get(x, []), m, c) for x, m, _ in r): continue
            shared = sum(1 for x, _, _ in r if x in cell_masks)
            if shared_total + shared > (2 if c["diff"] == 0 else 3): continue
            # the bed's other cats: holes of the same color, routes that split from this one at a junction
            group, group_nests, tmp_edges, ok = [r], [nest], {(nest["side"], nest["index"])} if "at" not in nest else set(), True
            for _j in range(bed - 1):
                for _t in range(80):
                    used_edges_save = set(used_edges)
                    used_edges |= tmp_edges
                    h2 = pick_hole(cat, False, None, False)
                    used_edges.clear(); used_edges |= used_edges_save
                    if not h2: continue
                    n2, end2, d_out2, _hc = h2
                    if (n2["side"], n2["index"]) in tmp_edges: continue
                    r2 = make_route(start, d_in, end2, d_out2, W, H, rng, c["turns"], blocked)
                    if not r2 or not split_prefix(r2, group): continue
                    if not all(compatible(cell_masks.get(x, []), m, c) for x, m, _ in r2): continue
                    if sum(1 for x, _, _ in r2 if x in cell_masks) > 0: continue
                    group.append(r2); group_nests.append(n2); tmp_edges.add((n2["side"], n2["index"]))
                    break
                else:
                    ok = False
                    break
            if not ok: continue
            shared_total += shared
            if cat_in: reserved.add(cc); inner_left -= 1
            else: used_edges.add((cat["side"], cat["index"]))
            if hc: reserved.add(hc)
            used_edges |= tmp_edges
            for rr in group:
                for x, m, _ in rr: cell_masks.setdefault(x, []).append(m)
                routes.append(rr); route_color.append(colors[k])
            if bed > 1: cat["count"] = bed
            cats.append(cat); nests += group_nests
            break
        else:
            return None
    if c["inner"] and inner_left > 0: return None
    if c["pad"] and not any(ct.get("count", 1) > 1 for ct in cats): return None
    for e in cats + nests:  # drop the placeholder edge fields of inside cats / holes
        if "at" in e: del e["side"], e["index"]

    need, kind = {}, {}
    for x, ms in cell_masks.items():
        u = 0
        for m in ms: u |= m
        need[x] = u
        if popcount(u) == 2: kind[x] = "normal"
        elif u == 15 and all(straight(m) for m in ms) and len(set(ms)) == 2: kind[x] = "cross"
        else: kind[x] = "junction"
    if any(k == "junction" for k in kind.values()) and not c["merge"]: return None
    if "junction" in c["feats"] and not any(k == "junction" for k in kind.values()): return None
    if "cross" in c["feats"] and not any(k == "cross" for k in kind.values()): return None

    # tile each road with blocks whose own shape is the needed path (fewest single cells, then fewest blocks)
    pieces, owner, specials = [], {}, 0

    def chunk_ok(chunk):
        if not is_shape(chunk): return False
        cs = set(chunk)
        return all(derived_mask(cs, x) == need[x] for x in chunk)

    def tile(run):
        best = [(0, 0, [])] + [None] * len(run)
        for k in range(1, len(run) + 1):
            for Ln in (4, 3, 2, 1):
                if Ln > k or best[k - Ln] is None: continue
                if Ln > 1 and not chunk_ok(run[k - Ln:k]): continue
                s1, p1, cuts = best[k - Ln]
                cand = (s1 + (Ln == 1), p1 + 1 + rng.random() * 0.1, cuts + [Ln])
                if best[k] is None or cand[:2] < best[k][:2]: best[k] = cand
        return best[len(run)][2]

    for r in routes:
        cells = [x for x, _, _ in r]
        i = 0
        while i < len(cells):
            x = cells[i]
            if x in owner: i += 1; continue
            if kind[x] == "cross":
                pieces.append(dict(at=f"{x[0]},{x[1]}", cells="0,0")); owner[x] = len(pieces) - 1; i += 1; continue
            if kind[x] == "junction":
                pieces.append(dict(at=f"{x[0]},{x[1]}", cells="0,0", paths=mask_str(need[x]))); owner[x] = len(pieces) - 1
                specials += 1; i += 1; continue
            j = i
            while j < len(cells) and cells[j] not in owner and kind[cells[j]] == "normal": j += 1
            run, k = cells[i:j], 0
            for Ln in tile(run):
                chunk = run[k:k + Ln]
                if Ln == 1:
                    if not c["special"]: return None
                    y = chunk[0]
                    pieces.append(dict(at=f"{y[0]},{y[1]}", cells="0,0", paths=mask_str(need[y]))); specials += 1
                else:
                    ax, ay = min(y[0] for y in chunk), min(y[1] for y in chunk)
                    pieces.append(dict(at=f"{ax},{ay}", cells=" ".join(f"{y[0]-ax},{y[1]-ay}" for y in chunk)))
                for y in chunk: owner[y] = len(pieces) - 1
                k += Ln
            i = j
    if "special" in c["feats"] and specials == 0: return None
    junctions = sum(1 for k in kind.values() if k == "junction")
    if specials - junctions > (0 if n < 7 else 1 if n <= ONB_END else 2): return None

    # rotating singles: a route cell of its own (split off a road block when there is none)
    if "rotate" in c["feats"]:
        want = 1 if n <= ONB_END else rng.randint(1, 2)
        singles = lambda: [pi for pi, p in enumerate(pieces) if p.get("paths") and len(p["cells"].split()) == 1
                           and need[abs_cells(p)[0]] != 15 and not p.get("rotate")]
        for pi in rng.sample(range(len(pieces)), len(pieces)):
            if len(singles()) >= want: break
            p = pieces[pi]
            if p.get("paths") or len(p["cells"].split()) < 2: continue
            cs = abs_cells(p)
            for x in rng.sample(cs, len(cs)):
                rest = [y for y in cs if y != x]
                if len(rest) > 1 and not chunk_ok(rest): continue
                if len(rest) == 1:
                    pieces[pi] = dict(at=f"{rest[0][0]},{rest[0][1]}", cells="0,0", paths=mask_str(need[rest[0]]))
                else:
                    ax, ay = min(y[0] for y in rest), min(y[1] for y in rest)
                    pieces[pi] = dict(at=f"{ax},{ay}", cells=" ".join(f"{y[0]-ax},{y[1]-ay}" for y in rest))
                pieces.append(dict(at=f"{x[0]},{x[1]}", cells="0,0", paths=mask_str(need[x])))
                owner[x] = len(pieces) - 1
                break
        sl = singles()
        if not sl: return None
        rot_pieces = rng.sample(sl, min(want, len(sl)))
        for pi in rot_pieces: pieces[pi]["rotate"] = True
    else:
        rot_pieces = []

    route_pieces = list(range(len(pieces)))
    users = {pi: {k for k, r in enumerate(routes) for x, _, _ in r if owner.get(x) == pi} for pi in route_pieces}

    # fill the board: a few cells stay free, the rest is crates and decoy roads (biggest shapes first)
    for x in reserved: owner[x] = -1
    walls = []
    for _ in range(c["walls"]):
        fr = [(x, y) for x in range(1, W - 1) for y in range(1, H - 1) if (x, y) not in owner]
        if fr: walls.append(rng.choice(fr)); owner[walls[-1]] = -1
    empty = [(x, y) for y in range(H) for x in range(W) if (x, y) not in owner]
    nfree = rng.randint(*c["free"])
    if len(empty) < nfree: return None
    keep = set(rng.sample(empty, nfree))
    crate_share = 0.35 if n <= 10 else 0.5
    max_pieces = c["target"][1] if c["use"] >= 0.95 else int(c["target"][1] / c["use"])  # every block must get moved
    if c.get("cap"): max_pieces = min(max_pieces, c["cap"])
    for cell in empty:
        if len(pieces) >= max_pieces: break  # few blocks: the rest of the board stays open
        if cell in owner or cell in keep: continue
        opts = [(sh, True) for sh in CRATES] + [(sh, False) for sh in ROADS]
        rng.shuffle(opts)
        want_crate = rng.random() < crate_share
        opts.sort(key=lambda o: (o[1] != want_crate, -len(parse(o[0]))))
        for sh, crate in opts:
            offs = parse(sh)
            first = min(offs, key=lambda o: (o[1], o[0]))
            ax, ay = cell[0] - first[0], cell[1] - first[1]
            cs = [(ax + o[0], ay + o[1]) for o in offs]
            if any(not inside(y, W, H) or y in owner or y in keep for y in cs): continue
            p = dict(at=f"{ax},{ay}", cells=sh)
            if not crate and n > 10 and rng.random() < 0.2: p["paths"] = " ".join("-" for _ in offs)  # road-shaped crate
            pieces.append(p)
            for y in cs: owner[y] = len(pieces) - 1
            break
    if c.get("cap"):  # hard levels: cells the few blocks don't fill become walls, so the board stays tight
        spare = [(x, y) for y in range(H) for x in range(W) if (x, y) not in owner and (x, y) not in keep]
        if len(spare) > 9: return None
        for cell in spare: walls.append(cell); owner[cell] = -1
    free_cells = [(x, y) for y in range(H) for x in range(W) if (x, y) not in owner]
    if len(pieces) < max_pieces and len(free_cells) > c["free"][1] + 2: return None  # too loose to be a real puzzle
    if len(route_pieces) > max_pieces: return None

    L = dict(name=f"Màn {n}", w=W, h=H, pieces=pieces, cats=cats, nests=nests)
    if walls: L["walls"] = " ".join(f"{x},{y}" for x, y in walls)

    # mechanics on road blocks; `focus` = blocks that show the level's new idea
    f = c["feats"]
    focus = list(rot_pieces)
    excl = [pi for pi in route_pieces if len(users[pi]) == 1 and not pieces[pi].get("rotate")]
    rng.shuffle(excl)
    pick = lambda pool, k=1: [pool.pop() for _ in range(min(k, len(pool)))]
    big = c["diff"] >= 2
    if "color" in f:
        for pi in pick(excl, 1 + big): pieces[pi]["color"] = route_color[next(iter(users[pi]))]; focus.append(pi)
    if "fixed" in f:
        for pi in pick(excl, 1 + big): pieces[pi]["fixed"] = True
    if "ice" in f:
        for pi in pick(excl, 1): pieces[pi]["ice"] = rng.randint(1, 2); focus.append(pi)
    if "keylock" in f:
        if len(routes) < 2: return None
        a, b = rng.sample(range(len(routes)), 2)
        ka = [pi for pi in excl if users[pi] == {a}]
        lb = [pi for pi in excl if users[pi] == {b}]
        if not ka or not lb: return None
        pieces[ka[0]]["key"] = True; pieces[lb[0]]["lock"] = True
        focus += [ka[0], lb[0]]
        excl = [pi for pi in excl if pi not in (ka[0], lb[0])]
    if "oneway" in f:  # straight or L road block (or a straight single cell): arrows toward the hole
        for pi in excl[:]:
            p = pieces[pi]
            k = next(iter(users[pi]))
            seq = [x for x, _, _ in routes[k]]
            cs = abs_cells(p)
            if p.get("paths") and (len(cs) > 1 or not straight(need[cs[0]])): continue
            last = max(cs, key=seq.index)
            p["oneway"] = NESW[routes[k][seq.index(last)][2]]
            excl.remove(pi); focus.append(pi)
            break
        else:
            return None
    if "bomb" in f:
        for pi in pick(excl, 1): pieces[pi]["bomb"] = 60 + 15 * len(cats); focus.append(pi)
    if "mystery" in f:  # hide a road block, and decoys/crates of the same size so you can't tell them apart
        mov = [pi for pi in route_pieces if not any(pieces[pi].get(k) for k in ("fixed", "lock", "rotate"))]
        if not mov: return None
        hid = rng.sample(mov, min(len(mov), 1 + big))
        sizes = {len(pieces[pi]["cells"].split()) for pi in hid}
        others = [i for i in range(len(route_pieces), len(pieces)) if len(pieces[i]["cells"].split()) in sizes]
        hid += rng.sample(others, min(len(others), 1 + big))
        for pi in hid: pieces[pi]["mystery"] = True
        focus += hid[:1]
    if "axis" in f:
        mov = [i for i, p in enumerate(pieces) if not p.get("fixed") and len(p["cells"].split()) > 1]
        for pi in rng.sample(mov, min(len(mov), 1 + big)):
            cs = parse(pieces[pi]["cells"])
            pieces[pi]["axis"] = "H" if max(x for x, _ in cs) >= max(y for _, y in cs) else "V"
            focus.append(pi)
    if "frozen" in f:
        single = [i for i, ct in enumerate(cats) if ct.get("count", 1) == 1]
        if len(all_cats(L)) < 2 or not single: return None
        cats[rng.choice(single)]["ice"] = rng.randint(1, min(2, len(all_cats(L)) - 1))

    if c["diff"]: L["difficulty"] = c["diff"]
    if c["tutorial"]: L["tutorial"] = True
    L["hint"], L["hintIcon"] = c["hint"], c["hint_icon"]
    if c["intro"]:
        L["introIcon"], L["introTitle"], L["introText"] = c["intro"]
        L["introFeat"] = INTRO[n][0]  # which animated demo the New Feature popup plays
    errs = validate(L)
    if errs: return None
    return pick_start(L, c, rng, focus if n in INTRO else [])


def pick_start(L, c, rng, focus):
    """Ask DeepSolver for a start whose fewest moves fall in the level's band; intro levels must use a focus block."""
    lo, hi = c["target"]
    with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False, encoding="utf-8") as fh:
        json.dump(L, fh, ensure_ascii=False)
        path = fh.name
    try:
        out = subprocess.run([SOLVER, path, str(lo), str(hi), str(rng.randrange(1 << 30)), str(c["use"]), "4000000" if c["diff"] else "1500000"],
                             capture_output=True, text=True, timeout=600).stdout
    except subprocess.TimeoutExpired:
        return None
    finally:
        os.remove(path)
    try:
        res = json.loads(out.strip().splitlines()[-1])
    except (ValueError, IndexError):
        return None
    c["last"] = res
    if not res.get("ok"): return None
    moves = [m.split(":") for m in res["solution"].split()]
    if focus and not any(int(i) in focus for i, _ in moves): return None
    if "rotate" in c["feats"] and c["intro"] and not any(t == "R" for _, t in moves): return None  # the intro needs a tap
    L["start"], L["solution"] = res["start"], res["solution"]
    if res.get("rot"): L["rot"] = res["rot"]
    mv, ncat = res["moves"], len(all_cats(L))
    L["time"] = min(300, max(60, int(round((40 + 7 * mv + 10 * ncat) / 10.0)) * 10))  # 5 minutes at most
    L["par"] = mv + max(2, -(-mv // 4))                                                 # moves for the moves star
    L["parTime"] = int(round(L["time"] * 0.6 / 10.0)) * 10                              # seconds for the time star
    return L


def main(out):
    demo = "--demo" in sys.argv  # the 10 demo levels (DEMO), saved as level_001..010
    only = [int(a) for a in sys.argv[2:] if a != "--demo"]
    for n in (only or (sorted(DEMO) if demo else range(1, LEVELS + 1))):
        rng = random.Random(5000 + n)
        stats = []
        for attempt in range(60000):
            c = config(n, rng)
            if c.get("cap"): c["cap"] += attempt // 15000  # hard levels: one more block allowed if it keeps failing
            c["use"] = max(c["use"] - 0.05 * (attempt // 2500), 0.85 if c["diff"] else 0.8)  # relax slowly if needed
            L = build(n, c, rng)
            if "last" in c: stats.append((c["last"].get("maxdist"), c["last"].get("best")))
            if L: break
        else:
            raise SystemExit(f"level {n}: no valid layout (solver tries: {stats[-10:]})")
        if n in DEMO: L["name"] = f"Màn {n - 100}"
        with open(f"{out}/level_{(n - 100 if n in DEMO else n):03}.json", "w", encoding="utf-8") as fh:
            json.dump(L, fh, ensure_ascii=False, indent=1)
        r = c["last"]
        print(n, c["tier"], f"{L['w']}x{L['h']}", "cats", len(all_cats(L)), "pieces", len(L["pieces"]), "moves", r["moves"],
              "used", f"{r['used']}/{r['movable']}", "time", L["time"], "comp", r["comp"], "secs", r["secs"],
              "feats", ",".join(sorted(c["feats"])), "attempt", attempt, flush=True)


if __name__ == "__main__":
    main(sys.argv[1])
