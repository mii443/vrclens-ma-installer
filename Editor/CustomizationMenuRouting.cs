using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Micro.VRCLensMA.Editor
{
    // VRCFury merges FullController menus after NDMF's Transforming phase. Keep the
    // merge destinations attached to the same menus when Lyra moves or renames them.
    // Only serialized configuration on our build copy is changed; no vendor code is bundled.
    internal static class CustomizationMenuRouting
    {
        private sealed class InstalledRoot { internal Transform Root; }
        internal sealed class Route
        {
            internal Component Component;
            internal string PropertyPath;
            internal string OriginalPrefix;
            internal VRCExpressionsMenu.Control Anchor;
            internal string[] Suffix;
        }
        internal sealed class State { internal readonly List<Route> Routes = new List<Route>(); }

        // The early MenuFavorites SDK hook and NDMF use separate BuildContexts for the
        // same clone. Weak keys connect those contexts without retaining completed builds.
        private static readonly ConditionalWeakTable<GameObject, InstalledRoot> Installed = new ConditionalWeakTable<GameObject, InstalledRoot>();
        internal static void MarkInstalled(GameObject avatar, Transform root)
        {
            Installed.Remove(avatar);
            Installed.Add(avatar, new InstalledRoot { Root = root });
        }

        internal static void Capture(BuildContext context)
        {
            if (!Installed.TryGetValue(context.AvatarRootObject, out var installed)) return;
            Installed.Remove(context.AvatarRootObject);
            if (installed.Root == null) return;
            var menu = context.AvatarRootObject.GetComponent<VRCAvatarDescriptor>().expressionsMenu;
            var state = context.GetState<State>();
            foreach (var component in installed.Root.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component.GetType().FullName != "VF.Model.VRCFury") continue;
                using (var serialized = new SerializedObject(component))
                {
                    var iterator = serialized.GetIterator();
                    while (iterator.Next(true))
                    {
                        if (iterator.propertyType != SerializedPropertyType.ManagedReference ||
                            !iterator.managedReferenceFullTypename.EndsWith(" VF.Model.Feature.FullController", StringComparison.Ordinal)) continue;
                        var menus = iterator.FindPropertyRelative("menus");
                        if (menus == null || !menus.isArray) continue;
                        for (var i = 0; i < menus.arraySize; i++)
                        {
                            var prefix = menus.GetArrayElementAtIndex(i).FindPropertyRelative("prefix");
                            if (prefix == null || string.IsNullOrWhiteSpace(prefix.stringValue)) continue;
                            var parts = SplitPath(prefix.stringValue);
                            Resolve(menu, parts, out var matched, out var anchor);
                            if (matched == 0) continue; // Explicit paths outside the installed menu keep their meaning.
                            state.Routes.Add(new Route { Component = component, PropertyPath = prefix.propertyPath,
                                OriginalPrefix = prefix.stringValue, Anchor = anchor, Suffix = parts.Skip(matched).ToArray() });
                        }
                    }
                }
            }
        }

        internal static void Retarget(BuildContext context)
        {
            var menu = context.AvatarRootObject.GetComponent<VRCAvatarDescriptor>().expressionsMenu;
            foreach (var route in context.GetState<State>().Routes)
            {
                if (route.Component == null) continue;
                var parts = SplitPath(route.OriginalPrefix);
                var anchorLength = parts.Length - route.Suffix.Length;
                Resolve(menu, parts.Take(anchorLength).ToArray(), out var matched, out var currentAnchor);
                if (currentAnchor == route.Anchor && matched == anchorLength) continue;
                // Track the control itself: VRCLens intentionally shares e.g. its Focus menu
                // between the main and Advanced menus, which makes asset-only paths ambiguous.
                var paths = new List<string[]>();
                Walk(menu, null, route.Anchor, new List<string>(), new HashSet<VRCExpressionsMenu>(), paths);
                if (paths.Count != 1)
                    throw new InvalidOperationException("VRCLensカスタマイズのメニュー追加先を一意に追跡できません: " + route.OriginalPrefix +
                        "。Lyraで追加先メニューを削除・複数配置していないか確認してください。");
                var newPrefix = string.Join("/", paths[0].Concat(route.Suffix).Select(EscapeSegment));
                using (var serialized = new SerializedObject(route.Component))
                {
                    var prefix = serialized.FindProperty(route.PropertyPath);
                    if (prefix == null || prefix.stringValue != route.OriginalPrefix)
                        throw new InvalidOperationException("VRCFuryのメニュー設定が処理中に変更されました: " + route.OriginalPrefix);
                    prefix.stringValue = newPrefix;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }

        private static VRCExpressionsMenu Resolve(VRCExpressionsMenu root, string[] parts, out int matched, out VRCExpressionsMenu.Control anchor)
        {
            matched = 0;
            anchor = null;
            var current = root;
            foreach (var part in parts)
            {
                var matches = current?.controls.Where(c => c.type == VRCExpressionsMenu.Control.ControlType.SubMenu &&
                    c.subMenu != null && c.name.Trim() == part.Trim()).ToArray();
                if (matches == null || matches.Length == 0) break;
                if (matches.Length > 1) throw new InvalidOperationException("同名のメニュー追加先が複数あります: " + part);
                current = matches[0].subMenu;
                anchor = matches[0];
                matched++;
            }
            return current;
        }

        internal static List<string[]> PathsTo(VRCExpressionsMenu root, VRCExpressionsMenu target)
        {
            var paths = new List<string[]>();
            Walk(root, target, null, new List<string>(), new HashSet<VRCExpressionsMenu>(), paths);
            return paths;
        }

        private static void Walk(VRCExpressionsMenu menu, VRCExpressionsMenu target, VRCExpressionsMenu.Control targetControl, List<string> path,
            HashSet<VRCExpressionsMenu> ancestors, List<string[]> result)
        {
            if (menu == null || result.Count > 1 || !ancestors.Add(menu)) return;
            if (menu == target) result.Add(path.ToArray());
            else foreach (var control in menu.controls)
            {
                if (control.type != VRCExpressionsMenu.Control.ControlType.SubMenu || control.subMenu == null) continue;
                path.Add(control.name);
                if (control == targetControl) result.Add(path.ToArray());
                else Walk(control.subMenu, target, targetControl, path, ancestors, result);
                path.RemoveAt(path.Count - 1);
            }
            ancestors.Remove(menu);
        }

        private static string EscapeSegment(string value) => value.Replace("/", "\\/");
        internal static string[] SplitPath(string value)
        {
            var parts = new List<string>();
            var segment = new StringBuilder();
            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] == '\\' && i + 1 < value.Length && value[i + 1] == '/') { segment.Append('/'); i++; }
                else if (value[i] == '/') { parts.Add(segment.ToString()); segment.Clear(); }
                else segment.Append(value[i]);
            }
            parts.Add(segment.ToString());
            return parts.ToArray();
        }
    }
}
