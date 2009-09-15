using System;
using System.Runtime.InteropServices;

using Mono.Debugger.Backend;
using Mono.Debugger.Backend.Mono;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoFundamentalType : TargetFundamentalType
	{
		MonoSymbolFile file;
		MonoClassType class_type;
		Cecil.TypeDefinition typedef;

		protected MonoFundamentalType (MonoSymbolFile file, Cecil.TypeDefinition typedef,
					       string name, FundamentalKind kind, int size)
			: base (file.Language, name, kind, size)
		{
			this.file = file;
			this.typedef = typedef;
		}

		public static MonoFundamentalType Create (MonoSymbolFile corlib,
							  TargetMemoryAccess memory,
							  FundamentalKind kind)
		{
			MonoFundamentalType fundamental;
			TargetAddress klass;

			switch (kind) {
			case FundamentalKind.Boolean:
				klass = corlib.MonoLanguage.MonoRuntime.GetBooleanClass (memory);
				fundamental = new MonoFundamentalType (
					corlib, corlib.ModuleDefinition.Types ["System.Boolean"],
					"bool", kind, 1);
				break;

			case FundamentalKind.Char:
				klass = corlib.MonoLanguage.MonoRuntime.GetCharClass (memory);
				fundamental = new MonoFundamentalType (
					corlib, corlib.ModuleDefinition.Types ["System.Char"],
					"char", kind, 2);
				break;

			case FundamentalKind.SByte:
				klass = corlib.MonoLanguage.MonoRuntime.GetSByteClass (memory);
				fundamental = new MonoFundamentalType (
					corlib, corlib.ModuleDefinition.Types ["System.SByte"],
					"sbyte", kind, 1);
				break;

			case FundamentalKind.Byte:
				klass = corlib.MonoLanguage.MonoRuntime.GetByteClass (memory);
				fundamental = new MonoFundamentalType (
					corlib, corlib.ModuleDefinition.Types ["System.Byte"],
					"byte", kind, 1);
				break;

			case FundamentalKind.Int16:
				klass = corlib.MonoLanguage.MonoRuntime.GetInt16Class (memory);
				fundamental = new MonoFundamentalType (
					corlib, corlib.ModuleDefinition.Types ["System.Int16"],
					"short", kind, 2);
				break;

			case FundamentalKind.UInt16:
				klass = corlib.MonoLanguage.MonoRuntime.GetUInt16Class (memory);
				fundamental = new MonoFundamentalType (
					corlib, corlib.ModuleDefinition.Types ["System.UInt16"],
					"ushort", kind, 2);
				break;

			case FundamentalKind.Int32:
				klass = corlib.MonoLanguage.MonoRuntime.GetInt32Class (memory);
				fundamental = new MonoFundamentalType (
					corlib, corlib.ModuleDefinition.Types ["System.Int32"],
					"int", kind, 4);
				break;

			case FundamentalKind.UInt32:
				klass = corlib.MonoLanguage.MonoRuntime.GetUInt32Class (memory);
				fundamental = new MonoFundamentalType (
					corlib, corlib.ModuleDefinition.Types ["System.UInt32"],
					"uint", kind, 4);
				break;

			case FundamentalKind.Int64:
				klass = corlib.MonoLanguage.MonoRuntime.GetInt64Class (memory);
				fundamental = new MonoFundamentalType (
					corlib, corlib.ModuleDefinition.Types ["System.Int64"],
					"long", kind, 8);
				break;

			case FundamentalKind.UInt64:
				klass = corlib.MonoLanguage.MonoRuntime.GetUInt64Class (memory);
				fundamental = new MonoFundamentalType (
					corlib, corlib.ModuleDefinition.Types ["System.UInt64"],
					"ulong", kind, 8);
				break;

			case FundamentalKind.Single:
				klass = corlib.MonoLanguage.MonoRuntime.GetSingleClass (memory);
				fundamental = new MonoFundamentalType (
					corlib, corlib.ModuleDefinition.Types ["System.Single"],
					"float", kind, 4);
				break;

			case FundamentalKind.Double:
				klass = corlib.MonoLanguage.MonoRuntime.GetDoubleClass (memory);
				fundamental = new MonoFundamentalType (
					corlib, corlib.ModuleDefinition.Types ["System.Double"],
					"double", kind, 8);
				break;

			case FundamentalKind.IntPtr:
				klass = corlib.MonoLanguage.MonoRuntime.GetIntPtrClass (memory);
				fundamental = new MonoFundamentalType (
					corlib, corlib.ModuleDefinition.Types ["System.IntPtr"],
					"System.IntPtr", kind, memory.TargetMemoryInfo.TargetAddressSize);
				break;

			case FundamentalKind.UIntPtr:
				klass = corlib.MonoLanguage.MonoRuntime.GetUIntPtrClass (memory);
				fundamental = new MonoFundamentalType (
					corlib, corlib.ModuleDefinition.Types ["System.UIntPtr"],
					"System.UIntPtr", kind, memory.TargetMemoryInfo.TargetAddressSize);
				break;

			case FundamentalKind.Decimal:
				fundamental = new MonoFundamentalType (
					corlib, corlib.ModuleDefinition.Types ["System.Decimal"],
					"decimal", kind, Marshal.SizeOf (typeof (Decimal)));
				return fundamental;

			default:
				throw new InternalError ();
			}

			fundamental.create_type (memory, klass);
			return fundamental;
		}

		internal void SetClass (MonoClassType info)
		{
			this.class_type = info;
		}

		protected void create_type (TargetMemoryAccess memory, TargetAddress klass)
		{
			class_type = file.MonoLanguage.CreateCoreType (file, typedef, memory, klass);
			file.MonoLanguage.AddCoreType (typedef, this, class_type, klass);
		}

		public override bool HasClassType {
			get { return true; }
		}

		public override TargetClassType ClassType {
			get { return class_type; }
		}

		internal MonoClassType MonoClassType {
			get { return class_type; }
		}
	}
}
