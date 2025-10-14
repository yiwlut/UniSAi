// ============================================================================
// UniSAI - AI-Friendly Unity Scene Analyzer
// ============================================================================
// Author: yiwlut
// Repository: https://github.com/yiwlut/UniSAI
// Description: Enhanced Unity scene and component analyzer for better AI understanding
// License: MIT
// ============================================================================

using UnityEngine;
using System.Reflection;
using System.Text;
using System.IO;
using System.Collections.Generic;
using System;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
#endif

public static class MiniJSON
{
    public static object Deserialize(string json)
    {
        if (json == null) return null;
        int index = 0;
        return ParseValue(json, ref index);
    }

    static object ParseValue(string json, ref int index)
    {
        SkipWhitespace(json, ref index);
        if (index >= json.Length) return null;
        char c = json[index];
        if (c == '{') return ParseObject(json, ref index);
        if (c == '[') return ParseArray(json, ref index);
        if (c == '"' || c == '\'') return ParseString(json, ref index);
        if (char.IsDigit(c) || c == '-') return ParseNumber(json, ref index);
        if (StartsWith(json, index, "true")) { index += 4; return true; }
        if (StartsWith(json, index, "false")) { index += 5; return false; }
        if (StartsWith(json, index, "null")) { index += 4; return null; }
        return null;
    }

    static Dictionary<string, object> ParseObject(string json, ref int index)
    {
        var table = new Dictionary<string, object>();
        index++;
        while (true)
        {
            SkipWhitespace(json, ref index);
            if (index >= json.Length) break;
            if (json[index] == '}') { index++; break; }
            var key = ParseString(json, ref index) as string;
            SkipWhitespace(json, ref index);
            if (json[index] == ':') index++;
            var value = ParseValue(json, ref index);
            table[key] = value;
            SkipWhitespace(json, ref index);
            if (json[index] == ',') { index++; continue; }
            if (json[index] == '}') { index++; break; }
        }
        return table;
    }

    static List<object> ParseArray(string json, ref int index)
    {
        var list = new List<object>();
        index++;
        while (true)
        {
            SkipWhitespace(json, ref index);
            if (index >= json.Length) break;
            if (json[index] == ']') { index++; break; }
            var val = ParseValue(json, ref index);
            list.Add(val);
            SkipWhitespace(json, ref index);
            if (json[index] == ',') { index++; continue; }
            if (json[index] == ']') { index++; break; }
        }
        return list;
    }

    static object ParseString(string json, ref int index)
    {
        var sb = new StringBuilder();
        char quote = json[index];
        index++;
        while (index < json.Length)
        {
            char c = json[index++];
            if (c == quote) break;
            if (c == '\\' && index < json.Length)
            {
                char esc = json[index++];
                switch (esc)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (index + 3 < json.Length)
                        {
                            string hex = json.Substring(index, 4);
                            if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int codePoint))
                            {
                                sb.Append((char)codePoint);
                                index += 4;
                            }
                        }
                        break;
                }
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    static object ParseNumber(string json, ref int index)
    {
        int start = index;
        while (index < json.Length && ("0123456789+-.eE".IndexOf(json[index]) != -1)) index++;
        string num = json.Substring(start, index - start);
        if (num.IndexOf('.') != -1 || num.IndexOf('e') != -1 || num.IndexOf('E') != -1)
        {
            if (double.TryParse(num, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d)) return d;
        }
        else
        {
            if (long.TryParse(num, out long ll)) return ll;
        }
        return null;
    }

    static void SkipWhitespace(string json, ref int index)
    {
        while (index < json.Length && char.IsWhiteSpace(json[index])) index++;
    }

    static bool StartsWith(string s, int index, string target)
    {
        if (index + target.Length > s.Length) return false;
        for (int i = 0; i < target.Length; i++) if (s[index + i] != target[i]) return false;
        return true;
    }
}

// ============================================================================
// UniSAI Main Component
// ============================================================================
public class UniSAI : MonoBehaviour
{
    [Header("Runtime Analysis")]
    public bool includePrivateFields = false;
    public bool includeMethodInfo = false;
    
