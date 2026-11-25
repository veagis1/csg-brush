using System.Linq;
using CsgBrush.Editor;
using NUnit.Framework;
using UnityEditor;

namespace CsgBrush.Tests
{
    public sealed class BrushLanguageTests
    {
        [Test]
        public void SwitchingLanguageRefreshesAllCreateLabelsWithoutChangingCommandsOrProjectSettings()
        {
            const string key = BrushLanguage.PreferenceKey;
            var previous = EditorPrefs.GetString(key, null);
            bool hadKey = EditorPrefs.HasKey(key);
            bool originalLanguage = BrushLanguage.English;
            var settings = EditorJsonUtility.ToJson(BrushSettings.instance);
            var commands = BrushCommandMenu.CreateCommands;
            try
            {
                foreach (bool english in new[] { false, true, false })
                {
                    BrushLanguage.SetEnglish(english);
                    Assert.That(commands.Select(c => c.Label), Is.EqualTo(english
                        ? new[] { "Box", "Wedge", "Cylinder", "Cone", "Sphere", "Linear Stairs", "Curved Stairs", "Spiral Stairs", "Arch", "Door", "Window", "Floor Plan", "Spline" }
                        : new[] { "Блок", "Клин", "Цилиндр", "Конус", "Сфера", "Прямая лестница", "Изогнутая лестница", "Винтовая лестница", "Арка", "Дверь", "Окно", "План стен", "Сплайн" }));
                    Assert.That(BrushCommandMenu.BuildCreateMenu().GetItemCount(), Is.EqualTo(13));
                    Assert.That(BrushSettings.FormatMeters(0.125f), Is.EqualTo(english ? "0.125 m" : "0,125 м"));
                    Assert.That(BrushPivotTool.ResetCommand.text, Is.EqualTo(english ? "Reset WCS" : "Сбросить WCS"));
                    Assert.That(EditorPrefs.GetString(key, "ru"), Is.EqualTo(english ? "en" : "ru"));
                    Assert.That(EditorJsonUtility.ToJson(BrushSettings.instance), Is.EqualTo(settings));
                }
            }
            finally
            {
                BrushLanguage.SetEnglish(originalLanguage);
                if (hadKey) EditorPrefs.SetString(key, previous);
                else EditorPrefs.DeleteKey(key);
            }
        }
    }
}
