using System;
using Mono.CSharp.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoStructObject : MonoObject, ITargetStructObject
	{
		new MonoStructType type;

		public MonoStructObject (MonoStructType type, ITargetLocation location)
			: base (type, location)
		{
			this.type = type;
		}

		public ITargetObject GetField (int index)
		{
			return type.GetField (location, index);
		}

		public ITargetObject GetProperty (int index)
		{
			return type.GetProperty (location, index);
		}

		public override bool HasObject {
			get {
				return false;
			}
		}

		public new ITargetStructType Type {
			get {
				return type;
			}
		}

		bool ITargetObject.HasObject {
			get {
				return false;
			}
		}

		protected override long GetDynamicSize (ITargetMemoryReader reader, TargetAddress address,
							out TargetAddress dynamic_address)
		{
			throw new InvalidOperationException ();
		}

		protected override object GetObject (ITargetMemoryReader reader, TargetAddress address)
		{
			throw new InvalidOperationException ();
		}
	}
}
