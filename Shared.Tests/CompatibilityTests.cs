using System;
using System.Linq;
using System.Reflection.Emit;
using DemonicMahjong.Compatibility;
using HarmonyLib;
using Xunit;

namespace Shared.Tests
{
    public class CompatibilityTests
    {
        private const string HybridClrAssemblies = "{\"names\":[\"MaJiang.dll\",\"HybridCLR.Runtime.dll\"]}";

        [Theory]
        [InlineData("6000.3.21", "1.5.3.0", true)]
        [InlineData("6000.3.0", "1.5.3.0", true)]
        [InlineData("6000.2.12", "1.5.3.0", false)]
        [InlineData("6000.4.0", "1.5.3.0", false)]
        [InlineData("6000.3.21", "1.6.0.0", false)]
        [InlineData("6000.3.21", "2.0.0.0", false)]
        public void WorkaroundIsLimitedToAffectedRuntime(string unity, string interop, bool expected)
        {
            Assert.Equal(expected, RuntimeDetection.NeedsMetadataHookWorkaround(HybridClrAssemblies,
                Version.Parse(unity), Version.Parse(interop)));
        }

        [Theory]
        [InlineData("{\"names\":[\"MaJiang.dll\"]}", false)]
        [InlineData("{\"names\":[\"OtherHybridCLR.Runtime.dll\"]}", false)]
        [InlineData("{\"names\":[],\"comment\":\"HybridCLR.Runtime.dll\"}", false)]
        [InlineData("{\"names\":[\"hybridclr.runtime.dll\"]}", true)]
        public void DetectionUsesExactAssemblyName(string assemblies, bool expected)
        {
            Assert.Equal(expected, RuntimeDetection.NeedsMetadataHookWorkaround(assemblies,
                new Version(6000, 3, 21), new Version(1, 5, 3, 0)));
        }

        [Fact]
        public void OnlyMetadataHookIsRemoved()
        {
            var other = new CodeInstruction(OpCodes.Call, typeof(Hook).GetMethod(nameof(Hook.Other)));
            var codes = MetadataHookCall().Append(other).Append(new CodeInstruction(OpCodes.Ret)).ToArray();
            var patched = InteropPatch.SkipMetadataHook(codes).ToArray();
            Assert.Equal(OpCodes.Nop, patched[0].opcode);
            Assert.Null(patched[0].operand);
            Assert.Equal(OpCodes.Nop, patched[1].opcode);
            Assert.Null(patched[1].operand);
            Assert.Same(other, patched[2]);
            Assert.Equal(OpCodes.Ret, patched[3].opcode);
        }

        [Fact]
        public void BranchLabelsAndExceptionBlocksArePreserved()
        {
            var codes = MetadataHookCall();
            var label = new DynamicMethod("LabelTest", typeof(void), Type.EmptyTypes).GetILGenerator().DefineLabel();
            codes[0].labels.Add(label);
            codes[1].blocks.Add(new ExceptionBlock(ExceptionBlockType.EndExceptionBlock));
            var patched = InteropPatch.SkipMetadataHook(codes).ToArray();
            Assert.Contains(label, patched[0].labels);
            Assert.Single(patched[1].blocks);
        }

        [Fact]
        public void ChangedSetupFailsInsteadOfGuessing()
        {
            Assert.Throws<InvalidOperationException>(() => InteropPatch.SkipMetadataHook(new[] { new CodeInstruction(OpCodes.Ret) }));
            Assert.Throws<InvalidOperationException>(() => InteropPatch.SkipMetadataHook(MetadataHookCall().Concat(MetadataHookCall())));
        }

        private static CodeInstruction[] MetadataHookCall() => new[]
        {
            new CodeInstruction(OpCodes.Ldsfld, typeof(Hook).GetField(nameof(Hook.GetTypeInfoFromTypeDefinitionIndexHook))),
            new CodeInstruction(OpCodes.Callvirt, typeof(Hook).GetMethod(nameof(Hook.ApplyHook)))
        };

        public class Hook
        {
            public static Hook GetTypeInfoFromTypeDefinitionIndexHook = new Hook();
            public void ApplyHook() { }
            public static void Other() { }
        }
    }
}
