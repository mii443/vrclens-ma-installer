using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Micro.VRCLensMA.Editor
{
    // Copies serialized reference graphs, including cycles and AnimatorController subassets.
    // Never modifies an input object. The map also preserves shared references between menus.
    internal sealed class AssetGraph
    {
        private readonly Func<Object, bool> shouldClone;
        internal readonly Dictionary<Object, Object> Copies = new Dictionary<Object, Object>();

        internal AssetGraph(Func<Object, bool> shouldClone) { this.shouldClone = shouldClone; }

        internal Object Clone(Object source)
        {
            if (source == null || !shouldClone(source)) return source;
            if (Copies.TryGetValue(source, out var existing)) return existing;
            var copy = Object.Instantiate(source);
            copy.name = source.name;
            copy.hideFlags = HideFlags.None;
            Copies.Add(source, copy);
            // Instantiate remaps self-references to the new instance. Traverse the SOURCE graph,
            // otherwise a cyclic menu would repeatedly clone its newly created self-reference.
            using (var original = new SerializedObject(source))
            using (var destination = new SerializedObject(copy))
            {
                var prop = original.GetIterator();
                while (prop.Next(true))
                {
                    if (prop.propertyType != SerializedPropertyType.ObjectReference || prop.propertyPath == "m_Script") continue;
                    var target = destination.FindProperty(prop.propertyPath);
                    if (target != null) target.objectReferenceValue = Clone(prop.objectReferenceValue);
                }
                destination.ApplyModifiedPropertiesWithoutUndo();
            }
            return copy;
        }

        internal static void Rewrite(Object target, Func<Object, Object> replace)
        {
            using (var so = new SerializedObject(target))
            {
                var prop = so.GetIterator();
                while (prop.Next(true))
                {
                    if (prop.propertyType != SerializedPropertyType.ObjectReference || prop.propertyPath == "m_Script") continue;
                    var before = prop.objectReferenceValue;
                    if (before == null) continue;
                    var after = replace(before);
                    if (after != before) prop.objectReferenceValue = after;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        internal void RemapHierarchy(GameObject avatar, bool discover)
        {
            foreach (var component in avatar.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component is Transform) continue;
                Rewrite(component, o => discover ? Clone(o) : Copies.TryGetValue(o, out var copy) ? copy : o);
            }
        }
    }
}
