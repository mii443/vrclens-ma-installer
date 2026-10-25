using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Micro.VRCLensMA.Editor
{
    internal static class LyraIntegration
    {
        private static Type ProxyType => AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("Lyra.MenuManagerItemProxy", false)).FirstOrDefault(t => t != null);
        internal static bool Available => ProxyType != null;

        internal static Component ConfigureProxy(VRCLensInstaller installer, Component setup)
        {
            var type = ProxyType ?? throw new InvalidOperationException("Lyra Menu Manager 1.1.0以降を導入してください。");
            var avatar = installer.GetComponentInParent<VRCAvatarDescriptor>();
            var target = new SerializedObject(setup).FindProperty("targetSubMenu").objectReferenceValue as VRCExpressionsMenu;
            var paths = target == null ? new[] { Array.Empty<string>() }.ToList()
                : CustomizationMenuRouting.PathsTo(avatar.expressionsMenu, target);
            if (paths.Count != 1) throw new InvalidOperationException("VRCLensの追加先メニューへのパスを一意に決定できません。");
            var proxy = installer.GetComponent(type) ?? Undo.AddComponent(installer.gameObject, type);
            using (var serialized = new SerializedObject(proxy))
            {
                serialized.FindProperty("menuItemName").stringValue = " VRCLens ";
                serialized.FindProperty("controlType").intValue = (int)VRCExpressionsMenu.Control.ControlType.SubMenu;
                var parent = serialized.FindProperty("parentFolderPath");
                parent.arraySize = paths[0].Length;
                for (var i = 0; i < paths[0].Length; i++) parent.GetArrayElementAtIndex(i).stringValue = paths[0][i];
                serialized.ApplyModifiedProperties();
            }
            return proxy;
        }
    }
}
