using System;
using System.Xml;
using Mono.Debugger.Backends;
using System.Runtime.Serialization;

using Mono.Debugger.Languages;

namespace Mono.Debugger
{
	[Serializable]
	public sealed class ExceptionCatchPoint : Event
	{
		int handle = -1;

		internal ExceptionCatchPoint (ThreadGroup group, TargetType exception)
			: base (EventType.CatchException, exception.Name, group)
		{
			this.exception = exception;
		}

		internal override void Enable (Thread target)
		{
			lock (this) {
				EnableCatchpoint (target);
			}
		}

		internal override void Disable (Thread target)
		{
			lock (this) {
				DisableCatchpoint (target);
			}
		}

		internal override void OnTargetExited ()
		{
			handle = -1;
		}

		public override void Remove (Thread target)
		{
			Disable (target);
		}

		void EnableCatchpoint (Thread target)
		{
			lock (this) {
				if (handle > 0)
					return;

				handle = target.AddEventHandler (this);
			}
		}

		void DisableCatchpoint (Thread target)
		{
			lock (this) {
				if (handle > 0)
					target.RemoveEventHandler (handle);

				handle = -1;
			}
		}

		bool IsSubclassOf (TargetClassType type, TargetType parent)
		{
			while (type != null) {
				if (type == parent)
					return true;

				if (!type.HasParent)
					return false;

				type = type.ParentType;
			}

			return false;
		}

		internal bool CheckException (Thread target, TargetAddress address)
		{
			TargetClassObject exc = exception.Language.CreateObject (target, address)
				as TargetClassObject;
			if (exc == null)
				return false; // OOOPS

			return IsSubclassOf (exc.Type, exception);
		}

		protected override void GetSessionData (XmlElement element, XmlElement root)
		{ }

		TargetType exception;
	}
}
