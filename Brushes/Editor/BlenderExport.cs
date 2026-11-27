using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CsgBrush.Editor
{
    /// <summary>
    /// Saves a Brush Group's meshes as a Blender file, using the Blender installed on this computer: the meshes are
    /// written in Blender's own coordinates (Unity's x, y, z become Blender's x, z, y: right-handed, Z up, metres, each
    /// triangle's corners reversed since that swap mirrors them) and a
    /// short script run by Blender in the background builds them with Blender's own API and saves the file. Nothing is
    /// left for an importer to interpret, and no package is needed.
    /// </summary>
    public static class BlenderExport
    {
        /// <summary>The oldest Blender the export script runs on.</summary>
        public static readonly Version MinimumVersion = new Version(3, 6);
        /// <summary>Seconds Blender may take to save a file before it is stopped.</summary>
        public static int TimeoutSeconds = 300;
        const string OkMarker = "CSGBRUSH_EXPORT_OK";

        // ------------------------------------------------------------------ finding Blender

        static string s_CheckedPath;
        static Version s_CheckedVersion;

        internal static string Text(string russian, string english) => BrushLanguage.English ? english : russian;

        internal static string ChooseExecutable() => EditorUtility.OpenFilePanel(
            Text("Указать программу Blender", "Choose the Blender executable"), "",
            Application.platform == RuntimePlatform.WindowsEditor ? "exe" : "");

        /// <summary>
        /// The Blender to use: the path in Project Settings > Brushes, else one found where it is usually installed. It counts
        /// only when it runs and reports a version of at least <see cref="MinimumVersion"/>. Null with <paramref name="error"/>
        /// saying why otherwise. The answer is kept until the path changes.
        /// </summary>
        public static string FindBlender(out Version version, out string error)
        {
            string set = BrushSettings.instance.blenderPath?.Trim();
            bool configured = !string.IsNullOrEmpty(set);
            var candidates = configured ? new[] { Executable(set) } : UsualPlaces();
            string firstError = null;
            foreach (var path in candidates)
            {
                if (string.IsNullOrEmpty(path)) continue;
                if (path == s_CheckedPath && File.Exists(path)) { version = s_CheckedVersion; error = null; return path; }
                var found = Check(path, out version, out error);
                if (found)
                {
                    s_CheckedPath = path; s_CheckedVersion = version;
                    return path;
                }
                if (File.Exists(path) || configured)
                    firstError ??= error;
            }
            version = null;
            error = firstError ?? Text(
                "Blender не найден. Установите его с blender.org или укажите путь в настройках проекта CSG Brush.",
                "Blender was not found. Install it from blender.org or choose its path in the CSG Brush project settings.");
            return null;
        }

        /// <summary>Forget the Blender found, so the next export looks again (after an install, or a new path).</summary>
        public static void Forget() { s_CheckedPath = null; s_CheckedVersion = null; }

        /// <summary>The program inside a path given as an application bundle on macOS (Blender.app).</summary>
        static string Executable(string path)
        {
            if (path.EndsWith(".app", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".app/", StringComparison.OrdinalIgnoreCase))
                return Path.Combine(path.TrimEnd('/'), "Contents", "MacOS", "Blender");
            return path;
        }

        /// <summary>Where Blender is installed by its installers, the stores and Steam, per platform.</summary>
        static IEnumerable<string> UsualPlaces()
        {
            string executable = Application.platform == RuntimePlatform.WindowsEditor ? "blender.exe" : "blender";
            foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
                if (!string.IsNullOrWhiteSpace(folder))
                    yield return Path.Combine(folder.Trim().Trim('"'), executable);
            switch (Application.platform)
            {
                case RuntimePlatform.OSXEditor:
                {
                    // Spotlight knows the app by its bundle id, wherever it is and whatever it is called
                    foreach (var app in Run("/usr/bin/mdfind", new[] { "kMDItemCFBundleIdentifier == 'org.blenderfoundation.blender'" }, 5, out _).Split('\n'))
                        if (app.Trim().Length > 0) yield return Executable(app.Trim());
                    yield return "/Applications/Blender.app/Contents/MacOS/Blender";
                    yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Applications/Blender.app/Contents/MacOS/Blender");
                    yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library/Application Support/Steam/steamapps/common/Blender/Blender.app/Contents/MacOS/Blender");
                    break;
                }
                case RuntimePlatform.WindowsEditor:
                {
                    // the program that opens .blend files: set by the installer and the Microsoft Store
                    var command = Run("reg", new[] { "query", @"HKCR\blendfile\shell\open\command", "/ve" }, 5, out _);
                    int q = command.IndexOf('"');
                    if (q >= 0) { int e = command.IndexOf('"', q + 1); if (e > q) yield return command.Substring(q + 1, e - q - 1).Replace("blender-launcher.exe", "blender.exe"); }
                    foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
                    {
                        var foundation = Path.Combine(root, "Blender Foundation");
                        if (!Directory.Exists(foundation)) continue;
                        var dirs = Directory.GetDirectories(foundation);
                        Array.Sort(dirs); Array.Reverse(dirs); // newest first
                        foreach (var d in dirs) yield return Path.Combine(d, "blender.exe");
                    }
                    yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Steam\steamapps\common\Blender\blender.exe");
                    break;
                }
                default:
                {
                    yield return "/usr/bin/blender";
                    yield return "/snap/bin/blender";
                    yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), ".steam/steam/steamapps/common/Blender/blender");
                    break;
                }
            }
        }

        /// <summary>Does the program run as Blender, and which version is it.</summary>
        static bool Check(string path, out Version version, out string error)
        {
            version = null;
            if (!File.Exists(path)) { error = Text($"Blender не найден по пути {path}.", $"Blender was not found at {path}."); return false; }
            var output = Run(path, new[] { "--factory-startup", "--version" }, 30, out bool ran);
            if (!ran) { error = Text($"Blender по пути {path} не запустился.", $"Blender at {path} did not start."); return false; }
            var match = System.Text.RegularExpressions.Regex.Match(output, @"(?m)^Blender\s+(\d+\.\d+(?:\.\d+)?)");
            if (!match.Success || !Version.TryParse(match.Groups[1].Value, out version))
            { error = Text($"Программа {path} не сообщила версию Blender.", $"{path} did not report a Blender version."); return false; }
            if (version < MinimumVersion)
            { error = Text($"Установлен Blender {version}. Требуется Blender {MinimumVersion} или новее.", $"Found Blender {version}. Blender {MinimumVersion} or newer is required."); return false; }
            error = null;
            return true;
        }

        /// <summary>Run a program and return what it printed (output and errors); <paramref name="ran"/> is false when it didn't start or finish in time.</summary>
        internal static string Run(string file, string[] args, int timeoutSeconds, out bool ran, string userResources = null)
        {
            ran = false;
            try
            {
                var info = new ProcessStartInfo(file, string.Join(" ", Array.ConvertAll(args, Quote))) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                if (userResources != null) info.EnvironmentVariables["BLENDER_USER_RESOURCES"] = userResources;
                using var p = Process.Start(info);
                if (p == null) return "";
                var stdout = p.StandardOutput.ReadToEndAsync(); var stderr = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(timeoutSeconds * 1000))
                {
                    try { p.Kill(); }
                    catch (InvalidOperationException) { }
                    p.WaitForExit(5000);
                    return "";
                }
                ran = p.ExitCode == 0;
                return stdout.Result + "\n" + stderr.Result;
            }
            catch (System.ComponentModel.Win32Exception) { return ""; }
            catch (InvalidOperationException) { return ""; }
        }

        /// <summary>An argument quoted for the command line (spaces in paths; quotes and backslashes before them escaped).</summary>
        static string Quote(string a)
        {
            if (a.Length > 0 && a.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return a;
            var sb = new System.Text.StringBuilder("\"");
            int slashes = 0;
            foreach (char c in a)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { sb.Append('\\', slashes * 2 + 1); sb.Append('"'); }
                else { sb.Append('\\', slashes); sb.Append(c); }
                slashes = 0;
            }
            sb.Append('\\', slashes * 2).Append('"');
            return sb.ToString();
        }

        // ------------------------------------------------------------------ saving

        [Serializable] class Data { public List<Obj> objects = new List<Obj>(); public List<Mat> materials = new List<Mat>(); public bool pack; }
        [Serializable] class Obj { public string name; public float[] vertices, normals, uv0, uv1; public int[] triangles, faceMaterial, materials; }
        /// <summary>
        /// A material as Blender's Principled BSDF takes it: colours linear (Unity stores them as sRGB), textures as files,
        /// the base map's tiling and offset, smoothness as Unity has it (Blender's roughness is one minus it).
        /// </summary>
        [Serializable]
        class Mat
        {
            public string name, baseMap = "", metallicMap = "", normalMap = "", emissionMap = "";
            public float[] color, emission, tiling = { 1f, 1f }, offset = { 0f, 0f };
            public float metallic, smoothness = 0.5f, normalScale = 1f, cutoff = 0.5f;
            /// <summary>Where smoothness is read in a texture: "metallic" (the metallic map's alpha), "albedo" (the base map's alpha), or "" (the value).</summary>
            public string smoothnessFrom = "";
            /// <summary>"opaque", "clip" or "blend".</summary>
            public string alpha = "opaque";
        }

        /// <summary>
        /// Save the group's meshes (built first if they are out of date) to a Blender file at <paramref name="path"/>,
        /// replacing it. In the group's own space: its origin is Blender's origin. Null when saved, else what went wrong.
        /// </summary>
        public static string Save(BrushGroup group, string path)
        {
            if (group == null)
                return Text("Выберите группу брашей.", "Select a Brush Group.");
            var blender = FindBlender(out _, out var error);
            if (blender == null) return error;
            BrushApi.ForceUpdate();
            var data = Collect(group);
            data.pack = BrushSettings.instance.blenderPackTextures;
            if (data.objects.Count == 0)
                return Text("В группе брашей нет мешей для экспорта.", "The Brush Group has no meshes to export.");
            string name = "csgbrush-blender-" + Guid.NewGuid().ToString("N");
            string temp = Path.Combine(Path.GetTempPath(), name);
            string pending = null;
            try
            {
                if (!string.Equals(Path.GetExtension(path), ".blend", StringComparison.OrdinalIgnoreCase))
                    return Text("Укажите имя файла с расширением .blend.", "Choose a file with the .blend extension.");
                path = Path.GetFullPath(path);
                string folder = Path.GetDirectoryName(path);
                if (!Directory.Exists(folder))
                    return Text($"Папка {folder} не существует.", $"The folder {folder} does not exist.");
                // A sibling staging file keeps the existing export intact if Blender fails or is stopped.
                pending = Path.Combine(folder, "." + name + ".blend");
                Directory.CreateDirectory(temp);
                string dataPath = Path.Combine(temp, "group.json"), scriptPath = Path.Combine(temp, "build.py");
                File.WriteAllText(dataPath, JsonUtility.ToJson(data));
                File.WriteAllText(scriptPath, Script);
                var output = Run(blender, new[] { "--background", "--factory-startup", "--disable-autoexec", "--python-exit-code", "1",
                    "--python", scriptPath, "--", dataPath, pending }, TimeoutSeconds, out bool ran, temp);
                if (ran && output.Contains(OkMarker) && File.Exists(pending))
                {
                    if (File.Exists(path)) File.Replace(pending, path, null);
                    else File.Move(pending, path);
                    if (output.Contains("CSGBRUSH_TEXTURE_WARNING"))
                        Debug.LogWarning(Text("CSG Brush: Blender пропустил часть текстур.\n", "CSG Brush: Blender skipped some textures.\n") + output);
                    return null;
                }
                if (!ran && output.Length == 0)
                    return Text($"Blender не запустился или не закончил запись за {TimeoutSeconds} с.", $"Blender did not start or finish saving within {TimeoutSeconds} s.");
                Debug.LogWarning(Text($"Вывод Blender при сохранении {path}.\n", $"Blender output while saving {path}.\n") + output);
                return Text("Blender не сохранил файл. Подробности в Console.", "Blender could not save the file. See the Console for details.");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            {
                return Text($"Не удалось сохранить файл {path}.\n{ex.Message}", $"Could not save {path}.\n{ex.Message}");
            }
            finally
            {
                try
                {
                    if (pending != null) File.Delete(pending);
                    if (Directory.Exists(temp)) Directory.Delete(temp, true);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    Debug.LogWarning(Text("Не удалось удалить временные файлы экспорта Blender.\n", "Could not remove the temporary Blender export files.\n") + ex.Message);
                }
            }
        }

        /// <summary>The group's render meshes in Blender's coordinates, relative to the group: positions, normals, both UV sets, triangles and materials.</summary>
        static Data Collect(BrushGroup group)
        {
            var data = new Data();
            var materialIndex = new Dictionary<Material, int>();
            var toGroup = group.transform.worldToLocalMatrix;
            foreach (var t in BrushCsg.MeshObjects(group))
            {
                if (!t.TryGetComponent<MeshFilter>(out var mf) || mf.sharedMesh == null || mf.sharedMesh.vertexCount == 0) continue;
                var mesh = mf.sharedMesh; var mats = t.TryGetComponent<MeshRenderer>(out var mr) ? mr.sharedMaterials : new Material[0];
                var m = toGroup * t.localToWorldMatrix;
                var normalMatrix = m.inverse.transpose;
                bool reverse = m.determinant >= 0f;
                var v = mesh.vertices; var n = mesh.normals;
                var obj = new Obj { name = group.name + (t.name == BrushGroup.MeshChildName ? "" : " " + t.name.Trim('<', '[', ']', '>')), vertices = new float[v.Length * 3], normals = new float[v.Length * 3] };
                for (int i = 0; i < v.Length; i++)
                {
                    var p = m.MultiplyPoint3x4(v[i]); var q = n.Length == v.Length ? normalMatrix.MultiplyVector(n[i]).normalized : Vector3.zero;
                    obj.vertices[i * 3] = p.x; obj.vertices[i * 3 + 1] = p.z; obj.vertices[i * 3 + 2] = p.y; // Unity (x, y, z) is Blender (x, z, y)
                    obj.normals[i * 3] = q.x; obj.normals[i * 3 + 1] = q.z; obj.normals[i * 3 + 2] = q.y;
                }
                obj.uv0 = Flatten(mesh, 0); obj.uv1 = Flatten(mesh, 1);
                var tris = new List<int>(); var faceMaterial = new List<int>(); var slots = new List<int>();
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    var mat = s < mats.Length ? mats[s] : null;
                    int mi = -1;
                    if (mat != null && !materialIndex.TryGetValue(mat, out mi)) { mi = data.materials.Count; materialIndex[mat] = mi; data.materials.Add(Describe(mat)); }
                    int slot = slots.IndexOf(mi); if (slot < 0) { slot = slots.Count; slots.Add(mi); }
                    var st = mesh.GetTriangles(s);
                    // The axis swap changes winding. A mirrored transform between child and group changes it again.
                    for (int k = 0; k + 2 < st.Length; k += 3)
                    {
                        tris.Add(st[k]);
                        tris.Add(st[k + (reverse ? 2 : 1)]);
                        tris.Add(st[k + (reverse ? 1 : 2)]);
                    }
                    for (int k = 0; k < st.Length / 3; k++) faceMaterial.Add(slot);
                }
                obj.triangles = tris.ToArray(); obj.faceMaterial = faceMaterial.ToArray(); obj.materials = slots.ToArray();
                if (obj.triangles.Length > 0) data.objects.Add(obj);
            }
            return data;
        }

        static float[] Flatten(Mesh mesh, int channel)
        {
            if (!mesh.HasVertexAttribute(channel == 0 ? UnityEngine.Rendering.VertexAttribute.TexCoord0 : UnityEngine.Rendering.VertexAttribute.TexCoord1)) return new float[0];
            var uv = new List<Vector2>(); mesh.GetUVs(channel, uv);
            var f = new float[uv.Count * 2];
            for (int i = 0; i < uv.Count; i++) { f[i * 2] = uv[i].x; f[i * 2 + 1] = uv[i].y; }
            return f;
        }

        /// <summary>
        /// The material's surface, read by the property names of Unity's standard shaders (URP Lit and the built-in Standard;
        /// other shaders that use the same names too): base colour and map with its tiling, metallic and smoothness, normal
        /// map, emission, and how it uses alpha. A shader without them gives its name and main texture only.
        /// </summary>
        static Mat Describe(Material material)
        {
            bool urp = material.HasProperty("_BaseColor");
            var c = (urp ? material.GetColor("_BaseColor") : material.HasProperty("_Color") ? material.color : Color.white);
            var m = new Mat { name = material.name };
            var linear = c.linear;
            m.color = new[] { linear.r, linear.g, linear.b, c.a };
            string mapName = urp && material.HasProperty("_BaseMap") ? "_BaseMap" : material.HasProperty("_MainTex") ? "_MainTex" : null;
            var baseTex = mapName != null ? material.GetTexture(mapName) : material.mainTexture;
            m.baseMap = FileOf(baseTex);
            if (mapName != null) { var st = material.GetTextureScale(mapName); var of = material.GetTextureOffset(mapName); m.tiling = new[] { st.x, st.y }; m.offset = new[] { of.x, of.y }; }
            if (material.HasProperty("_Metallic")) m.metallic = material.GetFloat("_Metallic");
            string smooth = urp ? "_Smoothness" : "_Glossiness";
            if (material.HasProperty(smooth)) m.smoothness = material.GetFloat(smooth);
            if (material.HasProperty("_MetallicGlossMap")) m.metallicMap = FileOf(material.GetTexture("_MetallicGlossMap"));
            bool fromAlbedo = material.HasProperty("_SmoothnessTextureChannel") && material.GetFloat("_SmoothnessTextureChannel") > 0.5f;
            if (fromAlbedo && m.baseMap.Length > 0) m.smoothnessFrom = "albedo";
            else if (m.metallicMap.Length > 0)
            {
                m.smoothnessFrom = "metallic";
                if (!urp && material.HasProperty("_GlossMapScale")) m.smoothness = material.GetFloat("_GlossMapScale"); // Standard scales the map's alpha by it
            }
            if (material.HasProperty("_BumpMap")) m.normalMap = FileOf(material.GetTexture("_BumpMap"));
            if (material.HasProperty("_BumpScale")) m.normalScale = material.GetFloat("_BumpScale");
            if (material.HasProperty("_EmissionColor") && (material.IsKeywordEnabled("_EMISSION") || !material.HasProperty("_EmissionMap")))
            {
                var e = material.GetColor("_EmissionColor");
                if (e.maxColorComponent > 0f) { var el = e.linear; m.emission = new[] { el.r, el.g, el.b }; }
                if (material.HasProperty("_EmissionMap")) m.emissionMap = FileOf(material.GetTexture("_EmissionMap"));
            }
            if (material.HasProperty("_Cutoff")) m.cutoff = material.GetFloat("_Cutoff");
            if (urp)
            {
                if (material.HasProperty("_Surface") && material.GetFloat("_Surface") > 0.5f) m.alpha = "blend";
                else if (material.HasProperty("_AlphaClip") && material.GetFloat("_AlphaClip") > 0.5f) m.alpha = "clip";
            }
            else if (material.HasProperty("_Mode"))
            {
                float mode = material.GetFloat("_Mode"); // Standard: opaque, cutout, fade, transparent
                m.alpha = mode > 1.5f ? "blend" : mode > 0.5f ? "clip" : "opaque";
            }
            return m;
        }

        /// <summary>The texture's file on disk, or "" when it has none (a texture made in code).</summary>
        static string FileOf(Texture texture)
        {
            if (texture == null) return "";
            var asset = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(asset)) return "";
            if (asset.StartsWith("Packages/", StringComparison.Ordinal))
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(asset);
                if (package != null)
                    asset = Path.Combine(package.resolvedPath, asset.Substring(package.assetPath.Length + 1));
            }
            return File.Exists(asset) ? Path.GetFullPath(asset) : "";
        }

        /// <summary>Run by Blender: builds the meshes from the data file and saves the .blend.</summary>
        const string Script = @"import bpy, json, sys
