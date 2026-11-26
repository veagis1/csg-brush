using System.Linq;
using System.Reflection;
using CsgBrush.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CsgBrush.Tests
{
    public sealed class BrushLanguageTests
    {
        [Test]
        public void BrushHasEnglishComponentAndContextMenuNames()
        {
            Assert.That(typeof(Brush).GetCustomAttribute<AddComponentMenu>().componentMenu, Is.EqualTo("Brush"));
            var entries = TypeCache.GetMethodsWithAttribute<MenuItem>()
                .Where(method => method.DeclaringType == typeof(BrushMenu))
                .SelectMany(method => method.GetCustomAttributes<MenuItem>())
                .Select(item => item.menuItem)
                .Where(path => path.StartsWith("CONTEXT/Brush/", System.StringComparison.Ordinal));
            CollectionAssert.AreEquivalent(new[] {
                "CONTEXT/Brush/To First", "CONTEXT/Brush/To Last", "CONTEXT/Brush/Move Up", "CONTEXT/Brush/Move Down"
            }, entries);
        }

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
                    Assert.That(BrushLanguage.Tr("Относительное смещение"), Is.EqualTo(english ? "Relative displacement" : "Относительное смещение"));
                    StringAssert.Contains(english ? "world axis" : "мировой оси", BrushLanguage.Tr(BrushSettings.RelativeDisplacementTooltip));
                    Assert.That(BrushLanguage.Tr("Переместить выделение"), Is.EqualTo(english ? "Move selection" : "Переместить выделение"));
                    Assert.That(BrushLanguage.Tr("Повернуть выделение"), Is.EqualTo(english ? "Rotate selection" : "Повернуть выделение"));
                    Assert.That(BrushLanguage.Tr("Масштабировать выделение"), Is.EqualTo(english ? "Scale selection" : "Масштабировать выделение"));
                    Assert.That(BrushPivotTool.ResetCommand.text, Is.EqualTo(english ? "Reset WCS" : "Сбросить WCS"));
                    Assert.That(EditorPrefs.GetString(key, "en"), Is.EqualTo(english ? "en" : "ru"));
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
