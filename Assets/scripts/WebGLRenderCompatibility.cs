using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>URP/WebGLで表示できない旧Standard素材を実行時にURP/Litへ変換する。</summary>
public static class WebGLRenderCompatibility
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ApplyAfterSceneLoad()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        ConvertLegacyMaterials();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
#endif
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ConvertLegacyMaterials();
    }

    private static void ConvertLegacyMaterials()
    {
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null)
        {
            Debug.LogError("【WebGL描画】URP/Litシェーダーが見つかりません。");
            return;
        }

        Renderer[] renderers = Object.FindObjectsByType<Renderer>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (Renderer renderer in renderers)
        {
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null || material.shader == null ||
                    material.shader.name != "Standard") continue;

                Texture texture = material.mainTexture;
                Color color = material.color;
                material.shader = urpLit;
                material.mainTexture = texture;
                material.color = color;
            }
        }
    }
}