args = sys.argv[sys.argv.index('--') + 1:]
data_path, out_path = args[0], args[1]
with open(data_path, encoding='utf-8') as f:
    d = json.load(f)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1.0
def inp(node, *names):
    for n in names:
        if n in node.inputs:
            return node.inputs[n]
    return None
mats = []
images = {}
for m in d['materials']:
    mat = bpy.data.materials.new(m['name'])
    c = m['color']
    mat.diffuse_color = (c[0], c[1], c[2], c[3])
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    bsdf = next((node for node in nodes if node.type == 'BSDF_PRINCIPLED'), None)
    if bsdf is None:
        mats.append(mat)
        continue
    uv = nodes.new('ShaderNodeUVMap')
    uv.uv_map = 'UVMap'
    mapping = nodes.new('ShaderNodeMapping')
    mapping.inputs['Scale'].default_value = (m['tiling'][0], m['tiling'][1], 1.0)  # Unity: uv * tiling + offset
    mapping.inputs['Location'].default_value = (m['offset'][0], m['offset'][1], 0.0)
    links.new(uv.outputs['UV'], mapping.inputs['Vector'])
    def image(path, color=True):
        try:
            key = (path, color)
            if key not in images:
                loaded = bpy.data.images.load(path, check_existing=False)
                if not color:
                    loaded.colorspace_settings.name = 'Non-Color'
                images[key] = loaded
            node = nodes.new('ShaderNodeTexImage')
            node.image = images[key]
            links.new(mapping.outputs['Vector'], node.inputs['Vector'])
            return node
        except Exception as e:
            print('CSGBRUSH_TEXTURE_WARNING:', path, e)
            return None
    def math(op, a, b):
        node = nodes.new('ShaderNodeMath')
        node.operation = op
        for i, v in enumerate((a, b)):
            if isinstance(v, (float, int)):
                node.inputs[i].default_value = v
            else:
                links.new(v, node.inputs[i])
        return node.outputs[0]
    base_color = inp(bsdf, 'Base Color')
    base_color.default_value = (c[0], c[1], c[2], 1.0)
    base = image(m['baseMap']) if m['baseMap'] else None
    if base:
        tint = nodes.new('ShaderNodeVectorMath')  # the map times the colour, as in Unity
        tint.operation = 'MULTIPLY'
        links.new(base.outputs['Color'], tint.inputs[0])
        tint.inputs[1].default_value = (c[0], c[1], c[2])
        links.new(tint.outputs['Vector'], base_color)
    metallic, roughness = inp(bsdf, 'Metallic'), inp(bsdf, 'Roughness')
    metallic.default_value = m['metallic']
    roughness.default_value = 1.0 - m['smoothness']
    mmap = image(m['metallicMap'], color=False) if m['metallicMap'] else None
    if mmap:
        split = nodes.new('ShaderNodeSeparateColor')
        links.new(mmap.outputs['Color'], split.inputs[0])
        links.new(split.outputs[0], metallic)  # Unity: metallic in red, smoothness in alpha
    smooth_alpha = mmap.outputs['Alpha'] if mmap and m['smoothnessFrom'] == 'metallic' else base.outputs['Alpha'] if base and m['smoothnessFrom'] == 'albedo' else None
    if smooth_alpha:
        links.new(math('SUBTRACT', 1.0, math('MULTIPLY', smooth_alpha, m['smoothness'])), roughness)
    nmap = image(m['normalMap'], color=False) if m['normalMap'] else None
    if nmap:
        normal = nodes.new('ShaderNodeNormalMap')
        normal.uv_map = 'UVMap'
        normal.inputs['Strength'].default_value = m['normalScale']
        links.new(nmap.outputs['Color'], normal.inputs['Color'])
        links.new(normal.outputs['Normal'], inp(bsdf, 'Normal'))
    e = m['emission'] if m['emission'] else [0.0, 0.0, 0.0]  # none: black, whatever this Blender's default is
    emission = inp(bsdf, 'Emission Color', 'Emission')
    emission.default_value = (e[0], e[1], e[2], 1.0)
    if m['emission']:
        strength = inp(bsdf, 'Emission Strength')
        if strength:
            strength.default_value = 1.0
        emap = image(m['emissionMap']) if m['emissionMap'] else None
        if emap:
            glow = nodes.new('ShaderNodeVectorMath')
            glow.operation = 'MULTIPLY'
            links.new(emap.outputs['Color'], glow.inputs[0])
            glow.inputs[1].default_value = (e[0], e[1], e[2])
            links.new(glow.outputs['Vector'], emission)
    if m['alpha'] != 'opaque':
        alpha = inp(bsdf, 'Alpha')
        coverage = math('MULTIPLY', base.outputs['Alpha'], c[3]) if base else c[3]
        if hasattr(mat, 'surface_render_method'):
            mat.surface_render_method = 'BLENDED' if m['alpha'] == 'blend' else 'DITHERED'
            if m['alpha'] == 'clip':
                coverage = math('GREATER_THAN', coverage, m['cutoff'])
        else:
            mat.blend_method = 'BLEND' if m['alpha'] == 'blend' else 'CLIP'
            if m['alpha'] == 'clip':
                mat.alpha_threshold = m['cutoff']
        if isinstance(coverage, (float, int)):
            alpha.default_value = coverage
        else:
            links.new(coverage, alpha)
    mats.append(mat)
