using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Shared
{
    public static class YamlConfig
    {
        /// <summary>
        /// 读取简易 YAML 配置（key: value 格式）。
        /// fileName: 配置文件名（如 "Mod.yml"），默认从 dll 所在目录加载。
        /// directory: 可选，指定加载目录（默认 = dll 所在目录）。
        /// 缺失的键保留在 defaults 中，调用方用 defaults 兜底。
        /// </summary>
        public static Dictionary<string, string> Load(string fileName, Dictionary<string, string> defaults, string directory = null)
        {
            directory ??= Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string path = Path.Combine(directory, fileName);

            var result = new Dictionary<string, string>(defaults, StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path))
            {
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("# 配置项（改后重启游戏生效）");
                foreach (var kv in defaults)
                    sb.AppendLine($"{kv.Key}: {kv.Value}");
                File.WriteAllText(path, sb.ToString(), System.Text.Encoding.UTF8);
                return result;
            }

            try
            {
                string[] lines = File.ReadAllLines(path);
                foreach (string line in lines)
                {
                    string trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;
                    int idx = trimmed.IndexOf(':');
                    if (idx < 0) continue;
                    string key = trimmed.Substring(0, idx).Trim();
                    string val = StripInlineComment(trimmed.Substring(idx + 1).Trim());
                    result[key] = val;
                }
            }
            catch (Exception) { }

            return result;
        }

        private static string StripInlineComment(string value)
        {
            bool inSingle = false, inDouble = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '\'' && !inDouble) inSingle = !inSingle;
                else if (c == '"' && !inSingle) inDouble = !inDouble;
                else if (c == '#' && !inSingle && !inDouble && (i == 0 || char.IsWhiteSpace(value[i - 1])))
                    return value.Substring(0, i).TrimEnd();
            }
            return value;
        }
    }
}
