using System;
using UnityEngine;

namespace CatPipe
{
    [Serializable]
    public class EdgeRef
    {
        public string side;   // L, R, T, B
        public int index;     // row for L/R, column for T/B
        public int color;     // 1-based palette index
        public int ice;       // cats only: frozen until this many other cats have reached their holes
        public string at;     // "x,y": on a board cell instead of the edge (cat sits there / hole in the board)
        public int count;     // cats only: a bed holding this many cats of the same color (0 = 1)
    }

    [Serializable]
    public class PieceJson
    {
        public string at;     // "x,y" anchor in the SOLVED layout
        public string cells;  // "dx,dy dx,dy ..." block shape (one of the 23 original shapes); the shape is the path
        public int color;     // 0 = neutral, otherwise only cats of this color may pass
        public int ice;       // drops needed before it can move / be passed
        public int bomb;      // seconds until it explodes, defused when a cat passes
        public bool @fixed, key, @lock, gate;
        public string axis;   // "H" or "V": the block only slides along that axis
        public bool mystery;  // path hidden until the block is first moved or a cat passes
        public string oneway; // "N", "E", "S" or "W": the way cats leave the block's path (straight or L); one-way only
        public string paths;  // special block: own path per cell, e.g. "EW" (horizontal only), "NES" (3-way junction), "-" (no path)
        public bool rotate;   // single cell: a tap turns its path a quarter clockwise (one move)
    }

    [Serializable]
    public class LevelJson
    {
        public string name;
        public int w, h, time, scramble;
        public string walls;  // "x,y x,y"
        public string hint;     // a few words shown in the bottom bar
        public string hintIcon; // sprite name (Resources/Sprites) shown next to the hint
        public bool tutorial;   // a hand shows the moves (solution) until the player deviates
        public string introIcon, introTitle, introText; // "New!" card shown when a level introduces a mechanic
        public string introFeat; // which mechanic the card's live demo shows (DemoStage script name)
        public int difficulty;                // 0 normal, 1 hard, 2 very hard, 3 super hard
        public bool consume;    // the blocks a cat walks over vanish once it is home (the file then holds the start layout)
        public string rot;      // "piece:quarter-turns ..." start rotation of rotating blocks
        public int par, parTime; // stars: win in at most `par` moves / within `parTime` seconds
        public string start;    // "x,y x,y ..." start anchor of every piece (generator-measured difficulty)
        public string solution; // "piece:x,y ..." fewest moves from start to win ("piece:R" = a tap that rotates it)
        public PieceJson[] pieces;
        public EdgeRef[] cats;
        public EdgeRef[] nests;
    }

    public static class Dir
    {
        // N, E, S, W in grid coordinates (y grows downward)
        public static readonly Vector2Int[] Step = { new Vector2Int(0, -1), new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(-1, 0) };
        public static int Opp(int d) => (d + 2) & 3;

        public static Vector2Int ParseXY(string s)
        {
            var p = s.Split(',');
            return new Vector2Int(int.Parse(p[0]), int.Parse(p[1]));
        }
    }
}
