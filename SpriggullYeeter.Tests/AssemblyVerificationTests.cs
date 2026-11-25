using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Xunit;

namespace SpriggullYeeter.Tests
{
    public sealed class AssemblyVerificationTests
    {
        private static string FindRepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "SpriggullYeeter.sln")))
                    return dir;
                dir = Path.GetDirectoryName(dir);
            }

            throw new InvalidOperationException("Could not locate SpriggullYeeter.sln root directory.");
        }

        [Fact]
        public void CompiledModDll_ResolvesRagdollLibAndGameReferencesCleanly()
        {
            string repoRoot = FindRepoRoot();
            string releaseDir = Path.Combine(repoRoot, "SpriggullYeeter", "bin", "Release", "net472");
            string modDll = Path.Combine(releaseDir, "SpriggullYeeter.dll");
            Assert.True(File.Exists(modDll), $"Release SpriggullYeeter.dll not found at '{modDll}'.");

            // Ensure RagdollLib.dll is NOT bundled into the release output directory.
            Assert.False(File.Exists(Path.Combine(releaseDir, "RagdollLib.dll")));

            string gameRoot = Path.GetFullPath(Path.Combine(repoRoot, "..", ".."));
            string rootTownshipPath = Path.Combine(gameRoot, "A Township Tale_Data", "Managed", "Root.Township.dll");
            string ragdollLibPath = Path.Combine(gameRoot, "Mods", "RagdollLib.dll");
            Assert.True(File.Exists(rootTownshipPath), $"Root.Township.dll not found at '{rootTownshipPath}'.");
            Assert.True(File.Exists(ragdollLibPath), $"RagdollLib.dll not found at '{ragdollLibPath}'.");

            using var modAsm = AssemblyDefinition.ReadAssembly(modDll);
            using var gameAsm = AssemblyDefinition.ReadAssembly(rootTownshipPath);
            using var ragdollAsm = AssemblyDefinition.ReadAssembly(ragdollLibPath);

            // 1. Verify expected references (including RagdollLib) and no accidental test/NuGet assemblies.
            string[] allowedPrefixes =
            {
                "mscorlib",
                "System",
                "netstandard",
                "MelonLoader",
                "0Harmony",
                "RagdollLib",
                "Root.Township",
                "Alta.",
                "UnityEngine"
            };

            foreach (AssemblyNameReference reference in modAsm.MainModule.AssemblyReferences)
            {
                Assert.True(
                    allowedPrefixes.Any(p => reference.Name.StartsWith(p, StringComparison.Ordinal)),
                    $"Unexpected assembly reference '{reference.Name}' in SpriggullYeeter.dll.");
            }

            Assert.Contains(modAsm.MainModule.AssemblyReferences, r => r.Name == "RagdollLib");

            // 2. Verify MelonLoader assembly attributes including hard dependency on RagdollLib.
            Assert.Contains(modAsm.CustomAttributes, a => a.AttributeType.Name == "MelonInfoAttribute");
            Assert.Contains(modAsm.CustomAttributes, a => a.AttributeType.Name == "MelonGameAttribute");
            CustomAttribute depAttr = modAsm.CustomAttributes.FirstOrDefault(
                a => a.AttributeType.Name == "MelonAdditionalDependenciesAttribute");
            Assert.NotNull(depAttr);

            // 3. Verify all RagdollLib and Root.Township type references resolve.
            foreach (TypeReference tr in modAsm.MainModule.GetTypeReferences())
            {
                if (tr.Scope is not AssemblyNameReference scope)
                    continue;

                if (scope.Name == "Root.Township")
                {
                    TypeDefinition resolved = gameAsm.MainModule.GetType(tr.FullName);
                    Assert.True(resolved != null, $"Referenced type '{tr.FullName}' missing in Root.Township.dll.");
                }
                else if (scope.Name == "RagdollLib")
                {
                    TypeDefinition resolved = ragdollAsm.MainModule.GetType(tr.FullName);
                    Assert.True(resolved != null, $"Referenced type '{tr.FullName}' missing in RagdollLib.dll.");
                }
            }

            // 4. Verify all member references on Root.Township and RagdollLib resolve.
            foreach (MemberReference mr in modAsm.MainModule.GetMemberReferences())
            {
                if (mr.DeclaringType?.Scope is not AssemblyNameReference scope)
                    continue;

                AssemblyDefinition targetAsm = scope.Name switch
                {
                    "Root.Township" => gameAsm,
                    "RagdollLib" => ragdollAsm,
                    _ => null
                };

                if (targetAsm == null)
                    continue;

                TypeDefinition decl = targetAsm.MainModule.GetType(mr.DeclaringType.FullName);
                Assert.NotNull(decl);

                if (mr is MethodReference methodRef)
                {
                    bool exists = decl.Methods.Any(m =>
                        m.Name == methodRef.Name &&
                        m.Parameters.Count == methodRef.Parameters.Count);
                    Assert.True(exists, $"Referenced method '{decl.FullName}.{methodRef.Name}({methodRef.Parameters.Count})' missing in {scope.Name}.");
                }
                else if (mr is FieldReference fieldRef)
                {
                    bool exists = decl.Fields.Any(f => f.Name == fieldRef.Name);
                    Assert.True(exists, $"Referenced field '{decl.FullName}.{fieldRef.Name}' missing in {scope.Name}.");
                }
            }

            // 5. Verify Harmony patch targets and private reflected fields.
            AssertMethodExists(gameAsm, "Alta.Networking.NetworkRigidbody", "ClientUpdate", 0);
            AssertMethodExists(gameAsm, "Alta.Networking.NetworkRigidbody", "ApplySerializedValues", 1);
            AssertMethodExists(gameAsm, "Alta.Pathfinding.Walker", "FixedUpdate", 0);
            AssertMethodExists(gameAsm, "Alta.Intelligence.Agent", "ChangeAction", 0);
            AssertMethodExists(gameAsm, "Alta.Intelligence.Spriggull", "BeginFlail", 1);
            AssertMethodExists(gameAsm, "HealthObject", "OnKilled", 0);
            AssertMethodExists(gameAsm, "Alta.Networking.NetworkEntity", "OnDestroyedByScene", 1);
            AssertMethodExists(gameAsm, "Alta.Networking.NetworkEntity", "OnDestroy", 0);
            AssertMethodExists(gameAsm, "PooledObject", "OnReleased", 0);

            AssertFieldExists(gameAsm, "HandGestureController", "gripAxis");
            AssertFieldExists(gameAsm, "Alta.Intelligence.Spriggull", "beak");
        }

        private static void AssertMethodExists(AssemblyDefinition asm, string typeName, string methodName, int paramCount)
        {
            TypeDefinition type = asm.MainModule.GetType(typeName);
            Assert.True(type != null, $"Type '{typeName}' not found.");
            Assert.True(
                type.Methods.Any(m => m.Name == methodName && m.Parameters.Count == paramCount),
                $"Method '{typeName}.{methodName}({paramCount})' not found.");
        }

        private static void AssertFieldExists(AssemblyDefinition asm, string typeName, string fieldName)
        {
            TypeDefinition type = asm.MainModule.GetType(typeName);
            Assert.True(type != null, $"Type '{typeName}' not found.");
            Assert.True(
                type.Fields.Any(f => f.Name == fieldName),
                $"Field '{typeName}.{fieldName}' not found.");
        }
    }
}
