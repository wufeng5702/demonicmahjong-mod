using System;
using System.Text.Json;

namespace DemonicMahjong.Compatibility
{
    internal static class RuntimeDetection
    {
        internal static bool NeedsMetadataHookWorkaround(string scriptingAssemblies, Version unity, Version interop)
        {
            // 只修改已实测的 Unity 6.3 + Il2CppInterop 1.5.3，避免覆盖未来框架的正式修复。
            if (unity.Major != 6000 || unity.Minor != 3
                || interop.Major != 1 || interop.Minor != 5 || interop.Build != 3)
                return false;

            using var document = JsonDocument.Parse(scriptingAssemblies);
            foreach (var name in document.RootElement.GetProperty("names").EnumerateArray())
                if (string.Equals(name.GetString(), "HybridCLR.Runtime.dll", StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}