    [ContextMenu("Log Serialized Values")]
    public void LogSerializedValues()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"GameObject: {gameObject.name}");
        var comps = GetComponents<Component>();
        foreach (var comp in comps)
        {
            if (comp == null) continue;
            sb.AppendLine($"  Component: {comp.GetType().FullName}");
            var fields = comp.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var f in fields)
            {
                bool isPublic = f.IsPublic;
                bool isSerialized = System.Attribute.IsDefined(f, typeof(SerializeField));
                if (!isPublic && !isSerialized && !includePrivateFields) continue;

                object val = null;
                try { val = f.GetValue(comp); } catch { val = "<error>"; }
                sb.AppendLine($"    {f.FieldType.Name} {f.Name} = {FormatValue(val)}");
            }
        }
        Debug.Log(sb.ToString());
    }

    static string FormatValue(object v)
    {
        if (v == null) return "null";
        if (v is UnityEngine.Object uo && uo != null) return $"{uo.name} ({uo.GetType().Name})";
        if (v is Vector3 v3) return $"({v3.x:F2}, {v3.y:F2}, {v3.z:F2})";
        if (v is Vector2 v2) return $"({v2.x:F2}, {v2.y:F2})";
        if (v is Quaternion q) return $"({q.x:F3}, {q.y:F3}, {q.z:F3}, {q.w:F3})";
        if (v is Color c) return $"rgba({c.r:F2}, {c.g:F2}, {c.b:F2}, {c.a:F2})";
        return v?.ToString() ?? "null";
    }

#if UNITY_EDITOR
[MenuItem("Tools/UniSAI/Scene Analyzer Settings")]
public static void OpenAIAnalyzer() => AISceneAnalyzerWindow.ShowWindow();

[MenuItem("Tools/UniSAI/Analyze Current Scene")]
public static void saveCurrentSceneToFile() => saveSceneToFile("Assets/scene_inspector.json");

[MenuItem("Tools/UniSAI/Analyze Selected GameObject")]
public static void saveSelectedToFile() => saveSelectedToFile("Assets/selected_inspector.json");

public static void saveSelectedToFile(string relativePath)
{
    var go = Selection.activeGameObject;
    if (go == null)
    {
        Debug.LogWarning("Select a GameObject first.");
        return;
    }
    var json = saveGameObjectToJson(go, true, 0, true);
    var path = Path.Combine(Application.dataPath, "..", relativePath);
    File.WriteAllText(path, json, Encoding.UTF8);
    Debug.Log($"GameObject data saved to: {path}");
    AssetDatabase.Refresh();
}

