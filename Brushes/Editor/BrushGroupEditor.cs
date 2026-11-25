using static CsgBrush.Editor.BrushLanguage;
using UnityEditor;
using UnityEngine;

namespace CsgBrush.Editor
{
    [CustomEditor(typeof(BrushGroup))]
    [CanEditMultipleObjects]
    public sealed class BrushGroupEditor : UnityEditor.Editor
    {
        static bool s_RenderingOpen, s_LightingOpen = true, s_LightmappingOpen = true, s_ProbesOpen = true, s_AdditionalOpen = true;

        /// <summary>A section of the Rendering foldout, as a Mesh Renderer shows its Lighting, Probes and the rest.</summary>
        static bool Section(bool open, string title) => EditorGUILayout.Foldout(open, title, true, EditorStyles.foldoutHeader);

        /// <summary>The settings every render mesh of the group gets, laid out as on a Mesh Renderer.</summary>
        void DrawRendering()
        {
            serializedObject.Update();
            var r = serializedObject.FindProperty(nameof(BrushGroup.rendering));
            SerializedProperty P(string name) => r.FindPropertyRelative(name);
            void Field(string name)
            {
                var property = P(name);
                EditorGUILayout.PropertyField(property, BrushEditor.PropertyLabel(typeof(RenderingSettings), property));
            }
            s_RenderingOpen = EditorGUILayout.BeginFoldoutHeaderGroup(s_RenderingOpen, new GUIContent(Tr("Рендеринг"), Tr("Настройки рендереров всех мешей группы. Материалы берутся из брашей.")));
            if (s_RenderingOpen)
            {
                EditorGUI.BeginChangeCheck();
                EditorGUI.indentLevel++;
                s_LightingOpen = Section(s_LightingOpen, Tr("Освещение"));
                if (s_LightingOpen)
                {
                EditorGUI.indentLevel++;
                DrawRenderingOption(P(nameof(RenderingSettings.castShadows)),
                    ("Off", Tr("Отключено")), ("On", Tr("Включено")), ("TwoSided", Tr("С обеих сторон")), ("ShadowsOnly", Tr("Только тени")));
                Field(nameof(RenderingSettings.staticShadowCaster));
                Field(nameof(RenderingSettings.receiveShadows));
                DrawRenderingOption(P(nameof(RenderingSettings.receiveGlobalIllumination)),
                    ("Lightmaps", Tr("Карты освещения")), ("LightProbes", Tr("Световые пробы")));
                EditorGUI.indentLevel--;
                }
                s_LightmappingOpen = Section(s_LightmappingOpen, Tr("Карты освещения"));
                if (s_LightmappingOpen)
                {
                EditorGUI.indentLevel++;
                Field(nameof(RenderingSettings.scaleInLightmap));
                EditorGUILayout.PropertyField(P(nameof(RenderingSettings.stitchLightmapSeams)), new GUIContent(Tr("Сглаживать швы"), Tr("Сглаживать швы карты освещения на стыках граней.")));
                var lp = P(nameof(RenderingSettings.lightmapParameters));
                EditorGUI.showMixedValue = lp.hasMultipleDifferentValues;
                var chosen = EditorGUILayout.ObjectField(new GUIContent(Tr("Параметры карт освещения"), Tr("Пустое поле использует настройки сцены.")), lp.objectReferenceValue, typeof(LightmapParameters), false);
                EditorGUI.showMixedValue = false;
                if (chosen != lp.objectReferenceValue) lp.objectReferenceValue = chosen;
                EditorGUILayout.LabelField(Tr("UV карт освещения"), EditorStyles.miniBoldLabel);
                Field(nameof(RenderingSettings.lightmapHardAngle));
                Field(nameof(RenderingSettings.lightmapAngleError));
                Field(nameof(RenderingSettings.lightmapAreaError));
                Field(nameof(RenderingSettings.lightmapPackMargin));
                LightmapUVStatus();
                EditorGUI.indentLevel--;
                }
                s_ProbesOpen = Section(s_ProbesOpen, Tr("Пробы"));
                if (s_ProbesOpen)
                {
                EditorGUI.indentLevel++;
                DrawRenderingOption(P(nameof(RenderingSettings.lightProbes)),
                    ("Off", Tr("Отключено")), ("BlendProbes", Tr("Смешивать пробы")), ("UseProxyVolume", Tr("Использовать объём проб")), ("CustomProvided", Tr("Задавать из кода")));
                DrawRenderingOption(P(nameof(RenderingSettings.reflectionProbes)),
                    ("Off", Tr("Отключено")), ("BlendProbes", Tr("Смешивать пробы")), ("BlendProbesAndSkybox", Tr("Смешивать пробы и небо")), ("Simple", Tr("Одна проба")));
                Field(nameof(RenderingSettings.anchorOverride));
                EditorGUI.indentLevel--;
                }
                s_AdditionalOpen = Section(s_AdditionalOpen, Tr("Дополнительно"));
                if (s_AdditionalOpen)
                {
                EditorGUI.indentLevel++;
                DrawRenderingOption(P(nameof(RenderingSettings.motionVectors)),
                    ("Camera", Tr("От камеры")), ("Object", Tr("От объекта")), ("ForceNoMotion", Tr("Отключено")));
                Field(nameof(RenderingSettings.dynamicOcclusion));
                var mask = P(nameof(RenderingSettings.renderingLayerMask));
                EditorGUI.showMixedValue = mask.hasMultipleDifferentValues;
                uint m = (uint)EditorGUILayout.MaskField(new GUIContent(Tr("Маска слоёв рендеринга"), Tr("Слои для фильтрации источников света и декалей.")), (int)mask.uintValue, RenderingLayerNames());
                EditorGUI.showMixedValue = false;
                if (m != mask.uintValue) mask.uintValue = m;
                EditorGUI.indentLevel--;
                }
                EditorGUI.indentLevel--;
                if (EditorGUI.EndChangeCheck())
                {
                    serializedObject.ApplyModifiedProperties();
                    foreach (var t in targets) BrushCsg.ApplyRendererSettings((BrushGroup)t);
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        static void DrawRenderingOption(SerializedProperty property, params (string Name, string Label)[] labels)
        {
            var options = property.enumDisplayNames;
            var names = property.enumNames;
            for (int index = 0; index < names.Length; index++)
                foreach (var label in labels)
                    if (names[index] == label.Name) { options[index] = label.Label; break; }

            var rect = EditorGUILayout.GetControlRect();
            var content = EditorGUI.BeginProperty(rect, BrushEditor.PropertyLabel(typeof(RenderingSettings), property), property);
            EditorGUI.BeginChangeCheck();
            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
            int selected = EditorGUI.Popup(rect, content, property.enumValueIndex, System.Array.ConvertAll(options, text => new GUIContent(text)));
            EditorGUI.showMixedValue = false;
            if (EditorGUI.EndChangeCheck() && selected >= 0) property.enumValueIndex = selected;
            EditorGUI.EndProperty();
        }

        /// <summary>A moving group's brushes are not static (static meshes cannot move); a still one's get the default flags back.</summary>
        /// <summary>Whether the selected groups' lit meshes have lightmap UVs, and a button to make them.</summary>
        void LightmapUVStatus()
        {
            int missing = 0;
            foreach (var t in targets) if (t is BrushGroup g) missing += BrushLightmapUVs.Missing(g).Count;
            if (missing == 0) return;
            EditorGUILayout.HelpBox(BrushSettings.instance.autoLightmapUVs ? Tr("UV карт освещения будут созданы после завершения правки.") : Tr("У мешей нет UV для карт освещения. Создайте развёртку перед запеканием."), MessageType.Info);
            if (GUILayout.Button(Tr("Создать UV-развёртки для карт освещения")))
                foreach (var t in targets) if (t is BrushGroup g) BrushLightmapUVs.Generate(g);
        }

        static void SetMoving(BrushGroup group, bool moving)
        {
            var flags = moving ? 0 : BrushSettings.instance.defaultModelStaticFlags;
            Undo.RecordObject(group.gameObject, Tr("Изменить подвижность группы"));
            GameObjectUtility.SetStaticEditorFlags(group.gameObject, flags);
            foreach (var brush in BrushPrefabBuild.BrushesOf(group))
            {
                Undo.RecordObject(brush.gameObject, Tr("Изменить подвижность группы"));
                GameObjectUtility.SetStaticEditorFlags(brush.gameObject, flags);
                BrushSync.Ensure(brush);
            }
        }

        static string[] RenderingLayerNames()
        {
            var names = UnityEngine.RenderingLayerMask.GetDefinedRenderingLayerNames();
            return names != null && names.Length > 0 ? names : new[] { Tr("По умолчанию") };
        }

        public override void OnInspectorGUI()
        {
            if (targets.Length == 1) EditorGUILayout.LabelField(Tr("Брашей"), BrushPrefabBuild.BrushesOf((BrushGroup)target).Count.ToString());
            serializedObject.Update();
            var movingProp = serializedObject.FindProperty(nameof(BrushGroup.moving));
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(movingProp, new GUIContent(Tr("Подвижная группа"), Tr("Группа строится один раз и перемещается в игре целиком, например как лифт. Её браши становятся подвижными.")));
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                foreach (var t in targets) SetMoving((BrushGroup)t, ((BrushGroup)t).moving);
            }
            bool unsaved = false;
            foreach (var t in targets)
            {
                var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(((BrushGroup)t).gameObject);
                if (stage != null && stage.scene.isDirty) unsaved = true;
            }
            using (new EditorGUI.DisabledScope(unsaved))
                if (GUILayout.Button(new GUIContent(Tr("Перестроить"), unsaved ? Tr("Сначала сохраните префаб. Перестроение читает сохранённый файл.") : Tr("Перестроить меш и коллайдеры. Для группы в префабе результат записывается в префаб."))))
                    foreach (var t in targets) BrushCsg.Rebuild((BrushGroup)t);
            DrawRendering();
        }
    }

    [CustomEditor(typeof(CsgBrush.Colliders.ConvexColliderSettings)), CanEditMultipleObjects]
    sealed class ConvexColliderSettingsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI() => BrushEditor.DrawNamedProperties(serializedObject);
    }
}
