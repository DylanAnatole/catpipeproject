using System;
using System.Collections;
using Spine.Unity;
using UnityEngine;
using UnityEngine.UI;

namespace CatPipe
{
    // Loading and home screens over the original Catdom splash art.
    // Loading: a cat hops along the loading bar as it fills and jumps into its bed at the end.
    // Home: title, the current level and a big Play button (plus the level map).
    public class FrontScreens : MonoBehaviour
    {
        const float BarW = 760f, BarH = 70f, BarY = 300f, BarX = -70f;

        GameController gc;
        SkeletonDataAsset catData;
        Material uiMat;
        RectTransform loading, home, fill, playBtn, title;
        Text loadText, playLevelText;
        Image bed;

        public void Build(GameController gc, Transform canvas, Func<string, Sprite> load, SkeletonDataAsset catData, Material uiMat,
                          Action onPlay, Action onMap)
        {
            this.gc = gc;
            this.catData = catData;
            this.uiMat = uiMat;
            var root = (RectTransform)transform;
            root.SetParent(canvas, false);
            Stretch(root);

            // ---- home
            home = MakeScreen("Home", load);
            title = Title(home, new Vector2(0, -300));
            var tag = gc.MakeUIText(home, "Dẫn mèo về đúng hộp!", 52, TextAnchor.MiddleCenter, new Vector2(0.5f, 1), new Vector2(0, -580), new Vector2(900, 80));
            tag.GetComponent<Outline>().effectColor = new Color(0.2f, 0.1f, 0.3f, 0.9f);
            var play = gc.MakeImage(home, load("ui_btn_play"), new Vector2(0.5f, 0), new Vector2(0, 330), new Vector2(560, 230));
            play.type = Image.Type.Sliced;
            playBtn = play.rectTransform;
            play.gameObject.AddComponent<Button>().onClick.AddListener(() => { gc.Sfx("click"); onPlay(); });
            var playText = gc.MakeUIText(playBtn, "Chơi", 96, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0, 22), new Vector2(520, 120));
            playText.GetComponent<Outline>().effectColor = new Color(0.1f, 0.35f, 0f, 0.9f);
            playText.verticalOverflow = VerticalWrapMode.Overflow;
            playLevelText = gc.MakeUIText(playBtn, "", 44, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0, -52), new Vector2(520, 70));
            playLevelText.GetComponent<Outline>().effectColor = new Color(0.1f, 0.35f, 0f, 0.9f);
            var map = gc.MakeImage(home, load("ui_btn_orange"), new Vector2(0.5f, 0), new Vector2(0, 150), new Vector2(380, 120));
            map.type = Image.Type.Sliced;
            map.gameObject.AddComponent<Button>().onClick.AddListener(() => { gc.Sfx("click"); onMap(); });
            gc.MakeUIText(map.transform, "Bản đồ", 54, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0, 4), new Vector2(360, 100));
            home.gameObject.SetActive(false);

            // ---- loading
            loading = MakeScreen("Loading", load);
            Title(loading, new Vector2(0, -300));
            var track = gc.MakeImage(loading, load("ui_load_track"), new Vector2(0.5f, 0), new Vector2(BarX, BarY), new Vector2(BarW, BarH));
            track.type = Image.Type.Sliced;
            var f = gc.MakeImage(track.transform, load("ui_load_fill"), new Vector2(0, 0.5f), new Vector2(8, 0), new Vector2(60, BarH - 16));
            f.type = Image.Type.Sliced;
            fill = f.rectTransform;
            fill.pivot = new Vector2(0f, 0.5f); // grows to the right from the track's left end
            bed = gc.MakeImage(loading, gc.sprPad, new Vector2(0.5f, 0), new Vector2(BarX + BarW / 2f + 110, BarY - 5), new Vector2(260, 180));
            bed.preserveAspect = true;
            loadText = gc.MakeUIText(loading, "", 46, TextAnchor.MiddleCenter, new Vector2(0.5f, 0), new Vector2(0, BarY - 95), new Vector2(900, 70));
            loadText.GetComponent<Outline>().effectColor = new Color(0.2f, 0.1f, 0.3f, 0.9f);
        }

        // Full-screen panel with the splash art covering it (cropped to the cats in the middle).
        RectTransform MakeScreen(string name, Func<string, Sprite> load)
        {
            var rt = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(RectMask2D)).GetComponent<RectTransform>();
            rt.SetParent(transform, false);
            Stretch(rt);
            rt.GetComponent<Image>().color = new Color(0.25f, 0.55f, 0.95f);
            var art = load("ui_splash");
            var bg = gc.MakeImage(rt, art, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            bg.raycastTarget = false;
            var fit = bg.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fit.aspectRatio = art.rect.width / art.rect.height;
            var shade = gc.MakeImage(rt, gc.sprWhite, new Vector2(0.5f, 0), new Vector2(0, 260), new Vector2(1400, 520)); // darker band behind the buttons/bar
            shade.color = new Color(0.08f, 0.04f, 0.15f, 0.35f);
            shade.raycastTarget = false;
            return rt;
        }

        RectTransform Title(RectTransform parent, Vector2 pos)
        {
            var t = gc.MakeUIText(parent, "CatPipe", 190, TextAnchor.MiddleCenter, new Vector2(0.5f, 1), pos, new Vector2(1000, 240));
            t.color = new Color(1f, 0.82f, 0.2f);
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var o = t.GetComponent<Outline>();
            o.effectColor = new Color(0.45f, 0.2f, 0.55f);
            o.effectDistance = new Vector2(6, -6);
            var s = t.gameObject.AddComponent<Shadow>();
            s.effectColor = new Color(0.25f, 0.1f, 0.35f, 0.9f);
            s.effectDistance = new Vector2(0, -16);
            return t.rectTransform;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        // A Spine cat on the UI canvas (feet at its pivot).
        public SkeletonGraphic MakeCat(Transform parent, string skin, Vector2 pos, float scale)
        {
            var sg = SkeletonGraphic.NewSkeletonGraphicGameObject(catData, parent, uiMat);
            sg.name = "Cat";
            sg.raycastTarget = false;
            sg.Initialize(false);
            sg.Skeleton.SetSkin(skin);
            sg.Skeleton.SetSlotsToSetupPose();
            var rt = sg.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0);
            rt.anchoredPosition = pos;
            rt.localScale = Vector3.one * scale;
            return sg;
        }

        // The cat hops along the bar while it fills, then jumps into its bed; then the home screen.
        public IEnumerator RunLoading(int currentLevel)
        {
            loading.gameObject.SetActive(true);
            home.gameObject.SetActive(false);
            var cat = MakeCat(loading, "vang", new Vector2(BarX - BarW / 2f, BarY + 26), 2.2f);
            cat.AnimationState.SetAnimation(0, "jump3", true);
            float x0 = BarX - BarW / 2f + 30, x1 = BarX + BarW / 2f - 30;
            const float dur = 2.6f;
            for (float t = 0; t < dur; t += Time.deltaTime)
            {
                float p = Mathf.SmoothStep(0f, 1f, t / dur);
                p = Mathf.Min(1f, p + 0.05f * Mathf.Sin(t * 9f) * (1f - p)); // uneven, like real loading
                fill.sizeDelta = new Vector2(Mathf.Lerp(60f, BarW - 16f, p), fill.sizeDelta.y);
                cat.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(x0, x1, p), BarY + 26);
                loadText.text = $"Đang tải... {Mathf.RoundToInt(p * 100)}%";
                yield return null;
            }
            fill.sizeDelta = new Vector2(BarW - 16f, fill.sizeDelta.y);
            loadText.text = "Mèo đã về tổ!";
            // final leap into the bed
            var from = cat.rectTransform.anchoredPosition;
            var to = bed.rectTransform.anchoredPosition + new Vector2(0, -25); // sits on the middle of its bed
            cat.AnimationState.SetAnimation(0, "jump3", false).TrackTime = 0.45f;
            for (float k = 0; k < 1f; k += Time.deltaTime / 0.4f)
            {
                cat.rectTransform.anchoredPosition = Vector2.Lerp(from, to, k) + new Vector2(0, 4f * 110f * k * (1f - k));
                yield return null;
            }
            cat.rectTransform.anchoredPosition = to;
            cat.AnimationState.SetAnimation(0, "select_to_sit", false);
            cat.AnimationState.AddAnimation(0, "front_idle1", true, 0);
            gc.Sfx("meow" + UnityEngine.Random.Range(1, 8), 0.8f);
            yield return new WaitForSeconds(0.9f);
            ShowHome(currentLevel);
        }

        public void ShowHome(int currentLevel)
        {
            loading.gameObject.SetActive(false);
            home.gameObject.SetActive(true);
            playLevelText.text = "Màn " + currentLevel;
            gameObject.SetActive(true);
        }

        public void Hide() => gameObject.SetActive(false);

        public bool HomeVisible => home && home.gameObject.activeSelf;

        void Update()
        {
            if (home && home.gameObject.activeInHierarchy)
            {
                playBtn.localScale = Vector3.one * (1f + 0.04f * Mathf.Sin(Time.time * 4f)); // Play button breathes
                title.localRotation = Quaternion.Euler(0, 0, 2f * Mathf.Sin(Time.time * 1.6f));
            }
        }
    }
}
