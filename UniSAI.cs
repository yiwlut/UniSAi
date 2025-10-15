// ============================================================================
// UniSAI - AI-Friendly Unity Scene Analyzer
// ============================================================================
// Author: yiwlut
// Repository: https://github.com/yiwlut/UniSAI
// Description: Enhanced Unity scene and component analyzer for better AI understanding
// License: MIT
// ============================================================================

using UnityEngine;
using System.Text;
using System.IO;
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine.SceneManagement;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
#endif

/// <summary>
/// Simplified UniSAI: only compact exporters retained per request.
/// Generates small JSON containing scene/gameobject hierarchy and component type names
/// and compact UnityEvent listener info when present.
/// </summary>
public static class UniSAI
{
#if UNITY_EDITOR
    [MenuItem("Tools/UniSAI/Analyze Current Scene (Compact)")]
    public static void saveCurrentSceneToFileCompact() => saveSceneToFileCompact("Assets/scene_inspector_compact.json");

    [MenuItem("Tools/UniSAI/Analyze Selected GameObject (Compact)")]
    public static void saveSelectedToFileCompact() => saveSelectedToFileCompact("Assets/selected_inspector_compact.json");

    public static void saveSceneToFileCompact(string relativePath)
    {
        var scene = SceneManager.GetActiveScene();
        var json = saveSceneToCompactJson(scene);
        var path = Path.Combine(Application.dataPath, "..", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, json, Encoding.UTF8);
        Debug.Log($"Compact Scene data saved to: {path}");
        AssetDatabase.Refresh();
    }

    public static void saveSelectedToFileCompact(string relativePath)
    {
        var go = Selection.activeGameObject;
        if (go == null)
        {
            Debug.LogWarning("Select a GameObject first.");
            return;
        }

        var json = saveGameObjectToCompactJson(go, true);
        var path = Path.Combine(Application.dataPath, "..", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, json, Encoding.UTF8);
        Debug.Log($"Compact GameObject data saved to: {path}");
        AssetDatabase.Refresh();
    }

    public static string saveSceneToCompactJson(Scene scene)
    {
        // Build symbol table
        var symbols = new List<string>();
        var symIndex = new Dictionary<string, int>();

        void AddSym(string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            if (!symIndex.ContainsKey(s))
            {
                symIndex[s] = symbols.Count;
                symbols.Add(s);
            }
        }

        // Collect symbols from scene
        AddSym(scene.name);
        foreach (var root in scene.GetRootGameObjects())
        {
            CollectSymbolsRecursive(root, AddSym);
        }

        // Build JSON
        var sb = new StringBuilder();
        sb.Append('{');
        // symbols array (short key "s")
        sb.Append("\"s\":[");
        for (int i = 0; i < symbols.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('"').Append(Escape(symbols[i])).Append('"');
        }
        sb.Append("],");

        // objects (short key "o")
        sb.Append("\"o\":[");
        bool first = true;
        foreach (var root in scene.GetRootGameObjects())
        {
            if (!first) sb.Append(',');
            first = false;
            AppendGameObjectCompactSym(sb, root, symIndex);
        }
        sb.Append("]}");

        return sb.ToString();
    }

