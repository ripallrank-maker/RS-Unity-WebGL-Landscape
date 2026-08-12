#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace RSWebGLLandscape.Editor
{
    /// <summary>How the WebGL canvas is fitted to the browser viewport.</summary>
    public enum RSFitMode
    {
        /// <summary>Recommended: keep the design ratio, letterbox, auto-flip by orientation so the adapter still sees portrait as portrait.</summary>
        Aspect,
        /// <summary>Fill the whole viewport (adapter handles everything; can crop/overflow on off-ratio screens).</summary>
        Expand,
        /// <summary>Keep a FIXED design aspect ratio (no flip), letterbox the rest.</summary>
        Contain,
        /// <summary>Keep a FIXED design aspect ratio (no flip), crop to fill the viewport.</summary>
        Cover,
        /// <summary>Stretch to fill, ignoring the aspect ratio.</summary>
        Stretch
    }

    /// <summary>
    /// Project-wide settings for the RSLandscape WebGL template. Stored under
    /// ProjectSettings/ (not the asset database) so it doesn't clutter Assets.
    /// The values are written into <c>TemplateData/rsconfig.js</c> of the build
    /// by <see cref="RSWebGLTemplateBuildProcessor"/>.
    /// </summary>
    public class RSWebGLTemplateSettings : ScriptableObject
    {
        internal const string SettingsPath = "ProjectSettings/RSWebGLTemplateSettings.asset";

        public RSFitMode fitMode = RSFitMode.Expand;
        public int aspectWidth = 0;   // 0 = template default (1920), independent of Player Settings
        public int aspectHeight = 0;  // 0 = template default (1080), independent of Player Settings
        public Color background = new Color(0x23 / 255f, 0x1F / 255f, 0x20 / 255f, 1f);
        public float maxDevicePixelRatio = 0f; // 0 = unlimited

        static RSWebGLTemplateSettings _instance;

        public static RSWebGLTemplateSettings GetOrCreate()
        {
            if (_instance != null) return _instance;

            var loaded = InternalEditorUtility.LoadSerializedFileAndForget(SettingsPath);
            if (loaded != null && loaded.Length > 0)
                _instance = loaded[0] as RSWebGLTemplateSettings;

            if (_instance == null)
            {
                _instance = CreateInstance<RSWebGLTemplateSettings>();
                _instance.Save();
            }
            return _instance;
        }

        public void Save()
        {
            InternalEditorUtility.SaveToSerializedFileAndForget(
                new Object[] { this }, SettingsPath, true);
        }
    }

    /// <summary>
    /// Adds "Project Settings &gt; RS WebGL Landscape" with a real enum dropdown
    /// for the fit mode and a color picker for the background — Unity's built-in
    /// WebGL template custom fields are text-only, so this replaces them.
    /// </summary>
    static class RSWebGLTemplateSettingsProvider
    {
        [SettingsProvider]
        public static SettingsProvider Create()
        {
            return new SettingsProvider("Project/RS WebGL Landscape", SettingsScope.Project)
            {
                label = "RS WebGL Landscape",
                guiHandler = _ =>
                {
                    var s = RSWebGLTemplateSettings.GetOrCreate();

                    EditorGUIUtility.labelWidth = 220f;
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("WebGL Template (RSLandscape)", EditorStyles.boldLabel);
                    EditorGUILayout.HelpBox(
                        "Written into TemplateData/rsconfig.js of the WebGL build when the " +
                        "RSLandscape template is selected. No effect on other templates.",
                        MessageType.Info);

                    EditorGUI.BeginChangeCheck();

                    s.fitMode = (RSFitMode)EditorGUILayout.EnumPopup(
                        new GUIContent("Fit Mode",
                            "Expand = fill viewport (default). " +
                            "Aspect = keep design ratio + letterbox, auto-flipped by orientation " +
                            "(portrait window stays portrait so the adapter rotates). " +
                            "Contain = fixed aspect + letterbox. " +
                            "Cover = fixed aspect + crop. Stretch = fill, ignore aspect."),
                        s.fitMode);

                    bool aspectUsed = s.fitMode == RSFitMode.Aspect
                        || s.fitMode == RSFitMode.Contain || s.fitMode == RSFitMode.Cover;
                    using (new EditorGUI.DisabledScope(!aspectUsed))
                    {
                        s.aspectWidth = Mathf.Max(0, EditorGUILayout.IntField(
                            new GUIContent("Aspect Width",
                                "The width of YOUR GAME's design resolution (e.g. 1920 for a " +
                                "1920x1080 game), not the player's screen. 0 = template default (1920)."),
                            s.aspectWidth));
                        s.aspectHeight = Mathf.Max(0, EditorGUILayout.IntField(
                            new GUIContent("Aspect Height",
                                "The height of YOUR GAME's design resolution (e.g. 1080 for a " +
                                "1920x1080 game), not the player's screen. 0 = template default (1080)."),
                            s.aspectHeight));
                    }

                    s.background = EditorGUILayout.ColorField(new GUIContent("Background"), s.background);

                    s.maxDevicePixelRatio = Mathf.Max(0f, EditorGUILayout.FloatField(
                        new GUIContent("Max Device Pixel Ratio", "0 = unlimited"),
                        s.maxDevicePixelRatio));

                    if (EditorGUI.EndChangeCheck())
                        s.Save();
                },
                keywords = new HashSet<string>(
                    new[] { "WebGL", "Template", "Landscape", "Fit", "Aspect", "Canvas", "RS", "Portrait" })
            };
        }
    }
}
#endif