public static void saveSceneToFile(string relativePath)
{
    var scene = SceneManager.GetActiveScene();
    var json = saveSceneToJson(scene, true);
    var path = Path.Combine(Application.dataPath, "..", relativePath);
    File.WriteAllText(path, json, Encoding.UTF8);
    Debug.Log($"Scene '{scene.name}' data saved to: {path}");
    AssetDatabase.Refresh();
}

    // ============================================================================
    // PrettyJson
    // ============================================================================
    static class PrettyJson
    {
        public static string Format(string json)
        {
            var sb = new StringBuilder();
            int indent = 0;
            bool inString = false;
            bool escape = false;

            foreach (char c in json)
            {
                if (escape)
                {
                    sb.Append(c);
                    escape = false;
                    continue;
                }

                if (c == '\\' && inString)
                {
                    sb.Append(c);
                    escape = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    sb.Append(c);
                    continue;
                }

                if (inString)
                {
                    sb.Append(c);
                    continue;
                }

                switch (c)
                {
                    case '{':
                    case '[':
                        sb.Append(c);
                        sb.AppendLine();
                        indent++;
                        sb.Append(new string(' ', indent * 2));
                        break;
                    case '}':
                    case ']':
                        sb.AppendLine();
                        indent--;
                        sb.Append(new string(' ', indent * 2));
                        sb.Append(c);
                        break;
                    case ',':
                        sb.Append(c);
                        sb.AppendLine();
                        sb.Append(new string(' ', indent * 2));
                        break;
                    case ':':
                        sb.Append(c);
                        sb.Append(' ');
                        break;
                    default:
                        if (!char.IsWhiteSpace(c))
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }

            return sb.ToString();
        }
    }

    public static string saveSceneToJson(Scene scene, bool prettyPrint = true)
    {
        var sb = new StringBuilder();
        sb.Append("{");
        sb.Append($"\"scene\":\"{Escape(scene.name)}\",");
        sb.Append($"\"scenePath\":\"{Escape(scene.path)}\",");
        sb.Append($"\"isLoaded\":{(scene.isLoaded ? "true" : "false")},");
        sb.Append($"\"isDirty\":{(scene.isDirty ? "true" : "false")},");
        sb.Append("\"gameObjects\":[");

        var rootObjects = scene.GetRootGameObjects();
        bool firstObj = true;

        foreach (var rootGO in rootObjects)
        {
            AppendGameObjectRecursive(sb, rootGO, ref firstObj, 0);
        }

        sb.Append("]}");
        
        string json = sb.ToString();
        return prettyPrint ? PrettyJson.Format(json) : json;
    }

    static void AppendGameObjectRecursive(StringBuilder sb, GameObject go, ref bool firstObj, int parentId)
    {
        if (!firstObj) sb.Append(",");
        firstObj = false;

        sb.Append(saveGameObjectToJson(go, false, parentId));

        var transform = go.transform;
        for (int i = 0; i < transform.childCount; i++)
        {
            var child = transform.GetChild(i).gameObject;
            AppendGameObjectRecursive(sb, child, ref firstObj, go.GetInstanceID());
        }
    }

    public static string saveGameObjectToJson(GameObject go, bool standalone = true, int parentId = 0, bool prettyPrint = true)
    {
        var sb = new StringBuilder();
        
        if (standalone) sb.Append("{\"gameObjects\":[");
        
        sb.Append("{");
        sb.Append($"\"name\":\"{Escape(go.name)}\",");
        sb.Append($"\"instanceId\":{go.GetInstanceID()},");
        sb.Append($"\"tag\":\"{Escape(go.tag)}\",");
        sb.Append($"\"layer\":{go.layer},");
        sb.Append($"\"active\":{(go.activeInHierarchy ? "true" : "false")},");
        sb.Append($"\"activeSelf\":{(go.activeSelf ? "true" : "false")},");
        
        if (parentId != 0)
        {
            sb.Append($"\"parentId\":{parentId},");
        }

        var transform = go.transform;
        sb.Append("\"hierarchy\":{");
        sb.Append($"\"childCount\":{transform.childCount},");
        sb.Append($"\"siblingIndex\":{transform.GetSiblingIndex()},");
        sb.Append($"\"hasParent\":{(transform.parent != null ? "true" : "false")}");
        sb.Append("},");

        sb.Append("\"components\":[");

        var comps = go.GetComponents<Component>();
        bool firstComp = true;
        for (int i = 0; i < comps.Length; i++)
        {
            var comp = comps[i];
            if (comp == null) continue;

            if (!firstComp) sb.Append(",");
            firstComp = false;

            sb.Append("{");
            sb.Append($"\"type\":\"{Escape(comp.GetType().FullName)}\",");
            sb.Append($"\"enabled\":{(GetComponentEnabled(comp) ? "true" : "false")},");
            
            AppendComponentMetadata(sb, comp);
            
            sb.Append("\"props\":{");
            AppendSerializedProperties(sb, comp);
            sb.Append("}}");
        }

        sb.Append("]}");
        
        if (standalone) sb.Append("]}");
        
        string json = sb.ToString();
        return (standalone && prettyPrint) ? PrettyJson.Format(json) : json;
    }

    static bool GetComponentEnabled(Component comp)
    {
        if (comp is Behaviour behaviour) return behaviour.enabled;
        if (comp is Renderer renderer) return renderer.enabled;
        if (comp is Collider collider) return collider.enabled;
        return true;
    }

    static string FormatNumber(float value, int decimals = 3)
    {
        return value.ToString("F" + decimals, System.Globalization.CultureInfo.InvariantCulture);
    }

    static void AppendComponentMetadata(StringBuilder sb, Component comp)
    {
        var type = comp.GetType();
        
        if (comp is Transform t)
        {
            sb.Append($"\"position\":{{\"x\":{FormatNumber(t.position.x)},\"y\":{FormatNumber(t.position.y)},\"z\":{FormatNumber(t.position.z)}}},");
            sb.Append($"\"rotation\":{{\"x\":{FormatNumber(t.rotation.x)},\"y\":{FormatNumber(t.rotation.y)},\"z\":{FormatNumber(t.rotation.z)},\"w\":{FormatNumber(t.rotation.w)}}},");
            sb.Append($"\"scale\":{{\"x\":{FormatNumber(t.localScale.x)},\"y\":{FormatNumber(t.localScale.y)},\"z\":{FormatNumber(t.localScale.z)}}},");
        }
        else if (comp is Camera cam)
        {
            sb.Append($"\"cameraType\":\"{cam.orthographic}\",");
            sb.Append($"\"fieldOfView\":{FormatNumber(cam.fieldOfView, 1)},");
            sb.Append($"\"nearClip\":{FormatNumber(cam.nearClipPlane)},");
            sb.Append($"\"farClip\":{FormatNumber(cam.farClipPlane, 1)},");
        }
        else if (comp is Light light)
        {
            sb.Append($"\"lightType\":\"{light.type}\",");
            sb.Append($"\"intensity\":{FormatNumber(light.intensity, 2)},");
            sb.Append($"\"range\":{FormatNumber(light.range, 1)},");
        }
        else if (comp is MeshRenderer mr)
        {
            sb.Append($"\"materialCount\":{(mr.sharedMaterials?.Length ?? 0)},");
        }

        if (!type.Namespace?.StartsWith("UnityEngine") == true)
        {
            var script = MonoScript.FromMonoBehaviour(comp as MonoBehaviour);
            if (script != null)
            {
                sb.Append($"\"scriptPath\":\"{Escape(AssetDatabase.GetAssetPath(script))}\",");
            }
        }
    }

    static void AppendSerializedProperties(StringBuilder sb, Component comp)
    {
        var so = new SerializedObject(comp);
        var iter = so.GetIterator();
        bool firstProp = true;

        for (bool enter = iter.Next(true); enter; enter = iter.Next(false))
        {
            if (iter.name == "m_Script") continue;

            if (!firstProp) sb.Append(",");
            firstProp = false;

            sb.Append($"\"{Escape(iter.name)}\":");
            AppendPropertyValue(sb, iter);
        }
    }

    static void AppendPropertyValue(StringBuilder sb, SerializedProperty prop)
    {
        switch (prop.propertyType)
        {
            case SerializedPropertyType.Integer:
                sb.Append(prop.intValue);
                break;
            
            case SerializedPropertyType.Boolean:
                sb.Append(prop.boolValue ? "true" : "false");
                break;
            
            case SerializedPropertyType.Float:
                sb.Append(prop.floatValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
                break;
            
            case SerializedPropertyType.String:
                sb.Append($"\"{Escape(prop.stringValue)}\"");
                break;
            
            case SerializedPropertyType.Color:
                var c = prop.colorValue;
                sb.Append($"{{\"r\":{FormatNumber(c.r)},\"g\":{FormatNumber(c.g)},\"b\":{FormatNumber(c.b)},\"a\":{FormatNumber(c.a)}}}");
                break;
            
            case SerializedPropertyType.ObjectReference:
                if (prop.objectReferenceValue != null)
                {
                    var obj = prop.objectReferenceValue;
                    sb.Append("{");
                    sb.Append($"\"name\":\"{Escape(obj.name)}\",");
                    sb.Append($"\"type\":\"{Escape(obj.GetType().Name)}\",");
                    sb.Append($"\"instanceId\":{obj.GetInstanceID()},");
                    
                    string assetPath = AssetDatabase.GetAssetPath(obj);
                    if (!string.IsNullOrEmpty(assetPath))
                    {
                        sb.Append($"\"assetPath\":\"{Escape(assetPath)}\",");
                        sb.Append($"\"guid\":\"{AssetDatabase.AssetPathToGUID(assetPath)}\"");
                    }
                    else
                    {
                        sb.Append("\"assetPath\":\"\",");
                        sb.Append("\"guid\":\"\"");
                    }
                    sb.Append("}");
                }
                else
                {
                    sb.Append("null");
                }
                break;
            
            case SerializedPropertyType.LayerMask:
                sb.Append($"{{\"value\":{prop.intValue}}}");
                break;
            
            case SerializedPropertyType.Enum:
                var enumNames = prop.enumNames;
                var enumIndex = prop.enumValueIndex;
                if (enumIndex >= 0 && enumIndex < enumNames.Length)
                {
                    sb.Append($"\"{Escape(enumNames[enumIndex])}\"");
                }
                else
                {
                    sb.Append($"{{\"index\":{enumIndex},\"value\":{prop.intValue}}}");
                }
                break;
            
            case SerializedPropertyType.Vector2:
                var v2 = prop.vector2Value;
                sb.Append($"{{\"x\":{FormatNumber(v2.x)},\"y\":{FormatNumber(v2.y)}}}");
                break;
            
            case SerializedPropertyType.Vector3:
                var v3 = prop.vector3Value;
                sb.Append($"{{\"x\":{FormatNumber(v3.x)},\"y\":{FormatNumber(v3.y)},\"z\":{FormatNumber(v3.z)}}}");
                break;
            
            case SerializedPropertyType.Vector4:
                var v4 = prop.vector4Value;
                sb.Append($"{{\"x\":{FormatNumber(v4.x)},\"y\":{FormatNumber(v4.y)},\"z\":{FormatNumber(v4.z)},\"w\":{FormatNumber(v4.w)}}}");
                break;
            
            case SerializedPropertyType.Rect:
                var rect = prop.rectValue;
                sb.Append($"{{\"x\":{FormatNumber(rect.x)},\"y\":{FormatNumber(rect.y)},\"width\":{FormatNumber(rect.width)},\"height\":{FormatNumber(rect.height)}}}");
                break;
            
            case SerializedPropertyType.ArraySize:
                sb.Append(prop.arraySize);
                break;
            
            case SerializedPropertyType.Character:
                sb.Append($"\"{(char)prop.intValue}\"");
                break;
            
            case SerializedPropertyType.AnimationCurve:
                var curve = prop.animationCurveValue;
                sb.Append($"{{\"keyCount\":{curve.keys.Length}}}");
                break;
            
            case SerializedPropertyType.Bounds:
                var bounds = prop.boundsValue;
                sb.Append($"{{\"center\":{{\"x\":{FormatNumber(bounds.center.x)},\"y\":{FormatNumber(bounds.center.y)},\"z\":{FormatNumber(bounds.center.z)}}},");
                sb.Append($"\"size\":{{\"x\":{FormatNumber(bounds.size.x)},\"y\":{FormatNumber(bounds.size.y)},\"z\":{FormatNumber(bounds.size.z)}}}}}");
                break;
            
            case SerializedPropertyType.Gradient:
                sb.Append($"{{\"mode\":\"{prop.gradientValue.mode}\"}}");
                break;

            case SerializedPropertyType.Quaternion:
                var q = prop.quaternionValue;
                sb.Append($"{{\"x\":{FormatNumber(q.x)},\"y\":{FormatNumber(q.y)},\"z\":{FormatNumber(q.z)},\"w\":{FormatNumber(q.w)}}}");
                break;

            case SerializedPropertyType.Generic:
                if (prop.isArray && !prop.isFixedBuffer)
                {
                    AppendArrayProperty(sb, prop);
                }
                else
                {
                    sb.Append($"{{\"type\":\"generic\",\"displayName\":\"{Escape(prop.displayName)}\"}}");
                }
                break;
            
            default:
                sb.Append($"{{\"type\":\"{prop.propertyType}\",\"displayName\":\"{Escape(prop.displayName)}\"}}");
                break;
        }
    }

    static void AppendArrayProperty(StringBuilder sb, SerializedProperty arrayProp)
    {
        sb.Append("{");
        sb.Append($"\"size\":{arrayProp.arraySize},");
        sb.Append("\"items\":[");
        
        int maxItems = Mathf.Min(arrayProp.arraySize, 10);
        for (int i = 0; i < maxItems; i++)
        {
            if (i > 0) sb.Append(",");
            var element = arrayProp.GetArrayElementAtIndex(i);
            AppendPropertyValue(sb, element);
        }
        
        if (arrayProp.arraySize > maxItems)
        {
            sb.Append($",\"...\":\"and {arrayProp.arraySize - maxItems} more items\"");
        }
        
        sb.Append("]}");
    }

    static string Escape(string s) => s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t") ?? "";

    // Enhanced AI-friendly analyzer window
    public class AISceneAnalyzerWindow : EditorWindow
    {
        private Vector2 scrollPos;
        private string lastOutputPath = "";
        private bool includeInactiveObjects = true;
        private bool includeComponentMetadata = true;
        private bool generateSceneSummary = true;

        public static void ShowWindow() => GetWindow<AISceneAnalyzerWindow>("UniSAI");

        void OnGUI()
        {
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
            
            EditorGUILayout.LabelField("AI-Friendly Unity Scene Analyzer", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Options:", EditorStyles.boldLabel);
            includeInactiveObjects = EditorGUILayout.Toggle("Include Inactive Objects", includeInactiveObjects);
            includeComponentMetadata = EditorGUILayout.Toggle("Include Metadata", includeComponentMetadata);
            generateSceneSummary = EditorGUILayout.Toggle("Generate Scene Summary", generateSceneSummary);
            
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Scene Analysis:", EditorStyles.boldLabel);
            
            if (GUILayout.Button("Analyze Current Scene ¡æ scene_inspector.json"))
            {
                AnalyzeScene("Assets/scene_inspector.json");
            }

            if (GUILayout.Button("Analyze Current Scene ¡æ Custom Path"))
            {
                string path = EditorUtility.SaveFilePanel("Save Scene Analysis", "Assets", "scene_analysis", "json");
                if (!string.IsNullOrEmpty(path))
                {
                    if (path.StartsWith(Application.dataPath))
                    {
                        path = "Assets" + path.Substring(Application.dataPath.Length);
                    }
                    AnalyzeScene(path);
                }
            }

            EditorGUILayout.Space();

            EditorGUILayout.LabelField("GameObject Analysis:", EditorStyles.boldLabel);
            
            if (GUILayout.Button("Analyze Selected GameObject"))
            {
                if (Selection.activeGameObject != null)
                {
                    saveSelectedToFile("Assets/selected_gameobject.json");
                }
                else
                {
                    EditorUtility.DisplayDialog("No Selection", "Please select a GameObject first.", "OK");
                }
            }

            if (GUILayout.Button("Generate AI Context Summary"))
            {
                GenerateAIContextSummary();
            }

            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Quick Actions:", EditorStyles.boldLabel);
            
            if (GUILayout.Button("Open Last Output File"))
            {
                if (!string.IsNullOrEmpty(lastOutputPath) && File.Exists(lastOutputPath))
                {
                    System.Diagnostics.Process.Start(lastOutputPath);
                }
                else
                {
                    EditorUtility.DisplayDialog("No File", "No output file found or generated yet.", "OK");
                }
            }

            if (GUILayout.Button("Refresh Scene View"))
            {
                SceneView.RepaintAll();
                EditorApplication.RepaintHierarchyWindow();
            }

            EditorGUILayout.Space();
            
            if (!string.IsNullOrEmpty(lastOutputPath))
            {
                EditorGUILayout.LabelField($"Last Output: {lastOutputPath}", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndScrollView();
        }

        void AnalyzeScene(string relativePath)
        {
            try
            {
                var scene = SceneManager.GetActiveScene();
                string json;
                
                if (generateSceneSummary)
                {
                    json = GenerateEnhancedSceneAnalysis(scene);
                }
                else
                {
                    json = saveSceneToJson(scene);
                }

                var fullPath = Path.Combine(Application.dataPath, "..", relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                File.WriteAllText(fullPath, json, Encoding.UTF8);
                
                lastOutputPath = fullPath;
                Debug.Log($"Scene analysis saved to: {fullPath}");
                AssetDatabase.Refresh();
                
                EditorUtility.DisplayDialog("Analysis Complete", 
                    $"Scene '{scene.name}' analyzed and saved to:\n{relativePath}", "OK");
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to analyze scene: {e.Message}");
                EditorUtility.DisplayDialog("Error", $"Failed to analyze scene:\n{e.Message}", "OK");
            }
        }

        string GenerateEnhancedSceneAnalysis(Scene scene)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            
            sb.Append("\"metadata\":{");
            sb.Append($"\"sceneName\":\"{Escape(scene.name)}\",");
            sb.Append($"\"scenePath\":\"{Escape(scene.path)}\",");
            sb.Append($"\"analysisTime\":\"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\",");
            sb.Append($"\"unityVersion\":\"{Application.unityVersion}\",");
            sb.Append($"\"platform\":\"{Application.platform}\"");
            sb.Append("},");

            var rootObjects = scene.GetRootGameObjects();
            var allObjects = GetAllGameObjectsInScene(scene);
            
            sb.Append("\"summary\":{");
            sb.Append($"\"totalGameObjects\":{allObjects.Count},");
            sb.Append($"\"rootGameObjects\":{rootObjects.Length},");
            sb.Append($"\"activeObjects\":{allObjects.Count(go => go.activeInHierarchy)},");
            sb.Append($"\"componentsBreakdown\":{GenerateComponentBreakdown(allObjects)}");
            sb.Append("},");

            string sceneData = saveSceneToJson(scene, false);
            sb.Append(sceneData.Substring(1));
            
            return PrettyJson.Format(sb.ToString());
        }

        List<GameObject> GetAllGameObjectsInScene(Scene scene)
        {
            var allObjects = new List<GameObject>();
            var rootObjects = scene.GetRootGameObjects();
            
            foreach (var root in rootObjects)
            {
                AddGameObjectAndChildren(root, allObjects);
            }
            
            return allObjects;
        }

        void AddGameObjectAndChildren(GameObject go, List<GameObject> list)
        {
            if (includeInactiveObjects || go.activeInHierarchy)
            {
                list.Add(go);
            }
            
            for (int i = 0; i < go.transform.childCount; i++)
            {
                AddGameObjectAndChildren(go.transform.GetChild(i).gameObject, list);
            }
        }

        string GenerateComponentBreakdown(List<GameObject> objects)
        {
            var componentCounts = new Dictionary<string, int>();
            
            foreach (var go in objects)
            {
                var components = go.GetComponents<Component>();
                foreach (var comp in components)
                {
                    if (comp == null) continue;
                    
                    var typeName = comp.GetType().Name;
                    componentCounts[typeName] = componentCounts.GetValueOrDefault(typeName, 0) + 1;
                }
            }

            var sb = new StringBuilder();
            sb.Append("{");
            
            bool first = true;
            foreach (var kvp in componentCounts.OrderByDescending(x => x.Value))
            {
                if (!first) sb.Append(",");
                first = false;
                sb.Append($"\"{Escape(kvp.Key)}\":{kvp.Value}");
            }
            
            sb.Append("}");
            return sb.ToString();
        }

        void GenerateAIContextSummary()
        {
            var scene = SceneManager.GetActiveScene();
            var summary = new StringBuilder();
            
            summary.AppendLine($"# AI Context Summary for Scene: {scene.name}");
            summary.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            summary.AppendLine();
            
            var allObjects = GetAllGameObjectsInScene(scene);
            summary.AppendLine($"## Scene Overview");
            summary.AppendLine($"- Total GameObjects: {allObjects.Count}");
            summary.AppendLine($"- Active GameObjects: {allObjects.Count(go => go.activeInHierarchy)}");
            summary.AppendLine();
            
            summary.AppendLine("## Key Components Found:");
            var componentCounts = new Dictionary<string, int>();
            foreach (var go in allObjects)
            {
                foreach (var comp in go.GetComponents<Component>())
                {
                    if (comp == null) continue;
                    var typeName = comp.GetType().Name;
                    componentCounts[typeName] = componentCounts.GetValueOrDefault(typeName, 0) + 1;
                }
            }
            
            foreach (var kvp in componentCounts.OrderByDescending(x => x.Value).Take(10))
            {
                summary.AppendLine($"- {kvp.Key}: {kvp.Value} instances");
            }
            
            var path = Path.Combine(Application.dataPath, "..", "Assets/AI_Context_Summary.md");
            File.WriteAllText(path, summary.ToString(), Encoding.UTF8);
            lastOutputPath = path;
            
            AssetDatabase.Refresh();
            Debug.Log($"AI Context Summary saved to: {path}");
        }
    }
#endif
}

