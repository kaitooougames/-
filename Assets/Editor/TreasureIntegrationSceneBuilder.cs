using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class TreasureIntegrationSceneBuilder
{
    private const string ActionScenePath = "Assets/Scenes/SampleScene.unity";
    private const string TreasureScenePath = "Assets/TreasureGame/Scenes/TreasureScene.unity";
    private const string IntegratedScenePath = "Assets/Scenes/IntegratedGameScene.unity";

    private static readonly HashSet<string> EnvironmentRootNames = new HashSet<string>
    {
        "Main Camera",
        "Directional Light",
        "Global Volume",
        "Plane",
        "GameController"
    };

    [MenuItem("Tools/TreasureGame/Create Integration Scene")]
    public static void CreateIntegrationScene()
    {
        if (!File.Exists(ActionScenePath) || !File.Exists(TreasureScenePath))
        {
            Debug.LogError("行動カードシーン、またはお宝シーンが見つかりません。");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(IntegratedScenePath) != null)
            AssetDatabase.DeleteAsset(IntegratedScenePath);

        if (!AssetDatabase.CopyAsset(ActionScenePath, IntegratedScenePath))
        {
            Debug.LogError("統合作業用シーンを複製できませんでした。");
            return;
        }

        Scene integratedScene = EditorSceneManager.OpenScene(
            IntegratedScenePath, OpenSceneMode.Single);
        Scene treasureScene = EditorSceneManager.OpenScene(
            TreasureScenePath, OpenSceneMode.Additive);

        var movedRoots = new List<GameObject>();
        foreach (GameObject root in treasureScene.GetRootGameObjects())
        {
            if (EnvironmentRootNames.Contains(root.name))
                continue;

            SceneManager.MoveGameObjectToScene(root, integratedScene);
            movedRoots.Add(root);
        }

        foreach (GameObject root in movedRoots)
        {
            if (root.name == "TreasureController")
                root.name = "TreasureGame Controller";
            else if (root.name.StartsWith("Player"))
                root.name = "Treasure" + root.name;

            foreach (TreasureGame.TreasureTurnPrototype prototype in
                     root.GetComponentsInChildren<TreasureGame.TreasureTurnPrototype>(true))
                prototype.enabled = true;
        }

        EditorSceneManager.CloseScene(treasureScene, true);
        EditorSceneManager.MarkSceneDirty(integratedScene);
        EditorSceneManager.SaveScene(integratedScene, IntegratedScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(IntegratedScenePath);
        Debug.Log(
            $"<color=#70E8FF>【統合シーン作成】{IntegratedScenePath} を作成しました。" +
            $"お宝ルート {movedRoots.Count} 個を追加し、行動カード側の環境を維持しています。</color>");
    }
}
