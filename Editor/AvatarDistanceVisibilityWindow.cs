#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using nadena.dev.modular_avatar.core;
using VRC.SDK3.Avatars.Components;

namespace HuanMeng.DistanceVisibility.Editor
{
    /// <summary>
    /// 拖动 avatar 根节点进来，即可批量管理 lilToon 距离显示：
    /// 添加 / 移除 / 恢复默认 / 逐项挑选材质 / 批量设置属性 / 生成 Modular Avatar 开关。
    /// </summary>
    public sealed class AvatarDistanceVisibilityWindow : EditorWindow
    {
        private const string GeneratedRoot = "Assets/@幻梦幻不梦蝶/材质距离显示工具/Generated";
        private const string DefaultParameterName = "DV_Visible";
        private const string MaterialPropertyName = "_DV_Enabled";
        private const string AnimatorLayerName = "DistanceVisibility";

        [Serializable]
        private sealed class MaterialEntry
        {
            public Material material;
            public bool selected;
        }

        /// <summary>批量设置的三种状态：不动、写成 1、写成 0。</summary>
        private enum ToggleMode
        {
            Keep = 0,
            Enable = 1,
            Disable = 2
        }

        [SerializeField] private GameObject avatarRoot;
        [SerializeField] private List<MaterialEntry> entries = new List<MaterialEntry>();
        [SerializeField] private Vector2 materialScroll;

        /// <summary>勾选规则：为 true 时，扫描/刷新后自动只勾选「已添加」的材质。</summary>
        [SerializeField] private bool autoSelectConverted;

        [SerializeField] private bool batchFoldout;

        [SerializeField] private ToggleMode enabledMode = ToggleMode.Keep;
        [SerializeField] private ToggleMode nearMode = ToggleMode.Keep;
        [SerializeField] private ToggleMode farMode = ToggleMode.Keep;
        [SerializeField] private ToggleMode meshCenterMode = ToggleMode.Keep;

        [SerializeField] private bool writeNearValues = true;
        [SerializeField] private float nearDistance = 2f;
        [SerializeField] private float nearFade = 0.2f;
        [SerializeField] private bool writeFarValues = true;
        [SerializeField] private float farDistance = 5f;
        [SerializeField] private float farFade = 0.2f;

        [SerializeField] private bool maFoldout = true;
        [SerializeField] private string maParameterName = DefaultParameterName;
        [SerializeField] private string maMenuLabel = "距离显示";
        [SerializeField] private string maHolderName = "材质距离显示开关";
        [SerializeField] private bool maDefaultOn;
        [SerializeField] private bool maParameterSaved;
        [SerializeField] private bool autoConvertBeforeMa = true;

        [MenuItem("Tools/材质距离显示工具/打开面板", false, 100)]
        public static void OpenWindow()
        {
            var window = GetWindow<AvatarDistanceVisibilityWindow>();
            window.titleContent = new GUIContent("材质距离显示");
            window.minSize = new Vector2(480f, 640f);
            window.Show();
        }

        [MenuItem("Tools/材质距离显示工具/对选中的 Avatar 一键添加距离显示", false, 101)]
        private static void MenuAddToSelection()
        {
            GameObject root = GetSelectionAvatarRoot();
            if(root == null)
            {
                EditorUtility.DisplayDialog("材质距离显示", "请先在 Hierarchy 中选中一个 Avatar。", "确定");
                return;
            }
            Material[] targets = DistanceVisibilityBridge.CollectMaterials(root)
                .Where(DistanceVisibilityBridge.CanConvert)
                .ToArray();
            if(targets.Length == 0)
            {
                EditorUtility.DisplayDialog("材质距离显示", "选中的 Avatar 下没有可添加距离显示的 lilToon 材质。", "确定");
                return;
            }
            DistanceVisibilityBridge.Convert(targets);
            SceneView.RepaintAll();
        }

