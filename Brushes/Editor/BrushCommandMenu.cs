using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace CsgBrush.Editor
{
    // Read the package's registered commands once. Both menus call the original tool methods.
    [InitializeOnLoad]
    internal static class BrushCommandMenu
    {
        internal const string CreateRoot = "CSG Brush/Создать/";
        internal sealed class Command
        {
            internal readonly string Path;
            internal int Priority;
            internal Action Execute;
            internal Func<bool> Validate;
            internal string Label => BrushLanguage.Tr(Path.Substring(Path.LastIndexOf('/') + 1));
            internal Command(string path) => Path = path;
        }

        static readonly Command[] Commands = ReadCommands();
        internal static readonly Command[] CreateCommands = Commands
            .Where(c => c.Path.StartsWith(CreateRoot, StringComparison.Ordinal)).ToArray();

        // Unity's native menu registration API is internal in Unity 6. Bind only this overload.
        static readonly Action<string, string, bool, int, Action, Func<bool>> Add =
            Bind<Action<string, string, bool, int, Action, Func<bool>>>("AddMenuItem");
        static readonly Action<string> Remove = Bind<Action<string>>("RemoveMenuItem");
        static bool nativeEnglish;

        static BrushCommandMenu()
        {
            EditorApplication.delayCall += Refresh;
            BrushLanguage.Changed += Refresh;
        }

        static T Bind<T>(string name) where T : Delegate
        {
            var parameters = typeof(T).GetMethod("Invoke").GetParameters().Select(p => p.ParameterType).ToArray();
            var method = typeof(Menu).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
                null, parameters, null);
            return (T)method?.CreateDelegate(typeof(T));
        }

        static Command[] ReadCommands()
        {
            var commands = new Dictionary<string, Command>(StringComparer.Ordinal);
            foreach (var method in TypeCache.GetMethodsWithAttribute<MenuItem>())
            {
                if (method.DeclaringType?.Assembly != typeof(BrushCommandMenu).Assembly || method.GetParameters().Length != 0) continue;
                foreach (var item in method.GetCustomAttributes<MenuItem>())
                {
                    if (!commands.TryGetValue(item.menuItem, out var command))
                        commands.Add(item.menuItem, command = new Command(item.menuItem));
                    if (item.validate) command.Validate = (Func<bool>)method.CreateDelegate(typeof(Func<bool>));
                    else
                    {
                        command.Execute = (Action)method.CreateDelegate(typeof(Action));
                        command.Priority = item.priority;
                    }
                }
            }
            return commands.Values.Where(c => c.Execute != null).OrderBy(c => c.Priority)
                .ThenBy(c => c.Path, StringComparer.Ordinal).ToArray();
        }

        static void Refresh()
        {
            if (nativeEnglish == BrushLanguage.English || Add == null || Remove == null) return;
            foreach (var command in Commands)
            {
                var english = string.Join("/", command.Path.Split('/').Select(part =>
                    BrushEnglish.Strings.TryGetValue(part, out var label) ? label : part));
                if (english == command.Path) continue;
                Remove(nativeEnglish ? english : command.Path);
                Add(nativeEnglish ? command.Path : english, "", false, command.Priority, command.Execute, command.Validate);
            }
            nativeEnglish = BrushLanguage.English;
        }

        internal static GenericMenu BuildCreateMenu()
        {
            var menu = new GenericMenu();
            foreach (var command in CreateCommands)
            {
                var label = new GUIContent(command.Label);
                if (command.Validate == null || command.Validate()) menu.AddItem(label, false, () => command.Execute());
                else menu.AddDisabledItem(label);
            }
            return menu;
        }
    }
}
