using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Logging;
using MaJiang.Bag;
using MaJiang.GM;

namespace RoleGrowth
{
    /// <summary>
    /// 角色技能成长的持久化存储（mod 自有文件）。
    /// 格式：{"存档ID":{"技能ID":数值}, ...}
    /// 只服务「继续游戏」（重启恢复）；开始新游戏时应重置该存档ID。
    /// </summary>
    internal static class GrowthStore
    {
        internal static ManualLogSource Log;
        internal static float PendingValue;
        internal static int PendingSkillId;
        private static string _path;
        private static Dictionary<string, Dictionary<string, float>> _data
            = new Dictionary<string, Dictionary<string, float>>();

        internal static void Init(string pluginDir)
        {
            _path = Path.Combine(pluginDir, "RoleGrowthState.json");
            Load();
        }

        private static void Load()
        {
            _data = new Dictionary<string, Dictionary<string, float>>();
            try
            {
                if (!File.Exists(_path)) return;
                // 格式 {"0":{"20036":0.8},"1":{...}}，手写解析，避免 Newtonsoft 版本问题
                string json = File.ReadAllText(_path).Trim();
                // 只去掉最外层一对大括号（不能用 Trim，它会连内层的 } 一起吃掉）
                if (json.Length >= 2 && json[0] == '{' && json[json.Length - 1] == '}')
                    json = json.Substring(1, json.Length - 2);
                foreach (var slotPart in SplitTop(json, '}'))
                {
                    int c1 = slotPart.IndexOf(':');
                    if (c1 < 0) continue;
                    string slotId = slotPart.Substring(0, c1).Trim().Trim('"');
                    string inner = slotPart.Substring(c1 + 1).Trim().Trim('{', '}');
                    var vals = new Dictionary<string, float>();
                    foreach (var kv in inner.Split(','))
                    {
                        int c2 = kv.IndexOf(':');
                        if (c2 < 0) continue;
                        string skillId = kv.Substring(0, c2).Trim().Trim('"');
                        string vs = kv.Substring(c2 + 1).Trim();
                        if (float.TryParse(vs, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out float v))
                            vals[skillId] = v;
                    }
                    _data[slotId] = vals;
                }
                Log?.LogInfo("[RoleGrowth] loaded " + _data.Count + " slots from " + _path);
            }
            catch (Exception e)
            {
                Log?.LogWarning("[RoleGrowth] load failed: " + e.GetType().Name);
            }
        }

        /// <summary>按最外层 '}' 切分（内层字典的结束符）。</summary>
        private static IEnumerable<string> SplitTop(string s, char close)
        {
            var list = new List<string>();
            int depth = 0, start = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == '{') depth++;
                else if (ch == close)
                {
                    depth--;
                    if (depth == 0)
                    {
                        list.Add(s.Substring(start, i - start + 1));
                        start = i + 1;
                        while (start < s.Length && (s[start] == ',' || char.IsWhiteSpace(s[start]))) start++;
                    }
                }
            }
            return list;
        }

        internal static float Get(int slot, int skillId)
        {
            string sid = slot.ToString();
            string kid = skillId.ToString();
            return _data.TryGetValue(sid, out var inner) && inner.TryGetValue(kid, out float v) ? v : 0f;
        }

        internal static void Set(int slot, int skillId, float v)
        {
            string sid = slot.ToString(), kid = skillId.ToString();
            if (!_data.TryGetValue(sid, out var inner)) { inner = new Dictionary<string, float>(); _data[sid] = inner; }
            if (inner.TryGetValue(kid, out float old) && Math.Abs(old - v) < 0.0001f) return;
            inner[kid] = v;
            Log?.LogInfo("[RoleGrowth] set slot=" + sid + " skill=" + kid + " = " + v.ToString("0.####"));
        }

        /// <summary>开始新游戏：重置该存档ID的所有成长值。</summary>
        internal static void ResetSlot(int slot)
        {
            string sid = slot.ToString();
            if (!_data.Remove(sid)) return;
            Log?.LogInfo("[RoleGrowth] reset slot=" + sid + " (new game)");
        }

        internal static void Flush()
        {
            try
            {
                var sb = new System.Text.StringBuilder("{");
                bool firstSlot = true;
                foreach (var slot in _data)
                {
                    if (!firstSlot) sb.Append(',');
                    firstSlot = false;
                    sb.Append('"').Append(slot.Key).Append("\":{");
                    bool firstSkill = true;
                    foreach (var kv in slot.Value)
                    {
                        if (!firstSkill) sb.Append(',');
                        firstSkill = false;
                        sb.Append('"').Append(kv.Key).Append("\":")
                          .Append(kv.Value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    sb.Append('}');
                }
                sb.Append('}');
                File.WriteAllText(_path, sb.ToString());
            }
            catch (Exception e)
            {
                Log?.LogWarning("[RoleGrowth] flush failed: " + e.GetType().Name);
            }
        }

        /// <summary>取当前 Saver 实例（public，供补丁调用）。</summary>
        internal static Saver GetSaverObject()
        {
            try
            {
                var t = typeof(PlayerInfoCtr);
                var m = t.GetMethod("get_saver",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
                if (m == null) return null;
                object target = null;
                if (!m.IsStatic)
                {
                    var objs = UnityEngine.Object.FindObjectsOfType<PlayerInfoCtr>(true);
                    if (objs == null || objs.Length == 0) return null;
                    target = objs[0];
                }
                return m.Invoke(target, null) as Saver;
            }
            catch { return null; }
        }

        /// <summary>当前存档槽索引：反射读 Saver 的槽位字段，失败回退 0。</summary>
        internal static int CurrentSlot()
        {
            try
            {
                var saver = GetSaverObject();
                if (saver == null) return 0;
                var st = saver.GetType();
                var slotField = st.GetField("_saverSlotIndex");
                var slotProp = st.GetProperty("SaverSlotIndex") ?? st.GetProperty("_saverSlotIndex");
                object val = slotProp != null ? slotProp.GetValue(saver, null)
                           : slotField != null ? slotField.GetValue(saver) : null;
                return val is int i ? i : 0;
            }
            catch { return 0; }
        }
    }
}