        [MenuItem("Tools/材质距离显示工具/对选中的 Avatar 一键移除距离显示", false, 102)]
        private static void MenuRemoveFromSelection()
        {
            GameObject root = GetSelectionAvatarRoot();
            if(root == null)
            {
                EditorUtility.DisplayDialog("材质距离显示", "请先在 Hierarchy 中选中一个 Avatar。", "确定");
                return;
            }
            Material[] targets = DistanceVisibilityBridge.CollectMaterials(root)
                .Where(DistanceVisibilityBridge.IsConverted)
                .ToArray();
            if(targets.Length == 0)
            {
                EditorUtility.DisplayDialog("材质距离显示", "选中的 Avatar 下没有已添加距离显示的材质。", "确定");
                return;
            }
            DistanceVisibilityBridge.Restore(targets);
            SceneView.RepaintAll();
        }

        [MenuItem("Tools/材质距离显示工具/对选中的 Avatar 一键添加距离显示", true)]
        [MenuItem("Tools/材质距离显示工具/对选中的 Avatar 一键移除距离显示", true)]
        private static bool ValidateAvatarMenu()
        {
            return GetSelectionAvatarRoot() != null;
        }

        private void OnEnable()
        {
            if(avatarRoot != null && entries.Count == 0) Scan();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(4f);

            if(!DistanceVisibilityBridge.IsInstalled)
            {
                EditorGUILayout.HelpBox(
                    "没有找到 com.zhuozhi.liltoon-distance-visibility。" +
                    "请先在 Package Manager 里安装该包，添加/移除功能才能使用。",
                    MessageType.Error
                );
            }

            DrawAvatarSection();
            DrawMaterialSection();
            DrawBatchSection();
            DrawMaSection();
        }

        // ------------------------------------------------------------------
        // Avatar 与扫描
        // ------------------------------------------------------------------

