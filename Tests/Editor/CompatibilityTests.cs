using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lyra;
using Micro.VRCLensMA.Editor;
using nadena.dev.ndmf;
using nadena.dev.ndmf.platform;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase.Editor.BuildPipeline;
using Object = UnityEngine.Object;

namespace Micro.VRCLensMA.Tests
{
    public static partial class SmokeTests
    {
        private const string Shell = "Packages/dev.mii.vrclens-ma-installer/Prefabs/VRCLens MA Installer.prefab";

        private static void TestPrefabVariants(GameObject prefab)
        {
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            GameObject clone = null;
            try
            {
                var shell = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Shell));
                var shellVariant = PrefabUtility.SaveAsPrefabAsset(shell, Temp + "/InstallerVariant.prefab");
                Check(PrefabUtility.GetPrefabAssetType(shellVariant) == PrefabAssetType.Variant, "installer fixture is a real prefab variant");
                Object.DestroyImmediate(shell);
                var source = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                var wrapper = (GameObject)PrefabUtility.InstantiatePrefab(shellVariant, source.transform);
                var installer = wrapper.GetComponent<VRCLensInstaller>();
                InstallerAuthoring.Initialize(installer, new InstalledVRCLens());
                var names = new[] { "HeadMountPoint", "SelfieFocusPoint", "FocusObject", "CamBase" };
                var positions = new Dictionary<string, Vector3>();
                foreach (var name in names)
                {
                    var t = installer.setupObject.transform.Find(name);
                    t.localPosition += new Vector3(0.013f, 0.024f, 0.035f);
                    positions[name] = t.localPosition;
                }
                var settings = new SerializedObject(new InstalledVRCLens().GetSetup(installer.setupObject));
                settings.FindProperty("focalIndex").intValue = 5;
                settings.ApplyModifiedPropertiesWithoutUndo();
                var sentinel = new GameObject("Variant customization");
                sentinel.AddComponent<BoxCollider>();
                sentinel.transform.SetParent(installer.customizations, false);
                // Save and reload before checking: in-memory assignments alone do not test overrides.
                EditorSceneManager.SaveScene(scene, Temp + "/VariantScene.unity");
                EditorSceneManager.CloseScene(scene, true);
                scene = EditorSceneManager.OpenScene(Temp + "/VariantScene.unity", OpenSceneMode.Additive);
                SceneManager.SetActiveScene(scene);
                source = scene.GetRootGameObjects().Single();
                installer = source.GetComponentInChildren<VRCLensInstaller>(true);
                Check(installer.setupObject != null, "installer variant setup reference survives scene reload");
                Check(installer.customizations != null, "installer variant customization reference survives scene reload");
                foreach (var name in names)
                    Check(Vector3.Distance(installer.setupObject.transform.Find(name).localPosition, positions[name]) < 0.00001f, name + " override persists");
                Check(new SerializedObject(new InstalledVRCLens().GetSetup(installer.setupObject)).FindProperty("focalIndex").intValue == 5, "VRCLens setting persists on nested installer variant");
                var avatarVariant = PrefabUtility.SaveAsPrefabAsset(source, Temp + "/AvatarVariant.prefab");
                Check(PrefabUtility.GetPrefabAssetType(avatarVariant) == PrefabAssetType.Variant, "avatar fixture is a real prefab variant");
                clone = Object.Instantiate(avatarVariant);
                var originalCam = clone.GetComponentInChildren<VRCLensInstaller>(true).setupObject.transform.Find("CamBase").position;
                BuildContext context;
                using (new OverrideTemporaryDirectoryScope(Temp + "/VariantBuild"))
                    context = AvatarProcessor.ProcessAvatar(clone, AmbientPlatform.DefaultPlatform);
                Check(context.Successful, "avatar variant with nested installer variant passes full NDMF pipeline");
                Check(clone.transform.Find("VRCLens/Variant customization") != null, "variant customization survives build");
                Check(Vector3.Distance(clone.transform.Find("VRCLens/WorldC/CamPickup/CamBase").position, originalCam) < 0.0001f, "variant camera placement preserved in build");
            }
            finally
            {
                if (clone != null) Object.DestroyImmediate(clone);
                SceneManager.SetActiveScene(previous);
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void TestLyraCustomMenus(GameObject prefab)
        {
            var source = Create(prefab);
            SkipWriteDefaultsPromptOnFixture(source);
            var installer = source.GetComponentInChildren<VRCLensInstaller>();
            foreach (var path in new[] { "Assets/VRCLens_Custom/[Utility] MenuExtra.prefab", "Assets/VRCLens_Custom/[Drone] MoveDroneVertical.prefab", "Assets/VRCLens_Custom/[Utility] MenuFavorites.prefab", "Assets/VRCLens_Custom/[Camera] SmoothZoom.prefab", "Assets/VRCLens_Custom/[Camera] FarClipPlane.prefab" })
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Check(asset != null, "custom menu fixture exists: " + Path.GetFileName(path));
                Object.Instantiate(asset, installer.customizations);
            }
            var proxy = (MenuManagerItemProxy)LyraIntegration.ConfigureProxy(installer, new InstalledVRCLens().GetSetup(installer.setupObject));
            Check(proxy.menuItemName == " VRCLens " && proxy.controlType == VRCExpressionsMenu.Control.ControlType.SubMenu, "optional Lyra integration creates correctly configured Item Proxy");
            var descriptor = source.GetComponent<VRCAvatarDescriptor>();
            var originalMenu = descriptor.expressionsMenu;
            descriptor.expressionsMenu = null;
            LyraIntegration.ConfigureProxy(installer, new InstalledVRCLens().GetSetup(installer.setupObject));
            Check(proxy.parentFolderPath.Count == 0, "Lyra proxy also supports avatars without an existing expression menu");
            descriptor.expressionsMenu = originalMenu;
            var layout = source.AddComponent<MenuLayoutData>();
            layout.BaseLayout = ScriptableObject.CreateInstance<MenuLayoutDataAsset>();
            AssetDatabase.CreateAsset(layout.BaseLayout, Temp + "/LyraLayout.asset");
            // A saved scene gives the proxy the same stable GlobalObjectId that the Lyra UI stores.
            EditorSceneManager.SaveScene(source.scene, Temp + "/LyraScene.unity");
            layout.BaseLayout.Items.Add(new MenuLayoutData.ItemLayout
            { Key = "camera-folder", Type = "SubMenu:Cameras:__custom__:1.00:0", ParentPath = "", Order = 0, IsSubMenu = true, DisplayName = "Cameras" });
            layout.BaseLayout.Items.Add(new MenuLayoutData.ItemLayout
            { Key = "lens-proxy", Type = "SubMenu: VRCLens ::0.00:0", ParentPath = "camera-folder", Order = 0, IsSubMenu = true,
                DisplayName = "My Lens", SourceObjId = GlobalObjectId.GetGlobalObjectIdSlow(installer.gameObject).ToString() });
            EditorUtility.SetDirty(layout.BaseLayout);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(source.scene);
            var layoutHash = Hash(Temp + "/LyraLayout.asset");
            var sourceMenu = source.GetComponent<VRCAvatarDescriptor>().expressionsMenu;
            var clone = Object.Instantiate(source);
            try
            {
                using (new OverrideTemporaryDirectoryScope(Temp + "/LyraSDK"))
                using (new VendorTempScope())
                    Check(VRCBuildPipelineCallbacks.OnPreprocessAvatar(clone), "Lyra + VRCLens_Custom actual SDK preprocess succeeds");
                var menu = clone.GetComponent<VRCAvatarDescriptor>().expressionsMenu;
                DumpMenu(menu, "", new HashSet<VRCExpressionsMenu>());
                var folder = menu.controls.SingleOrDefault(c => c.name == "Cameras")?.subMenu;
                Check(folder != null, "Lyra creates destination submenu");
                var lens = folder.controls.SingleOrDefault(c => c.name == "My Lens")?.subMenu;
                Check(lens != null, "Lyra moves and renames VRCLens via Item Proxy");
                Check(lens.controls.Any(c => c.name.Trim() == "Custom"), "custom menus stay under moved VRCLens");
                Check(lens.controls.First(c => c.name.Trim() == "Custom").subMenu.controls.Any(c => c.name == "Move Drone Vertical"), "MoveDroneVertical is inside moved VRCLens/Custom");
                Check(HasParameter(lens, "VRCL_Custom/FarClipPlane", new HashSet<VRCExpressionsMenu>()), "VRCFury Toggle control follows moved VRCLens alongside FullController menus");
                Check(!menu.controls.Any(c => c.name.Trim() == "VRCLens" || c.name.Trim() == "Custom"), "no stray VRCLens or Custom menu at avatar root");
                Check(layoutHash == Hash(Temp + "/LyraLayout.asset"), "Lyra layout asset unchanged");
                Check(source.GetComponent<VRCAvatarDescriptor>().expressionsMenu == sourceMenu && source.transform.Find("VRCLens") == null, "Lyra build preserves source avatar");
            }
            finally { Object.DestroyImmediate(clone); Object.DestroyImmediate(source); }
        }

