using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

// 用法：patcher <输入程序集> <输出程序集> <类型全名::方法名>...
if (args.Length < 3)
{
	Console.Error.WriteLine("usage: patcher <in.dll> <out.dll> <Type::Method>...");
	return 1;
}
var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(args[0])));
using var asm = AssemblyDefinition.ReadAssembly(args[0], new ReaderParameters { AssemblyResolver = resolver });
foreach (string target in args.Skip(2))
{
	string[] parts = target.Split("::");
	MethodDefinition? method = asm.MainModule.GetTypes().FirstOrDefault(t => t.FullName == parts[0])?.Methods.FirstOrDefault(m => m.Name == parts[1]);
	if (method == null)
	{
		Console.Error.WriteLine($"not found: {target}");
		return 2;
	}
	method.Body.Instructions.Clear();
	method.Body.ExceptionHandlers.Clear();
	method.Body.Variables.Clear();
	method.Body.GetILProcessor().Emit(OpCodes.Ret);
	Console.WriteLine($"stubbed {method.FullName}");
}
asm.Write(args[1]);
return 0;
