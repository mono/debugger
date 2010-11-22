using System;
using Mono.Debugger;
using Mono.Debugger.Backend;
using Mono.Cecil;
using Mono.Cecil.Metadata;
using C = Mono.CompilerServices.SymbolWriter;

namespace Mono.Debugger.Languages.Mono
{
	internal static class MonoDebuggerSupport
	{
		public static int GetMethodToken (Cecil.MethodDefinition method)
		{
			return method.MetadataToken.ToInt32 ();
		}

		public static Cecil.MethodDefinition GetMethod (Cecil.ModuleDefinition module, int token)
		{
			return (Cecil.MethodDefinition) module.LookupToken (
				new Cecil.MetadataToken (Cecil.TokenType.Method, token & 0xffffff));
		}
	}
}
