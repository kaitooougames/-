#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using KaitouOnline;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class KaitouWebOnlineSetup
{
    private const string JapaneseGuiFontPath =
        "Assets/TextMesh Pro/Fonts/SourceHanSansJP-Regular.otf";
    private const string MenuScene = "Assets/Scenes/MainMenu.unity";
    private const string GameScene = "Assets/Scenes/IntegratedGameScene.unity";
    private const string MacBuildDirectory = "Builds/KaitouOnlineMac";
    private const string MacBuildPath = MacBuildDirectory + "/KaitouTreasure.app";
    private const string WebGLBuildDirectory = "Builds/KaitouOnlineWebGL";

    [MenuItem("怪盗ゲーム/Web・オンライン/メニューとWebGL設定を作成")]
    public static void Setup()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = "MainMenu";
        GameObject cameraObject = new GameObject("Main Camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.055f, 0.02f, 0.025f);
        cameraObject.tag = "MainCamera";
        new GameObject("KaitouMainMenu").AddComponent<KaitouMainMenu>();
        Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, MenuScene);

        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>
        {
            new EditorBuildSettingsScene(MenuScene, true)
        };
        if (File.Exists(GameScene)) scenes.Add(new EditorBuildSettingsScene(GameScene, true));
        EditorBuildSettings.scenes = scenes.ToArray();

        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
        PlayerSettings.WebGL.decompressionFallback = true;
        PlayerSettings.runInBackground = true;
        PlayerSettings.productName = "怪盗ゲーム TREASURE";
        AssetDatabase.SaveAssets();
        Debug.Log("【Web・オンライン】MainMenuとWebGL設定を作成しました。");
    }

    [MenuItem("怪盗ゲーム/Web・オンライン/Mac接続テストアプリを作る")]
    public static void BuildMacConnectionTest()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("再生を停止してからMac接続テストアプリを作ってください。");
            return;
        }
        if (!File.Exists(MenuScene) || !File.Exists(GameScene))
        {
            Debug.LogError("先に「メニューとWebGL設定を作成」を実行してください。");
            return;
        }

        Directory.CreateDirectory(MacBuildDirectory);
        string[] scenes = { MenuScene, GameScene };
        BuildReport report = BuildPipeline.BuildPlayer(
            scenes, MacBuildPath, BuildTarget.StandaloneOSX, BuildOptions.Development);
        if (report.summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"【オンライン接続テスト】Macアプリを作成しました：{MacBuildPath}");
            EditorUtility.RevealInFinder(MacBuildPath);
        }
        else
            Debug.LogError($"Mac接続テストアプリの作成に失敗しました：{report.summary.result}");
    }

    [MenuItem("怪盗ゲーム/Web・オンライン/Web版を作る")]
    public static void BuildWebGL()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("再生を停止してからWeb版を作ってください。");
            return;
        }
        if (!BuildPipeline.IsBuildTargetSupported(
                BuildTargetGroup.WebGL, BuildTarget.WebGL))
        {
            Debug.LogError("WebGL Build SupportがUnityに入っていません。");
            EditorUtility.DisplayDialog(
                "WebGL Build Supportが必要です",
                "Unity Hub > Installs > 6000.0.26f1 > Add modules から、WebGL Build Supportを追加してください。追加後はUnityを再起動してください。",
                "OK");
            return;
        }
        if (!File.Exists(MenuScene) || !File.Exists(GameScene))
        {
            Debug.LogError("MainMenuまたはIntegratedGameSceneがありません。");
            return;
        }

        EditorSceneManager.SaveOpenScenes();
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(MenuScene, true),
            new EditorBuildSettingsScene(GameScene, true)
        };

        // Netlify Dropなどへフォルダをそのまま置ける設定。
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
        PlayerSettings.WebGL.decompressionFallback = false;
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.runInBackground = true;
        PlayerSettings.productName = "怪盗ゲーム TREASURE";
        EnsureJapaneseGuiFontIsPreloaded();

        if (Directory.Exists(WebGLBuildDirectory))
            Directory.Delete(WebGLBuildDirectory, true);
        Directory.CreateDirectory(WebGLBuildDirectory);

        BuildReport report;
        try
        {
            report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { MenuScene, GameScene },
                locationPathName = WebGLBuildDirectory,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            });
        }
        catch (UnityException exception)
        {
            Debug.LogError("Web版の作成に失敗しました：" + exception.Message);
            return;
        }

        if (report.summary.result == BuildResult.Succeeded)
        {
            MakeWebBuildResponsive();
            Debug.Log($"【Web版完成】{WebGLBuildDirectory}");
            EditorUtility.RevealInFinder(WebGLBuildDirectory);
        }
        else
            Debug.LogError("Web版の作成に失敗しました：" + report.summary.result);
    }

    private static void EnsureJapaneseGuiFontIsPreloaded()
    {
        Font font = AssetDatabase.LoadAssetAtPath<Font>(JapaneseGuiFontPath);
        if (font == null)
        {
            Debug.LogError("Web用日本語フォントが見つかりません：" + JapaneseGuiFontPath);
            return;
        }

        Object[] current = PlayerSettings.GetPreloadedAssets();
        if (System.Array.IndexOf(current, font) >= 0) return;

        List<Object> assets = new List<Object>(current) { font };
        PlayerSettings.SetPreloadedAssets(assets.ToArray());
        AssetDatabase.SaveAssets();
        Debug.Log("【Web版】日本語GUIフォントをビルドへ追加しました。");
    }

    private static void MakeWebBuildResponsive()
    {
        string indexPath = Path.Combine(WebGLBuildDirectory, "index.html");
        string cssPath = Path.Combine(WebGLBuildDirectory, "TemplateData/style.css");
        if (!File.Exists(indexPath) || !File.Exists(cssPath)) return;

        string html = File.ReadAllText(indexPath);
        html = html.Replace(
            "user-scalable=no, shrink-to-fit=yes",
            "user-scalable=no, shrink-to-fit=yes, viewport-fit=cover");
        File.WriteAllText(indexPath, html);

        const string responsiveCss = @"

html, body {
  width: 100%;
  height: 100%;
  overflow: hidden;
  background: #231F20;
  overscroll-behavior: none;
}
#unity-container.unity-mobile {
  position: fixed;
  left: env(safe-area-inset-left);
  top: env(safe-area-inset-top);
  width: calc(100% - env(safe-area-inset-left) - env(safe-area-inset-right));
  height: calc(100dvh - env(safe-area-inset-top) - env(safe-area-inset-bottom));
}
.unity-mobile #unity-canvas {
  display: block;
  width: 100%;
  height: 100%;
  touch-action: none;
}
";
        File.AppendAllText(cssPath, responsiveCss);
        Debug.Log("【Web版】スマホのSafe Areaと画面高へ対応しました。");
    }
}
#endif
