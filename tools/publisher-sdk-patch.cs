#:package Mono.Cecil@0.11.6
// Makes a COPY of the game's ModPublisher work on Linux.
//
//   dotnet run tools/publisher-sdk-patch.cs -- <copy>/PDX.SDK.dll
//
// Paradox's SDK, which the publisher uses, registers DiskIODefaultWindows as
// its disk service: its file work goes through kernel32, which Linux does not
// have, so page updates and new versions fail when the SDK cleans up its
// folders. The same SDK carries DiskIODefault, which does that work with .NET's
// own System.IO. This rewrites the one registration to use it, in the copy
// given; tools/publish.sh makes the copy in its temporary folder, so the game's
// files are never touched. What the publisher sends is unchanged.
using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length != 1 || !File.Exists(args[0]))
{
    Console.Error.WriteLine("usage: dotnet run tools/publisher-sdk-patch.cs -- <copy>/PDX.SDK.dll");
    return 1;
}
string path = Path.GetFullPath(args[0]);
var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(path)!);
using ModuleDefinition module = ModuleDefinition.ReadModule(path, new ReaderParameters { ReadWrite = true, AssemblyResolver = resolver });
TypeDefinition? windows = module.GetType("PDX.SDK.Internal.Util.IO.DiskIODefaultWindows");
TypeDefinition? portable = module.GetType("PDX.SDK.Internal.Util.IO.DiskIODefault");
if (windows == null || portable == null)
{
    Console.Error.WriteLine("PDX.SDK.dll has no DiskIODefaultWindows or DiskIODefault: a different SDK version");
    return 1;
}
int patched = 0;
foreach (MethodDefinition method in module.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody))
{
    foreach (Instruction instruction in method.Body.Instructions)
    {
        if (instruction.Operand is GenericInstanceMethod call && call.Name == "Register" && call.GenericArguments.Count == 2
            && call.GenericArguments[1].FullName == windows.FullName)
        {
            call.GenericArguments[1] = portable;
            patched++;
        }
    }
}
if (patched != 1)
{
    Console.Error.WriteLine($"expected one registration of DiskIODefaultWindows, found {patched}: a different SDK version");
    return 1;
}
module.Write();
Console.WriteLine("PDX.SDK.dll: disk service DiskIODefaultWindows -> DiskIODefault");
return 0;
