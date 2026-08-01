using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>WebGLでもIMGUIの日本語を表示するための共通フォント設定。</summary>
public static class KaitouGuiFont
{
    private const string JapaneseFontName = "SourceHanSansJP-Regular";
    private const string JapaneseFontPath =
        "Assets/TextMesh Pro/Fonts/SourceHanSansJP-Regular.otf";
    private static Font japaneseFont;
    private static bool searched;

    public static void Apply()
    {
        if (japaneseFont == null && !searched)
        {
            searched = true;
            japaneseFont = Array.Find(Resources.FindObjectsOfTypeAll<Font>(),
                font => font != null && font.name == JapaneseFontName);

#if UNITY_EDITOR
            // EditorではOSフォント名へフォールバックせず、プロジェクト内の
            // 日本語フォントを直接読み込む。
            if (japaneseFont == null)
                japaneseFont = AssetDatabase.LoadAssetAtPath<Font>(JapaneseFontPath);
#elif !UNITY_WEBGL
            if (japaneseFont == null)
                japaneseFont = Font.CreateDynamicFontFromOSFont(
                    new[] { "Hiragino Kaku Gothic ProN", "Yu Gothic", "Noto Sans CJK JP" }, 18);
#endif
        }

        if (japaneseFont != null && GUI.skin.font != japaneseFont)
            GUI.skin.font = japaneseFont;
    }
}
