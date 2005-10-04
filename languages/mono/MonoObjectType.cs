using System;
using Cecil = Mono.Cecil;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoObjectType : TargetPointerType
	{
		MonoSymbolFile file;

		public MonoObjectType (MonoSymbolFile file, Cecil.ITypeDefinition typedef, int size)
			: base (file.MonoLanguage, "object", size)
		{
			this.file = file;
		}

		public MonoSymbolFile File {
			get { return file; }
		}

		public override bool IsTypesafe {
			get { return true; }
		}

		public override bool HasStaticType {
			get { return false; }
		}

		public override bool IsArray {
			get { return false; }
		}

		public override TargetType StaticType {
			get {
				throw new InvalidOperationException ();
			}
		}

		internal override TargetObject GetObject (TargetLocation location)
		{
			return new MonoObjectObject (this, location);
		}
	}
}
