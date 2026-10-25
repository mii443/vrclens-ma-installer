using System;
using System.IO;
using System.Linq;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using Object = UnityEngine.Object;

[assembly: ExportsPlugin(typeof(Micro.VRCLensMA.Editor.InstallerPlugin))]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("VRCLensMAInstaller.Tests")]

namespace Micro.VRCLensMA.Editor
{
    public sealed class InstallerPlugin : Plugin<InstallerPlugin>
    {
        public override string QualifiedName => "dev.mii.vrclens-ma-installer";
        public override string DisplayName => "VRCLens MA Installer";

        protected override void Configure()
        {
            // Must precede MA's controller virtualization and platform configuration extraction.
            InPhase(BuildPhase.FirstChance).Run("Apply VRCLens to build copy", InstallerBuild.Run);
            InPhase(BuildPhase.Transforming).AfterPlugin("nadena.dev.modular-avatar").BeforePlugin("lyra.menu-manager")
                .Run("Remember customization menu destinations", CustomizationMenuRouting.Capture);
            InPhase(BuildPhase.Transforming).AfterPlugin("lyra.menu-manager")
                .Run("Follow moved customization menus", CustomizationMenuRouting.Retarget);
        }
    }

    internal static class InstallerBuild
    {
        internal static void Run(BuildContext context)
        {
            var root = context.AvatarRootObject;
            var installers = root.GetComponentsInChildren<VRCLensInstaller>(true);
            var active = installers.Where(i => i.enabled && i.gameObject.activeInHierarchy && !HasEditorOnlyAncestor(i.transform, root.transform)).ToArray();
            if (active.Length > 1) throw new InvalidOperationException("VRCLens Installerはアバターごとに1つだけ有効にしてください。");
            // Disabled authoring rigs must not leak cameras or vendor setup components into the upload.
            foreach (var installer in installers.Except(active))
            {
                if (installer.gameObject == root) throw new InvalidOperationException("Installerはアバター本体ではなく、その直下に置いてください。");
                Object.DestroyImmediate(installer.gameObject);
            }
            if (active.Length == 0) return;
            Install(context, active[0]);
        }

        private static bool HasEditorOnlyAncestor(Transform t, Transform avatar)
        {
            for (; t != null && t != avatar; t = t.parent)
                if (t.CompareTag("EditorOnly")) return true;
            return false;
        }

        private static void Install(BuildContext context, VRCLensInstaller installer)
        {
            var avatar = context.AvatarRootObject.GetComponent<VRCAvatarDescriptor>();
            ValidatePlacement(installer, avatar);
            var api = new InstalledVRCLens();
            var setup = api.GetSetup(installer.setupObject);
            if (setup == null) throw new InvalidOperationException("Installerの「設定用VRCLensを作成」を実行してください。");
            // The vendor inspector deactivates its authoring rig on deselection.
            setup.gameObject.SetActive(true);
            foreach (var name in new[] { "HeadMountPoint", "SelfieFocusPoint", "FocusObject", "CamBase", "VRCLens" })
                if (setup.transform.Find(name) == null) throw new InvalidOperationException("設定用VRCLensが壊れています。必要なオブジェクト: " + name);

            api.SetAvatar(setup, avatar); // Always use the build copy, regardless of the serialized target.
            ValidateAvatar(avatar);
            ValidateSubMenu(setup, avatar);
            if (!(bool)api.Call(setup, "HasEnoughMenuControls")) throw new InvalidOperationException("VRCLensの追加先メニューに空きがありません。サブメニューを指定してください。");
            if (!(bool)api.Call(setup, "HasEnoughParameters")) throw new InvalidOperationException("VRCLensに必要な同期パラメーター容量が足りません。");

            var originalName = avatar.name;
            var userPath = api.RootPath(setup) + "/User";
            var createdUser = !AssetDatabase.IsValidFolder(userPath);
            var buildName = "NDMF_" + Guid.NewGuid().ToString("N");
            var workPath = userPath + "/" + buildName;
            if (Directory.Exists(workPath) || AssetDatabase.IsValidFolder(workPath)) throw new IOException("一時フォルダーが既に存在します。");
            if (createdUser) AssetDatabase.CreateFolder(api.RootPath(setup), "User");
            AssetDatabase.CreateFolder(userPath, buildName);
            if (!AssetDatabase.IsValidFolder(workPath)) throw new IOException("VRCLensの一時フォルダーを作成できません。");

            try
            {
                // VRCLens derives its output directory from the avatar name on every call.
                // A unique name confines all vendor writes to this invocation's disposable directory.
                avatar.name = buildName;
                IsolateInputs(avatar, workPath);
                api.Apply(setup);
                var installed = avatar.transform.Find("VRCLens");
                if (installed == null || installed.Find("WorldA/AnimDummy") == null)
                    throw new InvalidOperationException("VRCLensの生成が完了しませんでした。");

                if (installer.customizations != null && !installer.customizations.CompareTag("EditorOnly"))
                {
                    // Snapshot children: custom components retain their object references and local settings.
                    foreach (var child in installer.customizations.Cast<Transform>().ToArray())
                    {
                        if (!installer.customizations.gameObject.activeInHierarchy) child.gameObject.SetActive(false);
                        child.SetParent(installed, true);
                    }
                }

                // ApplySetup restores an authoring rig outside the avatar. Dispose only our own rig.
                Object.DestroyImmediate(setup.gameObject);
                Object.DestroyImmediate(installer.gameObject);

                // Transfer every referenced generated asset (and its subassets) to NDMF ownership
                // before deleting vendor files. Original materials/clips remain shared, read-only.
                var outputs = new AssetGraph(o => AssetDatabase.GetAssetPath(o).StartsWith(workPath + "/", StringComparison.Ordinal));
                outputs.RemapHierarchy(avatar.gameObject, true);
                context.AssetSaver.SaveAssets(outputs.Copies.Values);
                CustomizationMenuRouting.MarkInstalled(avatar.gameObject, installed);
                AssetDatabase.SaveAssets();
            }
            finally
            {
                avatar.name = originalName;
                if (setup != null && !setup.transform.IsChildOf(avatar.transform)) Object.DestroyImmediate(setup.gameObject);
                // workPath is unique, created above, never a user's existing folder.
                AssetDatabase.DeleteAsset(workPath);
                if (createdUser && AssetDatabase.IsValidFolder(userPath) && !Directory.EnumerateFileSystemEntries(userPath).Any())
                    AssetDatabase.DeleteAsset(userPath);
                EditorUtility.ClearProgressBar();
            }
        }

