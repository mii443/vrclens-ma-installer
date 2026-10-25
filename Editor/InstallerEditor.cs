using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace Micro.VRCLensMA.Editor
{
    [CustomEditor(typeof(VRCLensInstaller))]
    internal sealed class InstallerEditor : UnityEditor.Editor
    {
        private static readonly Dictionary<string, string[]> Choices = new Dictionary<string, string[]>
        {
            { "sensorResIndex", new[] { "1920 × 1080", "2560 × 1440", "3840 × 2160", "7680 × 4320", "Custom" } },
            { "msaaIndex", new[] { "Off", "Auto", "2x", "4x", "8x" } },
            { "streamModeIndex", new[] { "Normal", "DirectCast" } },
            { "afModeIndex", new[] { "Normal", "Avatar AF", "Selfie AF" } },
            { "apertureIndex", new[] { "F1.4", "F2.0", "F2.8", "F4.0", "F5.6", "F8.0", "F11", "F16", "F22" } },
            { "focalIndex", new[] { "12mm", "17mm", "24mm", "35mm", "50mm", "70mm", "85mm", "135mm", "200mm", "400mm", "1200mm" } },
            { "tonemapIndex", new[] { "Off", "ACES", "EVILS", "HLG", "Neutral" } },
            { "sensorSizeIndex", new[] { "Full Size", "APS-H", "APS-C 1.5", "APS-C 1.6", "MFT", "1 inch" } }
        };

        public override void OnInspectorGUI()
        {
            var installer = (VRCLensInstaller)target;
            EditorGUILayout.HelpBox("アップロード時にビルド用コピーへVRCLensを適用します。元のメニュー・FXは変更しません。設定用VRCLensのApplyボタンは押さず、この画面で設定してください。", MessageType.Info);
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("setupObject"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("customizations"));
            serializedObject.ApplyModifiedProperties();
            var avatar = installer.GetComponentInParent<VRCAvatarDescriptor>();
            if (avatar == null || installer.transform.parent != avatar.transform)
            {
                EditorGUILayout.HelpBox("このPrefabをアバターの直下に配置してください。", MessageType.Warning);
                return;
            }
            try
            {
                var api = new InstalledVRCLens();
                if (installer.setupObject == null)
                {
                    if (GUILayout.Button("設定用VRCLensを作成")) InstallerAuthoring.Initialize(installer, api);
                    return;
                }
                InstallerBuild.ValidatePlacement(installer, avatar);
                var setup = api.GetSetup(installer.setupObject);
                if (setup == null) throw new InvalidOperationException("Setup ObjectにVRCLensSetupがありません。");
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("VRCLens Settings", EditorStyles.boldLabel);
                using (var settings = new SerializedObject(setup))
                {
                    settings.Update();
                    var prop = settings.GetIterator();
                    var enterChildren = true;
                    while (prop.NextVisible(enterChildren))
                    {
                        enterChildren = false;
                        if (prop.name == "m_Script" || prop.name == "avatarDescriptor" || prop.name == "maxBlurIndex") continue;
                        if (Choices.TryGetValue(prop.name, out var choices))
                            prop.intValue = EditorGUILayout.Popup(prop.displayName, prop.intValue, choices);
                        else EditorGUILayout.PropertyField(prop, true);
                    }
                    settings.ApplyModifiedProperties();
                }
                if (GUILayout.Button("設定をシーンのプレビューに反映"))
                {
                    Undo.RegisterFullObjectHierarchyUndo(installer.gameObject, "Preview VRCLens settings");
                    api.SetAvatar(setup, avatar);
                    api.RefreshPreview(setup);
                    EditorUtility.SetDirty(setup);
                    InstallerAuthoring.RecordPrefabOverrides(setup.gameObject);
                }
                if (LyraIntegration.Available && GUILayout.Button("Lyra Menu Manager用Proxyを設定"))
                    LyraIntegration.ConfigureProxy(installer, setup);
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("位置調整", EditorStyles.boldLabel);
                foreach (var name in new[] { "HeadMountPoint", "SelfieFocusPoint", "FocusObject", "CamBase" })
                    if (GUILayout.Button(name + " を選択")) Selection.activeTransform = setup.transform.Find(name);
                if (GUILayout.Button("Customizations を選択")) Selection.activeTransform = installer.customizations;
                EditorGUILayout.HelpBox("Customizations直下に任意のVRCLens_Custom Prefabを置いてください。ビルド時に生成されたVRCLens直下へ移動します。", MessageType.None);
            }
            catch (Exception e) { EditorGUILayout.HelpBox(e.Message, MessageType.Error); }
        }
    }

    internal static class InstallerAuthoring
    {
        [MenuItem("GameObject/VRCLens MA/Installerを追加", false, 30)]
        private static void CreateMenu(MenuCommand command)
        {
            var selected = command.context as GameObject ?? Selection.activeGameObject;
            var avatar = selected != null ? selected.GetComponentInParent<VRCAvatarDescriptor>() : null;
            if (avatar == null) throw new InvalidOperationException("Hierarchyでアバターを選択してください。");
            if (avatar.GetComponentInChildren<VRCLensInstaller>(true) != null)
                throw new InvalidOperationException("このアバターには既にInstallerがあります。");
            var api = new InstalledVRCLens();
            var go = new GameObject("VRCLens MA Installer");
            Undo.RegisterCreatedObjectUndo(go, "Add VRCLens Installer");
            go.transform.SetParent(avatar.transform, false);
            var installer = Undo.AddComponent<VRCLensInstaller>(go);
            Initialize(installer, api);
            Selection.activeGameObject = go;
        }

        internal static void Initialize(VRCLensInstaller installer, InstalledVRCLens api)
        {
            if (installer.setupObject != null) throw new InvalidOperationException("設定用VRCLensは既に存在します。");
            var avatar = installer.GetComponentInParent<VRCAvatarDescriptor>();
            if (avatar == null || installer.transform.parent != avatar.transform)
                throw new InvalidOperationException("Installerをアバター直下に配置してください。");
            InstallerBuild.ValidateAvatar(avatar);
            var prefab = api.FindPrefab();
            Undo.RecordObject(installer, "Initialize VRCLens Installer");
            var setupObject = Object.Instantiate(prefab, installer.transform);
            setupObject.name = "VRCLens Settings";
            Undo.RegisterCreatedObjectUndo(setupObject, "Create VRCLens settings");
            installer.setupObject = setupObject;
            if (installer.customizations == null)
            {
                var customs = new GameObject("Customizations");
                Undo.RegisterCreatedObjectUndo(customs, "Create customizations container");
                customs.transform.SetParent(installer.transform, false);
                installer.customizations = customs.transform;
            }
            var setup = api.GetSetup(setupObject);
            api.SetAvatar(setup, avatar);
            api.Call(setup, "AutoArrangeSetup");
            api.RefreshPreview(setup);
            EditorUtility.SetDirty(installer);
            PrefabUtility.RecordPrefabInstancePropertyModifications(installer);
        }

        internal static void RecordPrefabOverrides(GameObject root)
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (!PrefabUtility.IsPartOfPrefabInstance(transform)) continue;
                PrefabUtility.RecordPrefabInstancePropertyModifications(transform.gameObject);
                foreach (var component in transform.GetComponents<Component>())
                    if (component != null) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            }
        }
    }
}
