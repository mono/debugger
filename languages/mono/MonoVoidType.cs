using System;
using Cecil = Mono.Cecil;

using Mono.Debugger.Backend;
using Mono.Debugger.Backend.Mono;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoVoidType : TargetType
	{
		MonoSymbolFile file;
		Cecil.TypeDefinition typedef;
		MonoClassType class_type;

		protected MonoVoidType (MonoSymbolFile file, Cecil.TypeDefinition typedef)
			: base (file.MonoLanguage, TargetObjectKind.Unknown)
		{
			this.file = file;
			this.typedef = typedef;
		}

		public static MonoVoidType Create (MonoSymbolFile corlib, TargetMemoryAccess memory)
		{
			MonoVoidType type = new MonoVoidType (
				corlib, corlib.ModuleDefinition.GetType ("System.Void"));

			TargetAddress klass = corlib.MonoLanguage.MetadataHelper.GetVoidClass (memory);
			type.create_type (memory, klass);

			return type;
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

		public Cecil.TypeReference Type {
			get { return typedef; }
		}

		public override string Name {
			get { return typedef.FullName; }
		}

		public override bool IsByRef {
			get { return false; }
		}

		public override bool HasFixedSize {
			get { return false; }
		}

		public override int Size {
			get { return 0; }
		}

		public override bool ContainsGenericParameters {
			get { return false; }
		}

		protected override TargetObject DoGetObject (TargetMemoryAccess target,
							     TargetLocation location)
		{
			throw new TargetException (TargetError.LocationInvalid,
						   "Cannot access variables of type `{0}'", Name);
		}
	}
}
