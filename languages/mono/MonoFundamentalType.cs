using System;
using Mono.Debugger.Backends;

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
							  TargetReader mono_defaults,
							  FundamentalKind kind)
		{
			MonoFundamentalType fundamental;

			int offset;
			int address_size = memory.TargetInfo.TargetAddressSize;
			MonoMetadataInfo metadata = corlib.MonoLanguage.MonoMetadataInfo;

			switch (kind) {
			case FundamentalKind.Boolean:
				offset = metadata.MonoDefaultsBooleanOffset;
				fundamental = new MonoFundamentalType (
					corlib, corlib.ModuleDefinition.Types ["System.Boolean"],
					"bool", kind, 1);
				break;

			case FundamentalKind.Char:
				offset = metadata.MonoDefaultsCharOffset;
				fundamental = new MonoFundamentalType (
					corlib, corlib.ModuleDefinition.Types ["System.Char"],
					"char", kind, 2);
				break;

			case FundamentalKind.SByte:
				offset = metadata.MonoDefaultsSByteOffset;
				fundamental = new MonoFundamentalType (
					corlib, corlib.ModuleDefinition.Types ["System.SByte"],
					"sbyte", kind, 1);
				break;

			case FundamentalKind.Byte:
				offset = metadata.MonoDefaultsByteOffset;
				fundamental = new MonoFundamentalType (
					corlib, corlib.ModuleDefinition.Types ["System.Byte"],
					"byte", kind, 1);
				break;

			case FundamentalKind.Int16:
				offset = metadata.MonoDefaultsInt16Offset;
				fundamental = new MonoFundamentalType (
					corlib, corlib.ModuleDefinition.Types ["System.Int16"],
					"short", kind, 2);
				break;

			case FundamentalKind.UInt16:
				offset = metadata.MonoDefaultsUInt16Offset;
				fundamental = new MonoFundamentalType (
					corlib, corlib.ModuleDefinition.Types ["System.UInt16"],
					"ushort", kind, 2);
				break;

			case FundamentalKind.Int32:
				offset = metadata.MonoDefaultsInt32Offset;
				fundamental = new MonoFundamentalType (
					corlib, corlib.ModuleDefinition.Types ["System.Int32"],
					"int", kind, 4);
				break;

			case FundamentalKind.UInt32:
				offset = metadata.MonoDefaultsUInt32Offset;
				fundamental = new MonoFundamentalType (
					corlib, corlib.ModuleDefinition.Types ["System.UInt32"],
					"uint", kind, 4);
				break;

			case FundamentalKind.Int64:
				offset = metadata.MonoDefaultsInt64Offset;
				fundamental = new MonoFundamentalType (
					corlib, corlib.ModuleDefinition.Types ["System.Int64"],
					"long", kind, 8);
				break;

			case FundamentalKind.UInt64:
				offset = metadata.MonoDefaultsUInt64Offset;
				fundamental = new MonoFundamentalType (
					corlib, corlib.ModuleDefinition.Types ["System.UInt64"],
					"ulong", kind, 8);
				break;

			case FundamentalKind.Single:
				offset = metadata.MonoDefaultsSingleOffset;
				fundamental = new MonoFundamentalType (
					corlib, corlib.ModuleDefinition.Types ["System.Single"],
					"float", kind, 4);
				break;

			case FundamentalKind.Double:
				offset = metadata.MonoDefaultsDoubleOffset;
				fundamental = new MonoFundamentalType (
					corlib, corlib.ModuleDefinition.Types ["System.Double"],
					"double", kind, 8);
				break;

			case FundamentalKind.IntPtr:
				offset = metadata.MonoDefaultsIntOffset;
				fundamental = new MonoFundamentalType (
					corlib, corlib.ModuleDefinition.Types ["System.IntPtr"],
					"System.IntPtr", kind, memory.TargetInfo.TargetAddressSize);
				break;

			case FundamentalKind.UIntPtr:
				offset = metadata.MonoDefaultsUIntOffset;
				fundamental = new MonoFundamentalType (
					corlib, corlib.ModuleDefinition.Types ["System.UIntPtr"],
					"System.UIntPtr", kind, memory.TargetInfo.TargetAddressSize);
				break;

			default:
				throw new InternalError ();
			}

			TargetAddress klass = mono_defaults.PeekAddress (offset);
			fundamental.create_type (memory, klass);
			return fundamental;
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