        private static bool HasParameter(VRCExpressionsMenu menu, string parameter, HashSet<VRCExpressionsMenu> visited)
        {
            if (menu == null || !visited.Add(menu)) return false;
            return menu.controls.Any(c => c.parameter?.name == parameter || (c.subParameters != null && c.subParameters.Any(p => p?.name == parameter))
                || HasParameter(c.subMenu, parameter, visited));
        }

        private static void DumpMenu(VRCExpressionsMenu menu, string parent, HashSet<VRCExpressionsMenu> visited)
        {
            if (menu == null || !visited.Add(menu)) return;
            foreach (var control in menu.controls)
            {
                var path = parent + "/" + control.name;
                File.AppendAllText(ResultPath, "MENU " + path + "\n");
                DumpMenu(control.subMenu, path, visited);
            }
        }

        private static void SkipWriteDefaultsPromptOnFixture(GameObject source)
        {
            var furyType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("VF.Model.VRCFury")).First(t => t != null);
            var featureType = furyType.Assembly.GetType("VF.Model.Feature.FixWriteDefaults", true);
            var feature = Activator.CreateInstance(featureType);
            var mode = featureType.GetField("mode");
            mode.SetValue(feature, Enum.Parse(mode.FieldType, "Disabled"));
            var component = source.AddComponent(furyType);
            using (var serialized = new SerializedObject(component))
            {
                serialized.FindProperty("content").managedReferenceValue = feature;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private sealed class VendorTempScope : IDisposable
        {
            private readonly System.Reflection.FieldInfo field;
            private readonly object original;
            internal VendorTempScope()
            {
                var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("VRCLensCustom.AvatarBuildHook")).First(t => t != null);
                field = type.GetField("TempDir");
                original = field.GetValue(null);
                // The vendor normally replaces this disposable cache on every build. Keep
                // integration-test output in our own folder so existing project caches survive.
                field.SetValue(null, Temp + "/VendorTemp");
            }
            public void Dispose() => field.SetValue(null, original);
        }
    }
}