for o in d['objects']:
    v, t = o['vertices'], o['triangles']
    me = bpy.data.meshes.new(o['name'])
    me.from_pydata([tuple(v[i:i + 3]) for i in range(0, len(v), 3)], [], [tuple(t[i:i + 3]) for i in range(0, len(t), 3)])
    for mi in o['materials']:
        me.materials.append(mats[mi] if mi >= 0 else None)
    me.polygons.foreach_set('material_index', o['faceMaterial'])
    loop_vertex = [0] * len(me.loops)
    me.loops.foreach_get('vertex_index', loop_vertex)
    for name, key in (('UVMap', 'uv0'), ('Lightmap', 'uv1')):
        uv = o[key]
        if uv:
            layer = me.uv_layers.new(name=name)
            layer.data.foreach_set('uv', [c for i in loop_vertex for c in (uv[i * 2], uv[i * 2 + 1])])
    if hasattr(me, 'use_auto_smooth'):
        me.use_auto_smooth = True
    me.polygons.foreach_set('use_smooth', [True] * len(me.polygons))
    n = o['normals']
    me.normals_split_custom_set_from_vertices([tuple(n[i:i + 3]) for i in range(0, len(n), 3)])
    me.update()
    ob = bpy.data.objects.new(o['name'], me)
    scene.collection.objects.link(ob)
if d['pack']:
    bpy.ops.file.pack_all()
bpy.context.preferences.filepaths.save_version = 0
if 'FINISHED' in bpy.ops.wm.save_as_mainfile(filepath=out_path):
    print('" + OkMarker + @"')
";
    }
}