        internal static void ValidatePlacement(VRCLensInstaller installer, VRCAvatarDescriptor avatar)
        {
            if (avatar == null || installer.transform.parent != avatar.transform)
                throw new InvalidOperationException("VRCLens Installerをアバターの直下に配置してください。");
            if (installer.setupObject == null || installer.setupObject.transform.parent != installer.transform)
                throw new InvalidOperationException("設定用VRCLensはInstallerの直下に必要です。");
            if (installer.customizations != null && (installer.customizations.parent != installer.transform || installer.customizations.gameObject == installer.setupObject))
                throw new InvalidOperationException("Customizationsは設定用VRCLensと別の、Installer直下のオブジェクトを指定してください。");
            if (avatar.transform.Find("VRCLens") != null)
                throw new InvalidOperationException("通常Apply済みのVRCLensが残っています。未導入のアバターで使用してください。");
            foreach (Transform child in installer.transform)
                if (child.gameObject != installer.setupObject && child != installer.customizations)
                    throw new InvalidOperationException("カスタマイズPrefabはCustomizations直下に置いてください: " + child.name);
        }

        internal static void ValidateAvatar(VRCAvatarDescriptor avatar)
        {
            var animator = avatar.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.isHuman)
                throw new InvalidOperationException("有効なHumanoid Animatorを持つアバターが必要です。");
            foreach (var bone in new[] { HumanBodyBones.Hips, HumanBodyBones.Head, HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm })
                if (animator.GetBoneTransform(bone) == null) throw new InvalidOperationException("必要なHumanoidボーンがありません: " + bone);
            var scale = avatar.transform.lossyScale;
            if (scale.x <= 0 || !Mathf.Approximately(scale.x, scale.y) || !Mathf.Approximately(scale.y, scale.z))
                throw new InvalidOperationException("アバターのスケールは正の均等スケールにしてください。");
            foreach (var bone in new[] { HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.Head })
                foreach (var name in new[] { "PickupA", "PickupB", "PickupC" })
                    if (animator.GetBoneTransform(bone).Find(name) != null)
                        throw new InvalidOperationException("通常Applyの残存物または同名オブジェクトがあります: " + name);
        }

        private static void ValidateSubMenu(Component setup, VRCAvatarDescriptor avatar)
        {
            using (var so = new SerializedObject(setup))
            {
                var target = so.FindProperty("targetSubMenu").objectReferenceValue;
                if (target == null) return;
                var visited = new System.Collections.Generic.HashSet<VRCExpressionsMenu>();
                bool Visit(VRCExpressionsMenu menu)
                {
                    if (menu == null || !visited.Add(menu)) return false;
                    return menu == target || menu.controls.Any(c => Visit(c.subMenu));
                }
                if (!Visit(avatar.expressionsMenu)) throw new InvalidOperationException("追加先サブメニューはアバターのExpressions Menuから辿れるものを指定してください。");
            }
        }

        private static void IsolateInputs(VRCAvatarDescriptor avatar, string workPath)
        {
            // Clone both menu graphs and all playable layers; VRCLens also repairs the Action layer.
            var graph = new AssetGraph(o => o is RuntimeAnimatorController || o is AnimatorStateMachine || o is AnimatorState
                || o is AnimatorTransitionBase || o is StateMachineBehaviour || o is Motion || o is AvatarMask
                || o is VRCExpressionsMenu || o is VRCExpressionParameters);
            graph.Clone(avatar.expressionsMenu);
            graph.Clone(avatar.expressionParameters);
            foreach (var layer in (avatar.baseAnimationLayers ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>())
                         .Concat(avatar.specialAnimationLayers ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>()))
            {
                if (layer.animatorController is AnimatorOverrideController)
                    throw new InvalidOperationException("AnimatorOverrideControllerには未対応です。通常のAnimatorControllerを指定してください。");
                graph.Clone(layer.animatorController);
            }
            // Also remap MA menu targets and other references to these same assets on the build copy.
            graph.RemapHierarchy(avatar.gameObject, false);
            var index = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var copy in graph.Copies.Values)
                {
                    // Use neutral names to avoid VRCLens's legacy _VRCLensFX_Copy recovery branch.
                    var filename = "Input_" + index++;
                    if (copy is RuntimeAnimatorController) copy.name = filename;
                    var extension = copy is AnimatorController ? ".controller" : copy is AnimationClip ? ".anim" : ".asset";
                    AssetDatabase.CreateAsset(copy, workPath + "/" + filename + extension);
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
        }
    }
}
