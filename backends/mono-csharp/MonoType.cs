using System;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoType : ITargetType
	{
		protected Type type;
		protected int size;

		protected MonoType (Type type, int size)
		{
			this.type = type;
			this.size = size;
		}

		public static MonoType GetType (Type type, int size)
		{
			Console.WriteLine ("TEST: {0} {1}", type, size);

			if (MonoFundamentalType.Supports (type))
				return new MonoFundamentalType (type, size);

			return new MonoType (type, size);
		}

		public object TypeHandle {
			get {
				return type;
			}
		}

		public int Size {
			get {
				return size;
			}
		}

		public virtual bool HasObject {
			get {
				return false;
			}
		}

		public virtual object GetObject (ITargetMemoryReader reader)
		{
			throw new InvalidOperationException ();
		}
	}
}
