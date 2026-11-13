using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CatPipe
{
    // Level map (like Catdom's journey): a vertical scroll, level 1 at the bottom, 5 episodes of 20 levels,
    // each with its own original background. Played levels are wooden with a star, the current one is golden
    // with the cat on it, locked ones are grey with a padlock. Levels that introduce a mechanic show its icon.
    public class MapScreen : MonoBehaviour
    {
        const float Step = 195f, Bottom = 560f, Top = 520f, Width = 1080f;
        const int PerEpisode = 20;

        class Node { public RectTransform rt; public Image img; public Text num; public Image star, lockImg; }

        GameController gc;
        Func<string, Sprite> load;
        Action<int> onPlay;
        RectTransform content, viewport, avatar;
        ScrollRect scroll;
        Text playText;
        readonly List<Node> nodes = new List<Node>();
        Sprite sprNode, sprCur;
        int current;

        public void Build(GameController gc, Transform canvas, Func<string, Sprite> load, LevelJson[] levels, Action<int> onPlay)
        {
            this.gc = gc;
            this.load = load;
            this.onPlay = onPlay;
            sprNode = load("map_node");
            sprCur = load("map_node_cur");

            var root = (RectTransform)transform;
            root.SetParent(canvas, false);
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one; root.offsetMin = root.offsetMax = Vector2.zero;
            gameObject.AddComponent<Image>().color = new Color(0.23f, 0.14f, 0.08f);
            gameObject.AddComponent<RectMask2D>();
            viewport = root;

            float height = Bottom + levels.Length * Step + Top;
            content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(root, false);
            content.anchorMin = content.anchorMax = new Vector2(0.5f, 0);
            content.pivot = new Vector2(0.5f, 0);
            content.sizeDelta = new Vector2(Width, height);

            scroll = gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = root;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;

            // one original background per episode, scaled to cover its stretch of the road
            int episodes = Mathf.CeilToInt(levels.Length / (float)PerEpisode);
            for (int e = 0; e < episodes; e++)
            {
                var sp = load("map_bg" + (e % 5 + 1));
                float y0 = e == 0 ? 0 : Bottom + e * PerEpisode * Step - Step / 2f;
                float y1 = e == episodes - 1 ? height : Bottom + (e + 1) * PerEpisode * Step - Step / 2f;
                float h = y1 - y0, w = Mathf.Max(Width, h * sp.rect.width / sp.rect.height);
                var bg = Img(sp, new Vector2(0, y0 + h / 2f), new Vector2(w, h));
                bg.raycastTarget = false;
                bg.color = new Color(0.85f, 0.85f, 0.85f); // a little darker so the road and buttons stand out
            }

            // road dots between levels
            for (int i = 0; i + 1 < levels.Length; i++)
                for (int k = 1; k <= 3; k++)
                {
                    var d = Img(load("dot"), Vector2.Lerp(NodePos(i), NodePos(i + 1), k / 4f), new Vector2(38, 38));
                    d.color = new Color(1f, 0.97f, 0.9f, 0.95f);
                    d.raycastTarget = false;
                }

            for (int i = 0; i < levels.Length; i++)
            {
                int idx = i;
                var n = new Node();
                n.img = Img(sprNode, NodePos(i), new Vector2(150, 150));
                n.img.preserveAspect = true;
                n.rt = n.img.rectTransform;
                n.img.gameObject.AddComponent<Button>().onClick.AddListener(() => Tap(idx));
                n.num = gc.MakeUIText(n.rt, (i + 1).ToString(), 56, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0, 2), new Vector2(150, 90));
                n.star = Sub(n.rt, load("map_star"), new Vector2(0, 70), new Vector2(60, 58));
                n.lockImg = Sub(n.rt, load("lock"), new Vector2(52, -50), new Vector2(46, 60)); // small badge: the number stays readable
                var L = levels[i];
                if (!string.IsNullOrEmpty(L.introIcon)) // a level that teaches something new: preview its icon
                {
                    float side = NodePos(i).x > 0 ? -1f : 1f;
                    var bubble = Img(load("map_bubble"), NodePos(i) + new Vector2(side * 150, 40), new Vector2(150, 64));
                    bubble.raycastTarget = false;
                    if (side < 0) bubble.rectTransform.localScale = new Vector3(-1, 1, 1);
                    var icon = Img(load(L.introIcon), NodePos(i) + new Vector2(side * 150, 46), new Vector2(54, 54));
                    icon.preserveAspect = true;
                    icon.raycastTarget = false;
                }
                if (L.difficulty > 0) // hard / super hard tag under the button
                {
                    var hard = Sub(n.rt, load("map_hard"), new Vector2(0, -88), new Vector2(L.difficulty > 1 ? 190 : 150, 52));
                    if (L.difficulty == 2) hard.color = new Color(1f, 0.6f, 0.3f);
                    if (L.difficulty >= 3) hard.color = new Color(0.75f, 0.45f, 1f);
                    gc.MakeUIText(hard.transform, L.difficulty >= 3 ? "Siêu khó" : L.difficulty == 2 ? "Cực khó" : "Khó", 32, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0, 2), new Vector2(190, 50));
                }
                nodes.Add(n);
            }

            avatar = Img(load("map_ava"), Vector2.zero, new Vector2(120, 120)).rectTransform;
            avatar.GetComponent<Image>().raycastTarget = false;

            var play = gc.MakeImage(root, load("map_play"), new Vector2(0.5f, 0), new Vector2(0, 150), new Vector2(520, 200));
            play.preserveAspect = true;
            play.gameObject.AddComponent<Button>().onClick.AddListener(() => onPlay(current));
            playText = gc.MakeUIText(play.transform, "", 60, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0, 8), new Vector2(480, 120));
        }

        // Winding road: level i at a fixed height, swinging left and right.
        static Vector2 NodePos(int i) => new Vector2(250f * Mathf.Sin(i * 0.7f), Bottom + i * Step);

        Image Img(Sprite sp, Vector2 pos, Vector2 size)
        {
            var img = gc.MakeImage(content, sp, new Vector2(0.5f, 0), pos, size);
            return img;
        }

        Image Sub(RectTransform parent, Sprite sp, Vector2 pos, Vector2 size)
        {
            var img = gc.MakeImage(parent, sp, new Vector2(0.5f, 0.5f), pos, size);
            img.preserveAspect = true;
            img.raycastTarget = false;
            return img;
        }

        void Tap(int i)
        {
            onPlay(i); // all levels are open (internal demo)
        }

        // Show the map with `unlocked` levels open (current = unlocked - 1) and scroll to the current level.
        public void Show(int unlocked)
        {
            current = Mathf.Clamp(unlocked - 1, 0, nodes.Count - 1);
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                bool done = i < current, cur = i == current, locked = false; // all levels are open (internal demo)
                n.img.sprite = cur ? sprCur : sprNode;
                n.rt.sizeDelta = cur ? new Vector2(180, 180) : new Vector2(150, 150);
                n.img.color = locked ? new Color(0.62f, 0.6f, 0.58f) : Color.white;
                n.num.text = (i + 1).ToString();
                n.num.color = cur ? new Color(0.55f, 0.3f, 0.05f) : locked ? new Color(1f, 1f, 1f, 0.55f) : Color.white;
                n.star.enabled = done;
                n.lockImg.enabled = locked;
            }
            playText.text = "Chơi màn " + (current + 1);
            gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            float viewH = viewport.rect.height, contentH = content.rect.height;
            scroll.verticalNormalizedPosition = Mathf.Clamp01((NodePos(current).y - viewH * 0.45f) / Mathf.Max(1f, contentH - viewH));
        }

        void Update()
        {
            if (nodes.Count == 0) return;
            var cur = nodes[current].rt;
            cur.localScale = Vector3.one * (1f + 0.06f * Mathf.Sin(Time.time * 4f));
            avatar.anchoredPosition = cur.anchoredPosition + new Vector2(0, 120 + Mathf.Abs(Mathf.Sin(Time.time * 3f)) * 18f);
        }
    }
}
