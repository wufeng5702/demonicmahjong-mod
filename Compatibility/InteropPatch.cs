using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace DemonicMahjong.Compatibility
{
    internal static class InteropPatch
    {
        internal static IEnumerable<CodeInstruction> SkipMetadataHook(IEnumerable<CodeInstruction> instructions)
        {
            var codes = instructions.ToList();
            int matches = 0;
            for (int i = 0; i + 1 < codes.Count; i++)
            {
                if (codes[i].opcode != OpCodes.Ldsfld
                    || codes[i].operand is not FieldInfo field
                    || field.Name != "GetTypeInfoFromTypeDefinitionIndexHook"
                    || (codes[i + 1].opcode != OpCodes.Call && codes[i + 1].opcode != OpCodes.Callvirt)
                    || codes[i + 1].operand is not MethodInfo method || method.Name != "ApplyHook")
                    continue;

                // 保留分支标签与异常块，只移除这个不兼容的原生钩子；其他注入路径不变。
                codes[i].opcode = OpCodes.Nop;
                codes[i].operand = null;
                codes[i + 1].opcode = OpCodes.Nop;
                codes[i + 1].operand = null;
                matches++;
            }
            if (matches != 1)
                throw new InvalidOperationException("Il2CppInterop Setup changed; refusing to patch metadata hook (matches=" + matches + ").");
            return codes;
        }
    }
}
