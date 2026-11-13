using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CatPipe.EditorTools
{
    // Imports everything in CatPipe/Resources/Sprites as sprites; pipe/popup/btn get 9-slice borders.
    public class CatPipeSpriteImporter : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.Contains("CatPipe/Resources/Sprites/")) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;

            var settings = new TextureImporterSettings();
            ti.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            ti.SetTextureSettings(settings);

            string file = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            if (file == "popup") ti.spriteBorder = new Vector4(120, 120, 120, 120);
            else if (file == "btn") ti.spriteBorder = new Vector4(60, 60, 60, 60);
            else if (file == "board_frame") ti.spriteBorder = new Vector4(20, 20, 20, 20);
            else if (file == "board_cell") ti.spriteBorder = new Vector4(30, 30, 30, 30);
            else if (file == "ui_btn_orange") ti.spriteBorder = new Vector4(85, 85, 85, 85);
            else if (file == "ui_level") ti.spriteBorder = new Vector4(70, 70, 70, 70);
            else if (file == "ui_timer") ti.spriteBorder = new Vector4(30, 30, 30, 30);
            else if (file == "ui_load_track") ti.spriteBorder = new Vector4(36, 36, 36, 36);
            else if (file == "ui_load_fill") ti.spriteBorder = new Vector4(31, 31, 31, 31);
            else if (file == "ui_btn_play") ti.spriteBorder = new Vector4(50, 50, 50, 50);
            if (assetPath.Contains("/Sprites/blocks/")) ti.spritePixelsPerUnit = 114; // one block cell = 1 unit
        }
    }

    public static class CatPipeSetup
    {
        const string ScenePath = "Assets/CatPipe/Scenes/CatPipe.unity";

        [MenuItem("CatPipe/Create Game Scene")]
        public static void CreateScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            new GameObject("CatPipe").AddComponent<GameController>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            Debug.Log("CatPipe: scene created at " + ScenePath);
        }
    }
}

namespace CatPipe.EditorTools
{
    public static class CatPipeBuild
    {
        [MenuItem("CatPipe/Build Windows")]
        public static void BuildWindows()
        {
            PlayerSettings.runInBackground = true; // autotest keeps running when the window loses focus
            var report = BuildPipeline.BuildPlayer(new[] { "Assets/CatPipe/Scenes/CatPipe.unity" }, "Build/CatPipe.exe", BuildTarget.StandaloneWindows64, BuildOptions.None);
            Debug.Log("CatPipe build: " + report.summary.result);
        }

        // Web demo: portrait canvas, no compression so plain static hosting (e.g. GitHub Pages) can serve it.
        [MenuItem("CatPipe/Build WebGL")]
        public static void BuildWebGL()
        {
            PlayerSettings.defaultWebScreenWidth = 540;
            PlayerSettings.defaultWebScreenHeight = 960;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.template = "PROJECT:CatPipe"; // Assets/WebGLTemplates/CatPipe: canvas fits the window at 9:16
            PlayerSettings.WebGL.nameFilesAsHashes = true; // new file names per build: browsers never mix a cached old build with a new one
            var report = BuildPipeline.BuildPlayer(new[] { "Assets/CatPipe/Scenes/CatPipe.unity" }, "Build/WebGL", BuildTarget.WebGL, BuildOptions.None);
            Debug.Log("CatPipe WebGL build: " + report.summary.result + " size=" + report.summary.totalSize);
        }
    }
}
