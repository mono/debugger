using System;
using Mono.CSharp.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoStructObject : MonoObject, ITargetStructObject
	{
		new MonoStructType type;

		public MonoStructObject (MonoStructType type, TargetLocation location)
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

		public ITargetFunctionObject GetMethod (int index)
		{
			return type.GetMethod (location, index);
		}

		public string PrintObject ()
		{
			return type.PrintObject (location);
		}

		public new ITargetStructType Type {
			get {
				return type;
			}
		}

		protected override long GetDynamicSize (ITargetMemoryReader reader,
							TargetLocation location,
							out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}
	}
}
