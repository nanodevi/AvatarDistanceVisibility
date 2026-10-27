#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace HuanMeng.DistanceVisibility.Editor
{
    /// <summary>
    /// com.zhuozhi.liltoon-distance-visibility 的 Editor API 全部是 internal，
    /// 这里用反射封装一层，好处是：
    /// 1. 不需要改那个包，也不用加 asmdef；
    /// 2. 那个包被移除/改名时，本工具只是降级为"不可用"，不会连编译都过不了。
    /// </summary>
    public static class DistanceVisibilityBridge
    {
        private const string ManagerTypeName = "DistanceVisibility.Editor.DistanceVisibilityShaderManager";

        private static bool typeSearched;
        private static Type managerType;

        public static bool IsInstalled
        {
            get
            {
                EnsureManagerType();
                return managerType != null;
            }
        }

        private static void EnsureManagerType()
        {
            if(typeSearched) return;
            typeSearched = true;

            foreach(Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(ManagerTypeName, false);
                if(type == null) continue;
                managerType = type;
                return;
            }

            Debug.LogWarning(
                "材质距离显示工具：没有找到 " + ManagerTypeName +
                "，请确认已安装 com.zhuozhi.liltoon-distance-visibility。"
            );
        }

        private static MethodInfo FindMethod(string methodName, params Type[] parameterTypes)
        {
            EnsureManagerType();
            return managerType == null
                ? null
                : managerType.GetMethod(
                    methodName,
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    parameterTypes,
                    null
                );
        }

        private static object Invoke(string methodName, object[] arguments, params Type[] parameterTypes)
        {
            MethodInfo method = FindMethod(methodName, parameterTypes);
            if(method == null) return null;
            try
            {
                return method.Invoke(null, arguments);
            }
            catch(TargetInvocationException exception)
            {
                Debug.LogException(exception.InnerException ?? exception);
                return null;
            }
            catch(Exception exception)
            {
                Debug.LogException(exception);
                return null;
            }
        }

        public static bool TryEnsureShaders(bool force)
        {
            object result = Invoke(
                "EnsureGeneratedShaders",
                new object[] { force },
                typeof(bool)
            );
            return result is bool && (bool)result;
        }

        public static bool IsSupportedOriginal(Shader shader)
        {
            if(shader == null) return false;
            object result = Invoke("IsSupportedOriginal", new object[] { shader }, typeof(Shader));
            return result is bool && (bool)result;
        }

        public static bool IsDistanceShader(Shader shader)
        {
            if(shader == null) return false;
            object result = Invoke("IsDistanceShader", new object[] { shader }, typeof(Shader));
            return result is bool && (bool)result;
        }

        public static bool IsConverted(Material material)
        {
            return material != null &&
                material.HasProperty("_DV_Enabled") &&
                IsDistanceShader(material.shader);
        }

        public static bool CanConvert(Material material)
        {
            return material != null && IsSupportedOriginal(material.shader);
        }

        /// <summary>已添加距离显示，并且「启用距离显示」处于打开状态。</summary>
        public static bool IsEnabled(Material material)
        {
            return material != null &&
                material.HasProperty("_DV_Enabled") &&
                material.GetFloat("_DV_Enabled") >= 0.5f;
        }

        /// <summary>把材质切换到 DistanceVisibility 系列 Shader（等价于右键"添加距离显示"）。</summary>
        public static bool Convert(IEnumerable<Material> source)
        {
            return RunOnMaterials("ConvertMaterials", source, "材质距离显示工具：添加距离显示");
        }

        /// <summary>还原成原始 lilToon Shader（等价于右键"移除距离显示"）。</summary>
        public static bool Restore(IEnumerable<Material> source)
        {
            return RunOnMaterials("RestoreMaterials", source, "材质距离显示工具：移除距离显示");
        }

        private static bool RunOnMaterials(string methodName, IEnumerable<Material> source, string logPrefix)
        {
            Material[] materials = source == null
                ? Array.Empty<Material>()
                : source.Where(material => material != null).Distinct().ToArray();
            if(materials.Length == 0) return false;

            MethodInfo method = FindMethod(methodName, typeof(IEnumerable<Material>));
            if(method == null)
            {
                Debug.LogError(logPrefix + " 失败：距离显示包没有提供 " + methodName + "。");
                return false;
            }

            try
            {
                method.Invoke(null, new object[] { materials });
                return true;
            }
            catch(TargetInvocationException exception)
            {
                Debug.LogException(exception.InnerException ?? exception);
                return false;
            }
            catch(Exception exception)
            {
                Debug.LogException(exception);
                return false;
            }
        }

        /// <summary>恢复 _DV_* 到默认值（默认关闭、近端 2 米、远端 5 米）。</summary>
        public static void ResetToDefaults(IEnumerable<Material> source)
        {
            Material[] materials = source == null
                ? Array.Empty<Material>()
                : source.Where(material => material != null && material.HasProperty("_DV_Enabled")).Distinct().ToArray();
            if(materials.Length == 0) return;

            MethodInfo initialize = FindMethod("InitializeMaterial", typeof(Material));
            if(initialize != null)
            {
                foreach(Material material in materials)
                {
                    try
                    {
                        initialize.Invoke(null, new object[] { material });
                        EditorUtility.SetDirty(material);
                    }
                    catch(TargetInvocationException exception)
                    {
                        Debug.LogException(exception.InnerException ?? exception);
                    }
                }
                AssetDatabase.SaveAssets();
                return;
            }

            Undo.RecordObjects(materials, "材质距离显示工具：恢复默认属性");
            foreach(Material material in materials)
            {
                material.SetFloat("_DV_Enabled", 0f);
                material.SetFloat("_DV_Mode", 0f);
                material.SetFloat("_DV_NearEnabled", 1f);
                material.SetFloat("_DV_NearDistance", 2f);
                material.SetFloat("_DV_NearFade", 0.2f);
                material.SetFloat("_DV_FarEnabled", 1f);
                material.SetFloat("_DV_FarDistance", 5f);
                material.SetFloat("_DV_FarFade", 0.2f);
                material.SetFloat("_DV_UseMeshCenter", 0f);
                material.SetFloat("_DV_Version", 4f);
                EditorUtility.SetDirty(material);
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>Avatar 下所有 Renderer 用到的材质（去重，包含未激活对象）。</summary>
        public static List<Material> CollectMaterials(GameObject root)
        {
            var result = new List<Material>();
            if(root == null) return result;

            var seen = new HashSet<Material>();
            foreach(Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                foreach(Material material in renderer.sharedMaterials)
                {
                    if(material == null || !seen.Add(material)) continue;
                    result.Add(material);
                }
            }
            return result;
        }

        public static List<Renderer> CollectRenderers(GameObject root, ICollection<Material> materials)
        {
            var result = new List<Renderer>();
            if(root == null) return result;

            var filter = materials == null
                ? null
                : new HashSet<Material>(materials);
            foreach(Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if(renderer == null) continue;
                if(filter == null || renderer.sharedMaterials.Any(filter.Contains))
                    result.Add(renderer);
            }
            return result;
        }
    }
}
#endif
