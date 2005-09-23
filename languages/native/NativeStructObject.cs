using System;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeStructObject : TargetObject, ITargetStructObject
	{
		new NativeStructType type;

		public NativeStructObject (NativeStructType type, TargetLocation location)
			: base (type, location)
		{
			this.type = type;
		}

		public new ITargetStructType Type {
			get {
				return type;
			}
		}

		public ITargetObject GetField (int index)
		{
			return type.GetField (location, index);
		}

		public void SetField (int index, ITargetObject obj)
		{
			type.SetField (location, index, (TargetObject) obj);
		}

		public ITargetObject GetProperty (int index)
		{
			throw new InvalidOperationException ();
		}

		public ITargetObject GetEvent (int index)
		{
			throw new InvalidOperationException ();
		}

		public string PrintObject ()
		{
			throw new InvalidOperationException ();
		}

		public ITargetObject InvokeMethod (int index, params ITargetObject[] args)
		{
			throw new InvalidOperationException ();
		}

		protected override long GetDynamicSize (TargetBlob blob, TargetLocation location,
							out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}
	}
}