        private void DrawAvatarSection()
        {
            EditorGUILayout.LabelField("Avatar", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            GameObject picked = (GameObject)EditorGUILayout.ObjectField(
                "Avatar 根节点（可拖入）",
                avatarRoot,
                typeof(GameObject),
                true
            );
            if(EditorGUI.EndChangeCheck())
            {
                avatarRoot = picked;
                Scan();
            }

            using(new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if(GUILayout.Button("使用当前选中", GUILayout.Width(110f)))
                {
                    avatarRoot = GetSelectionAvatarRoot();
                    Scan();
                }
                if(GUILayout.Button("重新扫描", GUILayout.Width(90f)))
                {
                    Scan();
                }
            }

            EditorGUILayout.Space(4f);
        }

        private void Scan()
        {
            entries.Clear();
            if(avatarRoot != null)
            {
                foreach(Material material in DistanceVisibilityBridge.CollectMaterials(avatarRoot))
                {
                    entries.Add(
                        new MaterialEntry
                        {
                            material = material,
                            selected = DefaultSelected(material)
                        }
                    );
                }
            }
            Repaint();
        }

        /// <summary>扫描后每行的默认勾选状态，受「快捷勾选」开关影响。</summary>
        private bool DefaultSelected(Material material)
        {
            if(autoSelectConverted) return DistanceVisibilityBridge.IsConverted(material);
            return DistanceVisibilityBridge.CanConvert(material) ||
                DistanceVisibilityBridge.IsConverted(material);
        }

        // ------------------------------------------------------------------
        // 材质列表
        // ------------------------------------------------------------------

        private void DrawMaterialSection()
        {
            EditorGUILayout.LabelField("材质列表", EditorStyles.boldLabel);

            int total = entries.Count(entry => entry.material != null);
            int converted = entries.Count(entry => DistanceVisibilityBridge.IsConverted(entry.material));
            int enabled = entries.Count(entry => DistanceVisibilityBridge.IsEnabled(entry.material));
            int convertible = entries.Count(
                entry => DistanceVisibilityBridge.CanConvert(entry.material) &&
                    !DistanceVisibilityBridge.IsConverted(entry.material)
            );
            int selected = entries.Count(entry => entry.selected && entry.material != null);

            EditorGUILayout.HelpBox(
                "共 " + total + " 个材质：已添加 " + converted + " 个（已启用 " + enabled +
                "，已关闭 " + (converted - enabled) + "），可添加 " + convertible +
                " 个，当前勾选 " + selected + " 个。\n" +
                "批量改属性：点「只选已添加」→ 到下面「批量属性设置」统一改。",
                MessageType.None
            );

            EditorGUI.BeginChangeCheck();
            autoSelectConverted = EditorGUILayout.ToggleLeft(
                "快捷勾选：自动只勾选「已添加」的材质",
                autoSelectConverted
            );
            if(EditorGUI.EndChangeCheck() && autoSelectConverted)
            {
                SelectOnly(DistanceVisibilityBridge.IsConverted);
            }

            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button("全选")) SetAllSelected(true);
                if(GUILayout.Button("全不选")) SetAllSelected(false);
                if(GUILayout.Button("反选")) ToggleAllSelected();
            }

            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button("只选已添加")) SelectOnly(DistanceVisibilityBridge.IsConverted);
                if(GUILayout.Button("只选已启用")) SelectOnly(DistanceVisibilityBridge.IsEnabled);
                if(GUILayout.Button("只选已关闭")) SelectOnly(IsConvertedButDisabled);
                if(GUILayout.Button("只选可添加")) SelectOnly(DistanceVisibilityBridge.CanConvert);
            }

            materialScroll = EditorGUILayout.BeginScrollView(
                materialScroll,
                GUILayout.MinHeight(160f),
                GUILayout.ExpandHeight(true)
            );
            if(entries.Count == 0)
            {
                EditorGUILayout.LabelField("（列表为空：拖入 Avatar 后点「重新扫描」）");
            }
            foreach(MaterialEntry entry in entries)
            {
                using(new EditorGUILayout.HorizontalScope())
                {
                    entry.selected = EditorGUILayout.Toggle(entry.selected, GUILayout.Width(18f));
                    using(new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.ObjectField(entry.material, typeof(Material), false);
                    }
                    EditorGUILayout.LabelField(StatusText(entry.material), GUILayout.Width(70f));
                }
            }
            EditorGUILayout.EndScrollView();

            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button("添加距离显示")) AddToSelected();
                if(GUILayout.Button("移除距离显示")) RemoveFromSelected();
                if(GUILayout.Button("恢复默认属性")) ResetSelected();
            }

            EditorGUILayout.Space(4f);
        }

        private static string StatusText(Material material)
        {
            if(material == null) return "空";
            if(DistanceVisibilityBridge.IsConverted(material)) return "已添加";
            if(DistanceVisibilityBridge.CanConvert(material)) return "可添加";
            return "不支持";
        }

        private void SetAllSelected(bool value)
        {
            foreach(MaterialEntry entry in entries) entry.selected = value;
        }

        private void ToggleAllSelected()
        {
            foreach(MaterialEntry entry in entries) entry.selected = !entry.selected;
        }

        /// <summary>按条件重新勾选：满足条件的勾上，其余取消。</summary>
        private void SelectOnly(Func<Material, bool> predicate)
        {
            foreach(MaterialEntry entry in entries)
                entry.selected = entry.material != null && predicate(entry.material);
            Repaint();
        }

        private static bool IsConvertedButDisabled(Material material)
        {
            return DistanceVisibilityBridge.IsConverted(material) &&
                !DistanceVisibilityBridge.IsEnabled(material);
        }

        private Material[] SelectedMaterials()
        {
            return entries
                .Where(entry => entry.selected && entry.material != null)
                .Select(entry => entry.material)
                .Distinct()
                .ToArray();
        }

        private void AddToSelected()
        {
            Material[] targets = SelectedMaterials()
                .Where(DistanceVisibilityBridge.CanConvert)
                .ToArray();
            if(targets.Length == 0)
            {
                EditorUtility.DisplayDialog(
                    "材质距离显示",
                    "勾选的材质里没有可以添加距离显示的 lilToon 材质。",
                    "确定"
                );
                return;
            }
            if(DistanceVisibilityBridge.Convert(targets)) Scan();
        }

        private void RemoveFromSelected()
        {
            Material[] targets = SelectedMaterials()
                .Where(DistanceVisibilityBridge.IsConverted)
                .ToArray();
            if(targets.Length == 0)
            {
                EditorUtility.DisplayDialog("材质距离显示", "勾选的材质里没有已添加距离显示的材质。", "确定");
                return;
            }
            if(DistanceVisibilityBridge.Restore(targets)) Scan();
        }

        private void ResetSelected()
        {
            Material[] targets = SelectedMaterials()
                .Where(DistanceVisibilityBridge.IsConverted)
                .ToArray();
            if(targets.Length == 0)
            {
                EditorUtility.DisplayDialog("材质距离显示", "勾选的材质里没有已添加距离显示的材质。", "确定");
                return;
            }
            DistanceVisibilityBridge.ResetToDefaults(targets);
            Debug.Log("材质距离显示工具：已把 " + targets.Length + " 个材质恢复为默认属性（默认不显示）。");
        }

        // ------------------------------------------------------------------
        // 批量属性
        // ------------------------------------------------------------------

        private static readonly string[] ToggleModeLabels =
        {
            "不修改",
            "启用（写 1）",
            "关闭（写 0）"
        };

        private void DrawBatchSection()
        {
            batchFoldout = EditorGUILayout.Foldout(batchFoldout, "批量属性设置", true);
            if(!batchFoldout) return;

            EditorGUILayout.HelpBox(
                "每项选「不修改」就保持材质原值，选「启用 / 关闭」才会写进去。\n" +
                "「启用距离显示」是总开关（_DV_Enabled），默认是关的：只改距离参数而不打开它，画面不会有任何变化。",
                MessageType.None
            );

            EditorGUI.indentLevel++;

            enabledMode = (ToggleMode)EditorGUILayout.Popup(
                "启用距离显示",
                (int)enabledMode,
                ToggleModeLabels
            );

            EditorGUILayout.Space(2f);
            nearMode = (ToggleMode)EditorGUILayout.Popup(
                "近端限制",
                (int)nearMode,
                ToggleModeLabels
            );
            writeNearValues = EditorGUILayout.ToggleLeft("　写入近端距离 / 过渡", writeNearValues);
            using(new EditorGUI.DisabledScope(!writeNearValues))
            {
                nearDistance = EditorGUILayout.FloatField("　　近端距离（米）", nearDistance);
                nearFade = EditorGUILayout.FloatField("　　近端过渡（米）", nearFade);
            }

            EditorGUILayout.Space(2f);
            farMode = (ToggleMode)EditorGUILayout.Popup(
                "远端限制",
                (int)farMode,
                ToggleModeLabels
            );
            writeFarValues = EditorGUILayout.ToggleLeft("　写入远端距离 / 过渡", writeFarValues);
            using(new EditorGUI.DisabledScope(!writeFarValues))
            {
                farDistance = EditorGUILayout.FloatField("　　远端距离（米）", farDistance);
                farFade = EditorGUILayout.FloatField("　　远端过渡（米）", farFade);
            }

            EditorGUILayout.Space(2f);
            meshCenterMode = (ToggleMode)EditorGUILayout.Popup(
                "使用网格中心",
                (int)meshCenterMode,
                ToggleModeLabels
            );

            EditorGUI.indentLevel--;

            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button("应用到勾选的材质")) ApplyBatchSettings();
                if(GUILayout.Button("从第一个材质读取")) ReadBatchSettings();
            }

            EditorGUILayout.Space(4f);
        }

        private void ApplyBatchSettings()
        {
            Material[] targets = SelectedMaterials()
                .Where(DistanceVisibilityBridge.IsConverted)
                .ToArray();
            if(targets.Length == 0)
            {
                EditorUtility.DisplayDialog("材质距离显示", "没有勾选已添加距离显示的材质。", "确定");
                return;
            }
            if(enabledMode == ToggleMode.Keep &&
                nearMode == ToggleMode.Keep &&
                farMode == ToggleMode.Keep &&
                meshCenterMode == ToggleMode.Keep &&
                !writeNearValues &&
                !writeFarValues)
            {
                EditorUtility.DisplayDialog(
                    "材质距离显示",
                    "所有项目都是「不修改」，没有需要写入的内容。",
                    "确定"
                );
                return;
            }

            Undo.RecordObjects(targets, "材质距离显示工具：批量设置属性");
            foreach(Material material in targets)
            {
                ApplyToggle(material, "_DV_Enabled", enabledMode);
                ApplyToggle(material, "_DV_NearEnabled", nearMode);
                ApplyToggle(material, "_DV_FarEnabled", farMode);
                ApplyToggle(material, "_DV_UseMeshCenter", meshCenterMode);

                if(writeNearValues)
                {
                    SetFloat(material, "_DV_NearDistance", Mathf.Max(0f, nearDistance));
                    SetFloat(material, "_DV_NearFade", Mathf.Max(0f, nearFade));
                }
                if(writeFarValues)
                {
                    SetFloat(material, "_DV_FarDistance", Mathf.Max(0f, farDistance));
                    SetFloat(material, "_DV_FarFade", Mathf.Max(0f, farFade));
                }
                EditorUtility.SetDirty(material);
            }
            AssetDatabase.SaveAssets();
            // 全名引用：UnityEditorInternal 里也有 AnimatorController，直接 using 会和
            // UnityEditor.Animations.AnimatorController 冲突（CS0104）。
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            Debug.Log(
                "材质距离显示工具：已更新 " + targets.Length + " 个材质（总开关 " +
                ToggleModeLabels[(int)enabledMode] + "，近端 " + ToggleModeLabels[(int)nearMode] +
                "，远端 " + ToggleModeLabels[(int)farMode] + "）。"
            );
        }

        private void ReadBatchSettings()
        {
            Material source = SelectedMaterials().FirstOrDefault(DistanceVisibilityBridge.IsConverted);
            if(source == null)
            {
                EditorUtility.DisplayDialog("材质距离显示", "没有勾选已添加距离显示的材质。", "确定");
                return;
            }

            enabledMode = ReadFloat(source, "_DV_Enabled", 0f) >= 0.5f
                ? ToggleMode.Enable
                : ToggleMode.Disable;
            nearMode = ReadFloat(source, "_DV_NearEnabled", 1f) >= 0.5f
                ? ToggleMode.Enable
                : ToggleMode.Disable;
            farMode = ReadFloat(source, "_DV_FarEnabled", 1f) >= 0.5f
                ? ToggleMode.Enable
                : ToggleMode.Disable;
            meshCenterMode = ReadFloat(source, "_DV_UseMeshCenter", 0f) >= 0.5f
                ? ToggleMode.Enable
                : ToggleMode.Disable;

            nearDistance = ReadFloat(source, "_DV_NearDistance", 2f);
            nearFade = ReadFloat(source, "_DV_NearFade", 0.2f);
            farDistance = ReadFloat(source, "_DV_FarDistance", 5f);
            farFade = ReadFloat(source, "_DV_FarFade", 0.2f);
            writeNearValues = true;
            writeFarValues = true;
            Repaint();
        }

        private static void ApplyToggle(Material material, string propertyName, ToggleMode mode)
        {
            if(mode == ToggleMode.Keep) return;
            SetFloat(material, propertyName, mode == ToggleMode.Enable ? 1f : 0f);
        }

        private static float ReadFloat(Material material, string propertyName, float fallback)
        {
            return material.HasProperty(propertyName)
                ? material.GetFloat(propertyName)
                : fallback;
        }

        private static void SetFloat(Material material, string propertyName, float value)
        {
            if(material.HasProperty(propertyName)) material.SetFloat(propertyName, value);
        }

        // ------------------------------------------------------------------
        // Modular Avatar 开关
        // ------------------------------------------------------------------

        private void DrawMaSection()
        {
            maFoldout = EditorGUILayout.Foldout(maFoldout, "Modular Avatar 开关", true);
            if(!maFoldout) return;

            maParameterName = EditorGUILayout.TextField("参数名", maParameterName);
            maMenuLabel = EditorGUILayout.TextField("菜单显示名", maMenuLabel);
            maHolderName = EditorGUILayout.TextField("开关物体名", maHolderName);
            maDefaultOn = EditorGUILayout.Toggle("默认开启", maDefaultOn);
            maParameterSaved = EditorGUILayout.Toggle("参数保存（saved）", maParameterSaved);
            autoConvertBeforeMa = EditorGUILayout.Toggle("生成前自动添加距离显示", autoConvertBeforeMa);

            EditorGUILayout.HelpBox(
                "会生成 1 个 Animator 控制器（开 / 关两个状态，动画 material._DV_Enabled）" +
                "+ MA Merge Animator + MA Menu Item（Toggle）" +
                "+ MA Parameters（注册同名参数：类型 Bool、已同步；saved 由上面的勾选决定）。" +
                "上传时该开关会出现在表情菜单根目录，一键控制下面这些材质的距离显示开关。\n" +
                "注意：Unity 的材质属性动画按 Renderer 生效，所以同一个 Renderer 上的其它已添加材质也会被一起控制。",
                MessageType.Info
            );

            using(new EditorGUILayout.HorizontalScope())
            {
                using(new EditorGUI.DisabledScope(avatarRoot == null))
                {
                    if(GUILayout.Button("生成 / 更新 MA 开关")) GenerateMaToggle();
                }
                using(new EditorGUI.DisabledScope(avatarRoot == null))
                {
                    if(GUILayout.Button("移除 MA 开关")) RemoveMaToggle();
                }
            }
        }

        private void GenerateMaToggle()
        {
            if(avatarRoot == null)
            {
                EditorUtility.DisplayDialog("材质距离显示", "请先指定 Avatar 根节点。", "确定");
                return;
            }

            Material[] selected = SelectedMaterials();
            if(selected.Length == 0)
            {
                EditorUtility.DisplayDialog("材质距离显示", "请先在材质列表里勾选要控制的材质。", "确定");
                return;
            }

            if(autoConvertBeforeMa)
            {
                Material[] convertible = selected
                    .Where(DistanceVisibilityBridge.CanConvert)
                    .ToArray();
                if(convertible.Length > 0) DistanceVisibilityBridge.Convert(convertible);
            }

            Material[] converted = selected
                .Where(DistanceVisibilityBridge.IsConverted)
                .ToArray();
            if(converted.Length == 0)
            {
                EditorUtility.DisplayDialog(
                    "材质距离显示",
                    "勾选的材质都还没有添加距离显示，无法生成开关。",
                    "确定"
                );
                return;
            }

            List<Renderer> renderers = DistanceVisibilityBridge.CollectRenderers(
                avatarRoot,
                converted
            );
            if(renderers.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "材质距离显示",
                    "在 Avatar 下没有找到使用这些材质的 Renderer。",
                    "确定"
                );
                return;
            }

            string parameterName = string.IsNullOrWhiteSpace(maParameterName)
                ? DefaultParameterName
                : maParameterName.Trim();

            string avatarFolder = EnsureFolder(
                GeneratedRoot + "/" + SanitizeFileName(avatarRoot.name)
            );
            if(string.IsNullOrEmpty(avatarFolder)) return;

            string onPath = avatarFolder + "/DistanceVisibility_On.anim";
            string offPath = avatarFolder + "/DistanceVisibility_Off.anim";
            string controllerPath = avatarFolder + "/DistanceVisibility.controller";

            AssetDatabase.DeleteAsset(onPath);
            AssetDatabase.DeleteAsset(offPath);
            AssetDatabase.DeleteAsset(controllerPath);

            AnimationClip onClip = BuildMaterialEnableClip(onPath, renderers, 1f);
            AnimationClip offClip = BuildMaterialEnableClip(offPath, renderers, 0f);
            if(onClip == null || offClip == null) return;

            AnimatorController controller = BuildController(
                controllerPath,
                parameterName,
                onClip,
                offClip,
                maDefaultOn
            );
            if(controller == null) return;

            CreateMaHolder(controller, parameterName, maDefaultOn);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Scan();
            Debug.Log(
                "材质距离显示工具：已生成 MA 开关「" + maMenuLabel + "」，参数 " + parameterName +
                "，覆盖 " + renderers.Count + " 个 Renderer / " + converted.Length + " 个材质。"
            );
        }

        private AnimationClip BuildMaterialEnableClip(
            string path,
            IEnumerable<Renderer> renderers,
            float value
        )
        {
            var clip = new AnimationClip { frameRate = 60f };
            foreach(Renderer renderer in renderers)
            {
                if(renderer == null) continue;
                string transformPath = AnimationUtility.CalculateTransformPath(
                    renderer.transform,
                    avatarRoot.transform
                );
                EditorCurveBinding binding = EditorCurveBinding.FloatCurve(
                    transformPath,
                    renderer.GetType(),
                    "material." + MaterialPropertyName
                );
                AnimationUtility.SetEditorCurve(
                    clip,
                    binding,
                    AnimationCurve.Constant(0f, 1f / 60f, value)
                );
            }

            AssetDatabase.CreateAsset(clip, path);
            var saved = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            return saved != null ? saved : clip;
        }

        private static AnimatorController BuildController(
            string path,
            string parameterName,
            AnimationClip onClip,
            AnimationClip offClip,
            bool defaultOn
        )
        {
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            if(controller == null)
            {
                Debug.LogError("材质距离显示工具：创建 Animator 控制器失败：" + path);
                return null;
            }

            controller.AddParameter(parameterName, AnimatorControllerParameterType.Bool);

            AnimatorControllerLayer[] layers = controller.layers;
            layers[0].name = AnimatorLayerName;
            controller.layers = layers;

            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState onState = machine.AddState("On");
            onState.motion = onClip;
            onState.writeDefaultValues = false;

            AnimatorState offState = machine.AddState("Off");
            offState.motion = offClip;
            offState.writeDefaultValues = false;

            machine.defaultState = defaultOn ? onState : offState;

            AddBoolTransition(offState, onState, parameterName, true);
            AddBoolTransition(onState, offState, parameterName, false);

            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static void AddBoolTransition(
            AnimatorState from,
            AnimatorState to,
            string parameterName,
            bool whenTrue
        )
        {
            AnimatorStateTransition transition = from.AddTransition(to);
            if(transition == null) return;
            transition.hasExitTime = false;
            transition.exitTime = 0f;
            transition.hasFixedDuration = true;
            transition.duration = 0f;
            transition.canTransitionToSelf = false;
            transition.AddCondition(
                whenTrue ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot,
                0f,
                parameterName
            );
        }

        private void CreateMaHolder(
            RuntimeAnimatorController controller,
            string parameterName,
            bool defaultOn
        )
        {
            Transform existing = avatarRoot.transform.Find(maHolderName);
            if(existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

            var holder = new GameObject(maHolderName);
            Undo.RegisterCreatedObjectUndo(holder, "创建材质距离显示开关");
            holder.transform.SetParent(avatarRoot.transform, false);

            var merge = holder.AddComponent<ModularAvatarMergeAnimator>();
            merge.animator = controller;
            merge.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
            merge.pathMode = MergeAnimatorPathMode.Absolute;
            merge.deleteAttachedAnimator = true;
            merge.matchAvatarWriteDefaults = true;

            var item = holder.AddComponent<ModularAvatarMenuItem>();
            item.PortableControl.Type = PortableControlType.Toggle;
            item.PortableControl.Parameter = parameterName;
            item.PortableControl.Value = 1f;
            item.label = maMenuLabel;
            item.isSynced = true;
            item.isSaved = true;
            item.isDefault = defaultOn;
            item.automaticValue = false;
            item.MenuSource = SubmenuSource.Children;

            holder.AddComponent<ModularAvatarMenuInstaller>();

            // 用 MA Parameters 显式声明这个参数：
            // 只靠 MA Menu Item 自动创建的话，参数不一定被注册进 Expression Parameters，
            // 结果就是别人看不到你这边开关的效果（不同步）。
            var parameters = holder.AddComponent<ModularAvatarParameters>();
            parameters.parameters.Add(
                new ParameterConfig
                {
                    nameOrPrefix = parameterName,
                    internalParameter = false,
                    isPrefix = false,
                    // Bool = 已同步；NotSynced 才是不注册到 Expression Parameters
                    syncType = ParameterSyncType.Bool,
                    localOnly = false,
                    saved = maParameterSaved,
                    defaultValue = defaultOn ? 1f : 0f,
                    hasExplicitDefaultValue = true
                }
            );

            EditorUtility.SetDirty(holder);
            if(holder.scene.IsValid()) EditorSceneManager.MarkSceneDirty(holder.scene);
            Selection.activeGameObject = holder;
            EditorGUIUtility.PingObject(holder);
        }

        private void RemoveMaToggle()
        {
            if(avatarRoot == null) return;
            Transform existing = avatarRoot.transform.Find(maHolderName);
            if(existing == null)
            {
                EditorUtility.DisplayDialog(
                    "材质距离显示",
                    "Avatar 下没有找到名为「" + maHolderName + "」的开关物体。",
                    "确定"
                );
                return;
            }
            Undo.DestroyObjectImmediate(existing.gameObject);
            Debug.Log("材质距离显示工具：已移除 MA 开关「" + maHolderName + "」。");
        }

        // ------------------------------------------------------------------
        // 工具方法
        // ------------------------------------------------------------------

        private static GameObject GetSelectionAvatarRoot()
        {
            GameObject selected = Selection.activeGameObject;
            if(selected == null) return null;

            VRCAvatarDescriptor descriptor = selected.GetComponentInParent<VRCAvatarDescriptor>(true);
            return descriptor != null ? descriptor.gameObject : selected;
        }

        private static string SanitizeFileName(string name)
        {
            if(string.IsNullOrEmpty(name)) return "Avatar";
            string invalid = new string(Path.GetInvalidFileNameChars());
            var builder = new System.Text.StringBuilder(name.Length);
            foreach(char character in name)
            {
                builder.Append(invalid.IndexOf(character) >= 0 ? '_' : character);
            }
            return builder.ToString();
        }

        private static string EnsureFolder(string path)
        {
            if(AssetDatabase.IsValidFolder(path)) return path;

            string normalized = path.Replace('\\', '/').TrimEnd('/');
            string parent = Path.GetDirectoryName(normalized)?.Replace('\\', '/');
            string leaf = Path.GetFileName(normalized);
            if(string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) return null;

            string ensuredParent = EnsureFolder(parent);
            if(string.IsNullOrEmpty(ensuredParent)) return null;

            string guid = AssetDatabase.CreateFolder(ensuredParent, leaf);
            if(string.IsNullOrEmpty(guid)) return null;

            string createdPath = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(createdPath) ? null : createdPath;
        }
    }
}
#endif
