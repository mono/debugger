using System;
using Mono.CSharp.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoStructObject : MonoObject, ITargetStructObject
	{
		new MonoStructType type;

		public MonoStructObject (MonoStructType type, MonoTargetLocation location)
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

		public string PrintObject ()
		{
			return type.PrintObject (location);
		}

		public ITargetObject InvokeMethod (int index, params ITargetObject[] arguments)
		{
			return type.InvokeMethod (location, index, arguments);
		}

		public new ITargetStructType Type {
			get {
				return type;
			}
		}

		protected override long GetDynamicSize (ITargetMemoryReader reader,
							MonoTargetLocation location,
							out MonoTargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}
	}
}
