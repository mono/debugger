using System;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoObjectType : MonoType
	{
		public MonoObjectType (Type type, int size, ITargetMemoryReader info)
			: base (type, size, true)
		{
		}

		public override bool IsByRef {
			get {
				return true;
			}
		}

		public override bool HasObject {
			get {
				return true;
			}
		}

		public override MonoObject GetObject (ITargetLocation location)
		{
			ITargetMemoryAccess memory;
			TargetAddress address = GetAddress (location, out memory);

			Console.WriteLine ("OBJECT: {0}", address);
			address = memory.ReadAddress (address);
			Console.WriteLine ("VTABLE: {0}", address);
			address = memory.ReadAddress (address);
			Console.WriteLine ("KLASS: {0}", address);

			return new MonoOpaqueObject (this, location);
		}
	}
}
