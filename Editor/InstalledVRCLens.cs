using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Micro.VRCLensMA.Editor
{
    // This is an adapter to a separately purchased installation. No vendor implementation is bundled.
    internal sealed class InstalledVRCLens
    {
        private const string Namespace = "Hirabiki.AV3.Works.VRCLens.";
        private const BindingFlags Methods = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        internal readonly Type SetupType;
        private readonly Type inspectorType;

        internal InstalledVRCLens()
        {
            SetupType = FindType(Namespace + "VRCLensSetup");
            inspectorType = FindType(Namespace + "ConfigInspector");
            var verifier = FindType(Namespace + "VRCLensVerifier");
            var version = verifier.GetField("VRCLENS_VERSION")?.GetValue(null) as string;
            if (version != "1.10.0")
                throw new InvalidOperationException($"VRCLens MA Installer: 対応するVRCLensは1.10.0です（検出: {version ?? "不明"}）。更新されたVRCLens用のアダプターが必要です。");
            RequireMethod(inspectorType, "ApplySetup");
            RequireMethod(SetupType, "AppendAnimationSetup");
        }

        private static Type FindType(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(name, false)).FirstOrDefault(t => t != null)
            ?? throw new InvalidOperationException("VRCLens 1.10.0を先にインポートしてください。見つからない型: " + name);

        private static MethodInfo RequireMethod(Type type, string name) => type.GetMethod(name, Methods, null, Type.EmptyTypes, null)
            ?? throw new InvalidOperationException("VRCLensのAPIが対応バージョンと異なります: " + name);

        internal Component GetSetup(GameObject root) => root != null ? root.GetComponent(SetupType) : null;

        internal string RootPath(Component setup)
        {
            var script = MonoScript.FromMonoBehaviour((MonoBehaviour)setup);
            var path = AssetDatabase.GetAssetPath(script).Replace('\\', '/');
            const string suffix = "/Resource/VRCLensSetup.cs";
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(suffix, StringComparison.Ordinal))
                throw new InvalidOperationException("VRCLensSetup.csはAssets以下のVRCLens/Resourceに配置してください。");
            return path.Substring(0, path.Length - suffix.Length);
        }

        internal GameObject FindPrefab()
        {
            foreach (var guid in AssetDatabase.FindAssets("VRCLensSetup t:MonoScript"))
            {
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
                if (script == null || script.GetClass() != SetupType) continue;
                var path = AssetDatabase.GetAssetPath(script).Replace('\\', '/');
                var root = System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(path)).Replace('\\', '/');
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(root + "/Prefabs/VRCLens.prefab");
                if (prefab != null && GetSetup(prefab) != null) return prefab;
            }
            throw new InvalidOperationException("購入済みVRCLensのPrefabs/VRCLens.prefabが見つかりません。");
        }

        internal void SetAvatar(Component setup, VRCAvatarDescriptor avatar) => SetReference(setup, "avatarDescriptor", avatar);

        internal static void SetReference(Component setup, string field, UnityEngine.Object value)
        {
            using (var serialized = new SerializedObject(setup))
            {
                var prop = serialized.FindProperty(field) ?? throw new InvalidOperationException("VRCLensの設定が見つかりません: " + field);
                prop.objectReferenceValue = value;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        internal object Call(Component setup, string method) => Invoke(RequireMethod(SetupType, method), setup);

        private static object Invoke(MethodInfo method, object target)
        {
            try { return method.Invoke(target, null); }
            catch (TargetInvocationException e) { throw new InvalidOperationException("VRCLensの処理に失敗しました: " + method.Name, e.InnerException ?? e); }
        }

        internal void Apply(Component setup)
        {
            UnityEditor.Editor inspector = null;
            var selection = Selection.objects;
            try
            {
                Call(setup, "UpdateCustomRes");
                Call(setup, "RefreshCopyFolderPath");
                Call(setup, "RefreshAssetReserializeList");
                // The vendor inspector prepares the rig, camera model and preview exactly as its Apply button does.
                inspector = UnityEditor.Editor.CreateEditor(setup, inspectorType);
                Invoke(RequireMethod(inspectorType, "ApplySetup"), inspector);
                Call(setup, "AppendAnimationSetup");
                if (!(bool)Call(setup, "IsSetupErrorFree"))
                    throw new InvalidOperationException("VRCLensがセットアップエラーを報告しました。Consoleを確認してください。");
                Call(setup, "ReserializeAssetList");
            }
            finally
            {
                if (inspector != null) UnityEngine.Object.DestroyImmediate(inspector);
                Selection.objects = selection;
                EditorUtility.ClearProgressBar();
            }
        }

        internal void RefreshPreview(Component setup)
        {
            var active = setup.gameObject.activeSelf;
            var selection = Selection.objects;
            var inspector = UnityEditor.Editor.CreateEditor(setup, inspectorType);
            try
            {
                inspectorType.GetMethod("ChangeCameraModel", Methods)?.Invoke(inspector, new object[] { false, Quaternion.identity });
                inspectorType.GetMethod("EnforceInspectorAdjustments", Methods)?.Invoke(inspector, new object[] { true });
                Call(setup, "UpdateCustomRes");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(inspector);
                setup.gameObject.SetActive(active);
                Selection.objects = selection;
            }
        }
    }
}
