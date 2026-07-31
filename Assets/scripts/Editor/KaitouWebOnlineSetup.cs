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
    private const string MenuScene = "Assets/Scenes/MainMenu.unity";
    private const string GameScene = "Assets/Scenes/IntegratedGameScene.unity";
    private const string MacBuildDirectory = "Builds/KaitouOnlineMac";
    private const string MacBuildPath = MacBuildDirectory + "/KaitouTreasure.app";

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
}
#endif
