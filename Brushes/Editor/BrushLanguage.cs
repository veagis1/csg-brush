using System;
using System.Globalization;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

namespace CsgBrush.Editor
{
    [InitializeOnLoad]
    internal static class BrushLanguage
    {
        internal const string PreferenceKey = "digital.dream.csgbrush.editor.language";
        static readonly string[] Languages = { "Русский", "English" };
        static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");
        internal static bool English { get; private set; }
        internal static CultureInfo Culture => English ? CultureInfo.InvariantCulture : Russian;
        internal static event Action Changed;

        static BrushLanguage()
        {
            English = EditorPrefs.GetString(PreferenceKey, "en") != "ru";
        }

        internal static void SetEnglish(bool english)
        {
            if (English == english) return;
            EditorPrefs.SetString(PreferenceKey, english ? "en" : "ru");
            English = english;
            Changed?.Invoke();
            InternalEditorUtility.RepaintAllViews();
        }

        internal static string Tr(string source) => English && source != null &&
            BrushEnglish.Strings.TryGetValue(source, out var translated) ? translated : source;

        internal static string Format(string source, params object[] args) =>
            string.Format(Culture, Tr(source), args);

        // Retained toolbar elements can outlive a language change.
        internal static void Bind(VisualElement element, Action refresh)
        {
            element.RegisterCallback<AttachToPanelEvent>(_ => { Changed += refresh; refresh(); });
            element.RegisterCallback<DetachFromPanelEvent>(_ => Changed -= refresh);
            refresh();
        }

        internal static void DrawPreference()
        {
            var label = new GUIContent("Язык интерфейса / Language", English
                ? "CSG Brush interface language for this user on this computer."
                : "Язык интерфейса CSG Brush для этого пользователя на этом компьютере.");
            SetEnglish(EditorGUILayout.Popup(label, English ? 1 : 0, Languages) == 1);
        }
    }
}
