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
        /// <summary>Fill the whole viewport (recommended — lets WebGLOrientationAdapter rotate).</summary>
        Expand,
        /// <summary>Keep the design aspect ratio, letterbox the rest.</summary>
        Contain,
        /// <summary>Keep the design aspect ratio, crop to fill the viewport.</summary>
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
        public int aspectWidth = 0;   // 0 = use Player Settings Default Canvas Width
        public int aspectHeight = 0;  // 0 = use Player Settings Default Canvas Height
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
                            "Expand = fill viewport (recommended, lets the adapter rotate). " +
                            "Contain = keep aspect + letterbox. Cover = keep aspect + crop. " +
                            "Stretch = fill, ignore aspect."),
                        s.fitMode);

                    bool aspectUsed = s.fitMode == RSFitMode.Contain || s.fitMode == RSFitMode.Cover;
                    using (new EditorGUI.DisabledScope(!aspectUsed))
                    {
                        s.aspectWidth = Mathf.Max(0, EditorGUILayout.IntField(
                            new GUIContent("Aspect Width", "0 = Player Settings Default Canvas Width"),
                            s.aspectWidth));
                        s.aspectHeight = Mathf.Max(0, EditorGUILayout.IntField(
                            new GUIContent("Aspect Height", "0 = Player Settings Default Canvas Height"),
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
