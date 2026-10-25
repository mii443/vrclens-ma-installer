using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Micro.VRCLensMA.Editor;
using nadena.dev.ndmf;
using nadena.dev.ndmf.platform;
using nadena.dev.modular_avatar.core;
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
    // Local integration tests consume a user-supplied humanoid prefab; no avatar or paid asset is shipped.
    public static partial class SmokeTests
    {
        private const string Temp = "Assets/VRCLensMAInstaller_TestOutput";
        private static int checks;
        private const string RequestPath = "Library/VRCLensMAInstaller.test-request.txt";
        private const string ResultPath = "Logs/vrclens-ma-live-results.txt";

        [InitializeOnLoadMethod]
        private static void RegisterLocalRunner()
        {
            if (Application.isBatchMode) return;
            EditorApplication.update += PollRequest;
        }

        private static void PollRequest()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(RequestPath)) return;
            Run();
        }

        public static void Run()
        {
            checks = 0;
            File.WriteAllText(ResultPath, "Running\n");
            var previousScene = SceneManager.GetActiveScene();
            var testScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive);
            SceneManager.SetActiveScene(testScene);
            var selection = Selection.objects;
            try
            {
                var args = Environment.GetCommandLineArgs();
                var index = Array.IndexOf(args, "-vrclensTestAvatar");
                string fixture;
                var compatibilityOnly = false;
                var lyraOnly = false;
                var modeIndex = Array.IndexOf(args, "-vrclensTestMode");
                if (modeIndex >= 0 && modeIndex + 1 < args.Length)
                {
                    var mode = args[modeIndex + 1];
                    compatibilityOnly = mode == "compat" || mode == "lyra";
                    lyraOnly = mode == "lyra";
                }
                if (!Application.isBatchMode && File.Exists(RequestPath))
                {
                    var request = File.ReadAllLines(RequestPath);
                    fixture = request[0].Trim();
                    compatibilityOnly = request.Length > 1 && (request[1].Trim() == "compat" || request[1].Trim() == "lyra");
                    lyraOnly = request.Length > 1 && request[1].Trim() == "lyra";
                    File.Delete(RequestPath);
                }
                else if (index >= 0 && index + 1 < args.Length) fixture = args[index + 1];
                else throw new Exception("Pass -vrclensTestAvatar <humanoid prefab path>.");
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fixture);
                Check(prefab != null, "avatar fixture exists");
                if (AssetDatabase.IsValidFolder(Temp)) throw new Exception("Test output folder already exists; inspect it before running again.");
                AssetDatabase.CreateFolder("Assets", "VRCLensMAInstaller_TestOutput");
                var baseline = Snapshot();
                if (compatibilityOnly)
                {
                    EditorSceneManager.SaveScene(testScene, Temp + "/TestHost.unity");
                    if (!lyraOnly) TestPrefabVariants(prefab);
                    TestLyraCustomMenus(prefab);
                }
                else
                {
                TestMenuCycles();
                TestBuild(prefab, false);
                TestBuild(prefab, true);
                TestDisabled(prefab);
                TestDuplicate(prefab);
                TestSdkCallbacks(prefab);
                }
                foreach (var pair in baseline) Check(File.Exists(pair.Key) && Hash(pair.Key) == pair.Value, "unchanged source: " + pair.Key, false);
                File.AppendAllText(ResultPath, $"SOURCE_HASHES_UNCHANGED files={baseline.Count}\n");
                Check(!Directory.GetDirectories("Assets", "NDMF_*", SearchOption.AllDirectories).Any(), "vendor temporary output cleaned");
                Debug.Log($"VRCLENS_MA_TESTS_PASSED checks={checks}");
                File.AppendAllText(ResultPath, $"VRCLENS_MA_TESTS_PASSED checks={checks}\n");
                AssetDatabase.DeleteAsset(Temp);
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                File.AppendAllText(ResultPath, e + "\n");
                if (AssetDatabase.IsValidFolder(Temp)) AssetDatabase.DeleteAsset(Temp);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
            finally
            {
                if (!Application.isBatchMode)
                {
                    SceneManager.SetActiveScene(previousScene);
                    EditorSceneManager.CloseScene(testScene, true);
                }
                Selection.objects = selection;
            }
        }

        private static GameObject Create(GameObject prefab)
        {
            var source = Object.Instantiate(prefab);
            source.name = "Installer Test Avatar";
            var root = new GameObject("VRCLens MA Installer");
            root.transform.SetParent(source.transform, false);
            InstallerAuthoring.Initialize(root.AddComponent<VRCLensInstaller>(), new InstalledVRCLens());
            return source;
        }

        private static void TestBuild(GameObject prefab, bool fullPipeline)
        {
            var source = Create(prefab);
            var installer = source.GetComponentInChildren<VRCLensInstaller>();
            var descriptor = source.GetComponent<VRCAvatarDescriptor>();
            var sourceMenu = descriptor.expressionsMenu;
            var sourceParams = descriptor.expressionParameters;
            var sourceFx = descriptor.baseAnimationLayers.First(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
            var cam = installer.setupObject.transform.Find("CamBase");
            cam.position += new Vector3(0.021f, 0.034f, 0.055f);
            var position = cam.position;
            var custom = new GameObject("Custom sentinel");
            custom.AddComponent<BoxCollider>(); // Keep the sentinel through MA's empty-object collection.
            custom.transform.SetParent(installer.customizations, false);
            var submenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            submenu.controls.Add(new VRCExpressionsMenu.Control { name = "MA sentinel", type = VRCExpressionsMenu.Control.ControlType.Button,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "MA_Test" } });
            var maObject = new GameObject("MA Menu");
            maObject.transform.SetParent(source.transform, false);
            var maMenu = maObject.AddComponent<ModularAvatarMenuInstaller>();
            maMenu.menuToAppend = submenu;
            var clone = Object.Instantiate(source);
            try
            {
                BuildContext context;
                if (fullPipeline)
                {
                    using (new OverrideTemporaryDirectoryScope(Temp + "/Full"))
                        context = AvatarProcessor.ProcessAvatar(clone, AmbientPlatform.DefaultPlatform);
                }
                else
                {
                    context = new BuildContext(clone, Temp + "/Direct");
                    InstallerBuild.Run(context);
                    context.Serialize();
                }
                Check(context.Successful, "NDMF context succeeded");
                Check(clone.transform.Find("VRCLens/WorldA/AnimDummy") != null, "installed rig exists");
                Check(clone.transform.Find("VRCLens/Custom sentinel") != null, "customization moved to VRCLens");
                var installedCam = clone.transform.Find("VRCLens/WorldC/CamPickup/CamBase");
                Check(installedCam != null && Vector3.Distance(installedCam.position, position) < 0.0001f, "camera placement preserved");
                Check(clone.GetComponentsInChildren<VRCLensInstaller>(true).Length == 0, "installer removed from build");
                var built = clone.GetComponent<VRCAvatarDescriptor>();
                Check(built.expressionsMenu != sourceMenu, "menu isolated");
                Check(built.expressionParameters != sourceParams, "parameters isolated");
                var fx = built.baseAnimationLayers.First(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController as AnimatorController;
                Check(fx != null && fx != sourceFx && fx.layers.Any(l => l.name.StartsWith("vCNT_")), "FX merged and isolated");
                Check(built.expressionParameters.parameters.Any(p => p.name == "VRCLFeatureToggle"), "VRCLens parameters added");
                if (fullPipeline) Check(built.expressionsMenu.controls.Any(c => c.name == "MA sentinel"), "MA menu merged after VRCLens");
                Check(source.transform.Find("VRCLens") == null && installer.setupObject.transform.Find("CamBase") != null, "source scene unchanged");
                Check(descriptor.expressionsMenu == sourceMenu && descriptor.expressionParameters == sourceParams, "source descriptor references unchanged");
                Check(clone.name == source.name + "(Clone)", "avatar name restored");
                foreach (var dependency in EditorUtility.CollectDependencies(new Object[] { clone }))
                    Check(!AssetDatabase.GetAssetPath(dependency).Contains("/User/NDMF_"), "no references to removed temporary files", false);
            }
            finally
            {
                Object.DestroyImmediate(clone);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(submenu);
            }
        }

        private static void TestDisabled(GameObject prefab)
        {
            var source = Create(prefab);
            try
            {
                source.GetComponentInChildren<VRCLensInstaller>().enabled = false;
                var context = new BuildContext(source, null);
                InstallerBuild.Run(context);
                Check(source.transform.Find("VRCLens") == null && source.GetComponentsInChildren<VRCLensInstaller>(true).Length == 0, "disabled installer stripped without installing");
            }
            finally { Object.DestroyImmediate(source); }
        }

        private static void TestDuplicate(GameObject prefab)
        {
            var source = Create(prefab);
            try
            {
                var extra = new GameObject("Second installer");
                extra.transform.SetParent(source.transform, false);
                extra.AddComponent<VRCLensInstaller>();
                var context = new BuildContext(source, null);
                var failed = false;
                try { InstallerBuild.Run(context); } catch (InvalidOperationException) { failed = true; }
                Check(failed && source.transform.Find("VRCLens") == null, "duplicate installers fail before mutation");
            }
            finally { Object.DestroyImmediate(source); }
        }

        private static void TestMenuCycles()
        {
            var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            menu.controls.Add(new VRCExpressionsMenu.Control { name = "cycle", type = VRCExpressionsMenu.Control.ControlType.SubMenu, subMenu = menu });
            var graph = new AssetGraph(o => o is VRCExpressionsMenu);
            var copy = (VRCExpressionsMenu)graph.Clone(menu);
            Check(copy != menu && copy.controls[0].subMenu == copy, "cyclic menu cloned without recursion or source mutation");
            Object.DestroyImmediate(copy);
            Object.DestroyImmediate(menu);
        }

        private static void TestSdkCallbacks(GameObject prefab)
        {
            var source = Create(prefab);
            SkipWriteDefaultsPromptOnFixture(source);
            var installer = source.GetComponentInChildren<VRCLensInstaller>();
            var originalMenu = source.GetComponent<VRCAvatarDescriptor>().expressionsMenu;
            var customPaths = new[]
            {
                "Assets/VRCLens_Custom/[Camera] SmoothZoom.prefab",
                "Assets/VRCLens_Custom/[Utility] FixAvatarDrop.prefab",
                "Assets/VRCLens_Custom/[Utility] MenuFavorites.prefab"
            };
            foreach (var path in customPaths)
            {
                var custom = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (custom == null) throw new Exception("SDK integration fixture is missing: " + path);
                Object.Instantiate(custom, installer.customizations);
            }
            var clone = Object.Instantiate(source);
            try
            {
                using (new OverrideTemporaryDirectoryScope(Temp + "/SDK"))
                using (new VendorTempScope())
                    Check(VRCBuildPipelineCallbacks.OnPreprocessAvatar(clone), "actual SDK preprocessor pipeline succeeded with Custom + VRCFury + MenuFavorites");
                var built = clone.GetComponent<VRCAvatarDescriptor>();
                var fx = built.baseAnimationLayers.First(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController as AnimatorController;
                Check(clone.transform.Find("VRCLens/WorldA/AnimDummy") != null && fx != null, "SDK output contains VRCLens rig and FX");
                Check(fx.parameters.Any(p => p.name.Contains("VRCLZoomRadial")), "custom SDK output retains zoom parameter");
                Check(built.expressionsMenu != originalMenu && source.transform.Find("VRCLens") == null, "SDK pipeline leaves authoring avatar unchanged");
                Check(clone.GetComponentsInChildren<VRCLensInstaller>(true).Length == 0, "SDK installer removed");
                File.AppendAllText(ResultPath, "SDK_FX_LAYERS " + string.Join(", ", fx.layers.Select(l => l.name)) + "\n");
            }
            finally
            {
                Object.DestroyImmediate(clone);
                Object.DestroyImmediate(source);
            }
        }

        private static Dictionary<string, string> Snapshot() => Directory.GetFiles("Assets", "*", SearchOption.AllDirectories)
            .Where(p => !p.Replace('\\', '/').StartsWith(Temp + "/", StringComparison.Ordinal)).ToDictionary(p => p, Hash);
        private static string Hash(string path) { using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path))); }
        private static void Check(bool result, string message, bool log = true)
        {
            if (!result) throw new Exception("TEST FAILED: " + message);
            checks++;
            if (log)
            {
                Debug.Log("TEST PASS: " + message);
                File.AppendAllText(ResultPath, "TEST PASS: " + message + "\n");
            }
        }
    }
}
