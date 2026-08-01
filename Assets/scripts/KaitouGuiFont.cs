using System;
using UnityEngine;

/// <summary>WebGLでもIMGUIの日本語を表示するための共通フォント設定。</summary>
public static class KaitouGuiFont
{
    private const string JapaneseFontName = "SourceHanSansJP-Regular";
    private static Font japaneseFont;
    private static bool searched;

    public static void Apply()
    {
        if (japaneseFont == null && !searched)
        {
            searched = true;
            japaneseFont = Array.Find(Resources.FindObjectsOfTypeAll<Font>(),
                font => font != null && font.name == JapaneseFontName);

#if !UNITY_WEBGL
            if (japaneseFont == null)
                japaneseFont = Font.CreateDynamicFontFromOSFont(
                    new[] { "Hiragino Sans", "Yu Gothic", "Noto Sans CJK JP" }, 18);
#endif
        }

        if (japaneseFont != null && GUI.skin.font != japaneseFont)
            GUI.skin.font = japaneseFont;
    }
}