    static void CollectSymbolsRecursive(GameObject go, Action<string> add)
    {
        add(go.name);
        add(go.tag);
        foreach (var c in go.GetComponents<Component>())
        {
            if (c == null) continue;
            var type = c.GetType();
            add(type.Name);
            var eventFields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                  .Where(f => typeof(UnityEngine.Events.UnityEventBase).IsAssignableFrom(f.FieldType));
            // Collect serialized ObjectReference fields (minimal): field name and target identifier (GUID or names)
            try
            {
                var so = new SerializedObject(c);
                var prop = so.GetIterator();
                if (prop.NextVisible(true))
                {
                    do
                    {
                        if (prop.propertyType == SerializedPropertyType.ObjectReference)
                        {
                            add(prop.name);
                            var obj = prop.objectReferenceValue;
                            if (obj != null)
                            {
                                try
                                {
                                    var path = AssetDatabase.GetAssetPath(obj);
                                    if (!string.IsNullOrEmpty(path))
                                    {
                                        var guid = AssetDatabase.AssetPathToGUID(path);
                                        add(guid);
                                    }
                                    else if (obj is Component compTarget)
                                    {
                                        add(compTarget.gameObject.name);
                                        add(compTarget.GetType().Name);
                                    }
                                    else if (obj is GameObject goTarget)
                                    {
                                        add(goTarget.name);
                                    }
                                    else
                                    {
                                        add(obj.GetType().Name);
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                    while (prop.NextVisible(false));
                }
            }
            catch { }
            foreach (var f in eventFields)
            {
                add(f.Name);
                try
                {
                    var evtObj = f.GetValue(c) as UnityEngine.Events.UnityEventBase;
                    if (evtObj == null) continue;
                    int cnt = 0;
                    try { cnt = evtObj.GetPersistentEventCount(); } catch { cnt = 0; }
                    for (int i = 0; i < cnt; i++)
                    {
                        var target = evtObj.GetPersistentTarget(i);
                        var method = "";
                        try { method = evtObj.GetPersistentMethodName(i) ?? ""; } catch { method = ""; }
                        add(method);
                        if (target != null)
                        {
                            if (target is Component compTarget)
                            {
                                add(compTarget.gameObject.name);
                                add(compTarget.GetType().Name);
                            }
                            else if (target is GameObject goTarget)
                            {
                                add(goTarget.name);
                            }
                            else
                            {
                                add(target.GetType().Name);
                            }
                        }
                    }
                }
                catch { }
            }
        }

        var t = go.transform;
        for (int i = 0; i < t.childCount; i++)
            CollectSymbolsRecursive(t.GetChild(i).gameObject, add);
    }

    static void AppendGameObjectCompactSym(StringBuilder sb, GameObject go, Dictionary<string, int> symIndex)
    {
        int GetIdx(string s) => string.IsNullOrEmpty(s) ? -1 : (symIndex.TryGetValue(s, out var idx) ? idx : -1);

        sb.Append('{');
        // name
        sb.Append("\"n\":").Append(GetIdx(go.name)).Append(',');
        // tag
        sb.Append("\"g\":").Append(GetIdx(go.tag)).Append(',');
        // layer
        sb.Append("\"l\":").Append(go.layer).Append(',');
        // active (omit when true)
        if (!go.activeInHierarchy) sb.Append("\"a\":0,");

        // components
        sb.Append("\"c\":[");
        bool firstComp = true;
        foreach (var c in go.GetComponents<Component>())
        {
            if (c == null) continue;
            if (!firstComp) sb.Append(',');
            firstComp = false;

            var type = c.GetType();
            var eventFields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                  .Where(f => typeof(UnityEngine.Events.UnityEventBase).IsAssignableFrom(f.FieldType));

            var events = new List<string>();
            foreach (var f in eventFields)
            {
                try
                {
                    var evtObj = f.GetValue(c) as UnityEngine.Events.UnityEventBase;
                    if (evtObj == null) continue;
                    int cnt = 0;
                    try { cnt = evtObj.GetPersistentEventCount(); } catch { cnt = 0; }
                    if (cnt <= 0) continue;

                    var listenersSb = new StringBuilder();
                    listenersSb.Append('{');
                    listenersSb.Append("\"f\":").Append(GetIdx(f.Name)).Append(",\"l\":[");
                    bool firstL = true;
                    for (int i = 0; i < cnt; i++)
                    {
                        if (!firstL) listenersSb.Append(',');
                        firstL = false;
                        var target = evtObj.GetPersistentTarget(i);
                        var method = "";
                        try { method = evtObj.GetPersistentMethodName(i) ?? ""; } catch { method = ""; }

                        int tg = -1;
                        int ct = -1;
                        int m = GetIdx(method);

                        if (target != null)
                        {
                            if (target is Component compTarget)
                            {
                                tg = GetIdx(compTarget.gameObject.name);
                                ct = GetIdx(compTarget.GetType().Name);
                            }
                            else if (target is GameObject goTarget)
                            {
                                tg = GetIdx(goTarget.name);
                            }
                            else
                            {
                                tg = GetIdx(target.GetType().Name);
                            }
                        }

                        listenersSb.Append('[').Append(tg).Append(',').Append(ct).Append(',').Append(m).Append(']');
                    }
                    listenersSb.Append("]}");
                    events.Add(listenersSb.ToString());
                }
                catch { }
            }

            if (events.Count == 0)
            {
                sb.Append(GetIdx(type.Name));
            }
            else
            {
                sb.Append('{');
                sb.Append("\"T\":").Append(GetIdx(type.Name)).Append(",\"E\":[");
                for (int ei = 0; ei < events.Count; ei++)
                {
                    if (ei > 0) sb.Append(',');
                    sb.Append(events[ei]);
                }
                sb.Append("]}");
            }

            // Minimal: collect serialized ObjectReference fields and append compact refs if any
            try
            {
                var so = new SerializedObject(c);
                var prop = so.GetIterator();
                var refs = new List<string>();
                if (prop.NextVisible(true))
                {
                    do
                    {
                        if (prop.propertyType == SerializedPropertyType.ObjectReference)
                        {
                            var obj = prop.objectReferenceValue;
                            if (obj == null) continue;

                            int fieldIdx = GetIdx(prop.name);
                            int targetIdx = -1;
                            int meta = 0; // 0=scene-object,1=asset,2=component

                            try
                            {
                                var path = AssetDatabase.GetAssetPath(obj);
                                if (!string.IsNullOrEmpty(path))
                                {
                                    var guid = AssetDatabase.AssetPathToGUID(path);
                                    targetIdx = GetIdx(guid);
                                    meta = 1;
                                }
                                else if (obj is Component compTarget)
                                {
                                    targetIdx = GetIdx(compTarget.gameObject.name);
                                    meta = 2;
                                }
                                else if (obj is GameObject goTarget)
                                {
                                    targetIdx = GetIdx(goTarget.name);
                                    meta = 0;
                                }
                                else
                                {
                                    targetIdx = GetIdx(obj.GetType().Name);
                                    meta = 1;
                                }
                            }
                            catch { targetIdx = -1; }

                            // only record if we have a valid target symbol
                            if (fieldIdx >= 0 && targetIdx >= 0)
                            {
                                refs.Add($"[{fieldIdx},{targetIdx},{meta}]");
                            }
                        }
                    }
                    while (prop.NextVisible(false));
                }

                if (refs.Count > 0)
                {
                    sb.Append(',');
                    sb.Append("\"r\":[");
                    for (int i = 0; i < refs.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append(refs[i]);
                    }
                    sb.Append(']');
                }
            }
            catch { }
        }
        sb.Append(']').Append(',');

        // children
        sb.Append("\"h\":[");
        var t = go.transform;
        bool firstChild = true;
        for (int i = 0; i < t.childCount; i++)
        {
            var child = t.GetChild(i).gameObject;
            if (!firstChild) sb.Append(',');
            firstChild = false;
            AppendGameObjectCompactSym(sb, child, symIndex);
        }
        sb.Append(']');

        sb.Append('}');
    }

    static void AppendGameObjectCompact(StringBuilder sb, GameObject go)
    {
        sb.Append('{');
        sb.Append("\"name\":").Append('"').Append(Escape(go.name)).Append('"').Append(',');
        sb.Append("\"tag\":").Append('"').Append(Escape(go.tag)).Append('"').Append(',');
        sb.Append("\"layer\":").Append(go.layer).Append(',');
        sb.Append("\"active\":").Append(go.activeInHierarchy ? "true" : "false").Append(',');

        var comps = go.GetComponents<Component>();
        sb.Append("\"components\":[");
        bool firstComp = true;
        foreach (var c in comps)
        {
            if (c == null) continue;
            if (!firstComp) sb.Append(',');
            firstComp = false;

            var type = c.GetType();
            var eventFields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                  .Where(f => typeof(UnityEngine.Events.UnityEventBase).IsAssignableFrom(f.FieldType));

            bool hasEvents = false;
            var eventsInfo = new System.Collections.Generic.List<string>();

            foreach (var f in eventFields)
            {
                try
                {
                    var evtObj = f.GetValue(c) as UnityEngine.Events.UnityEventBase;
                    if (evtObj == null) continue;
                    int cnt = 0;
                    try { cnt = evtObj.GetPersistentEventCount(); } catch { cnt = 0; }
                    if (cnt <= 0) continue;

                    hasEvents = true;
                    var listenersSb = new StringBuilder();
                    listenersSb.Append('{');
                    listenersSb.Append("\"f\":\"").Append(Escape(f.Name)).Append("\",\"l\":[");

                    for (int i = 0; i < cnt; i++)
                    {
                        if (i > 0) listenersSb.Append(',');
                        var target = evtObj.GetPersistentTarget(i);
                        var method = "";
                        try { method = evtObj.GetPersistentMethodName(i) ?? ""; } catch { method = ""; }

                        string tgName = "";
                        string tgComp = "";
                        if (target != null)
                        {
                            if (target is Component compTarget)
                            {
                                tgName = Escape(compTarget.gameObject.name);
                                tgComp = Escape(compTarget.GetType().Name);
                            }
                            else if (target is GameObject goTarget)
                            {
                                tgName = Escape(goTarget.name);
                            }
                            else
                            {
                                tgName = Escape(target.GetType().Name);
                            }
                        }

                        listenersSb.Append('{');
                        listenersSb.Append("\"tg\":\"").Append(tgName).Append("\",");
                        listenersSb.Append("\"ct\":\"").Append(tgComp).Append("\",");
                        listenersSb.Append("\"m\":\"").Append(Escape(method)).Append("\"");
                        listenersSb.Append('}');
                    }

                    listenersSb.Append("]}");
                    eventsInfo.Add(listenersSb.ToString());
                }
                catch { }
            }

            if (!hasEvents)
            {
                sb.Append('"').Append(Escape(type.Name)).Append('"');
            }
            else
            {
                sb.Append('{');
                sb.Append("\"t\":\"").Append(Escape(type.Name)).Append("\"");
                sb.Append(",\"ev\":[");
                for (int ei = 0; ei < eventsInfo.Count; ei++)
                {
                    if (ei > 0) sb.Append(',');
                    sb.Append(eventsInfo[ei]);
                }
                sb.Append("]}");
            }
        }
        sb.Append(']').Append(',');

        sb.Append("\"children\":[");
        var t = go.transform;
        bool firstChild = true;
        for (int i = 0; i < t.childCount; i++)
        {
            var child = t.GetChild(i).gameObject;
            if (!firstChild) sb.Append(',');
            firstChild = false;
            AppendGameObjectCompact(sb, child);
        }
        sb.Append(']');

        sb.Append('}');
    }

    static string saveGameObjectToCompactJson(GameObject go, bool standalone = true)
    {
        var sb = new StringBuilder();
        if (standalone) sb.Append('{').Append("\"gameObjects\":[");
        AppendGameObjectCompact(sb, go);
        if (standalone) sb.Append("]}");
        return sb.ToString();
    }

    static string Escape(string s) => s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t") ?? "";
#endif
}

