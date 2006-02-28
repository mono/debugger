using System;
using System.Runtime.Serialization;

using Mono.Debugger.Backends;
using Mono.Debugger.Languages;
using Mono.Debugger.Remoting;

namespace Mono.Debugger
{
	public class CatchpointHandle : EventHandle
	{
		TargetType exception;
		bool enabled;

		internal CatchpointHandle (Thread target, ThreadGroup group,
					   TargetType exception)
			: base (group, exception.Name, Breakpoint.GetNextBreakpointIndex ())
		{
			this.exception = exception;

			Enable (target);
		}

		public override bool IsEnabled {
			get { return enabled; }
		}

		public override void Enable (Thread target)
		{
			lock (this) {
				EnableCatchpoint (target);
			}
		}

		public override void Disable (Thread target)
		{
			lock (this) {
				DisableCatchpoint (target);
			}
		}

		public override void Remove (Thread target)
		{
			Disable (target);
		}

		void EnableCatchpoint (Thread target)
		{
			lock (this) {
				if (enabled)
					return;

				target.AddEventHandler (EventType.CatchException, this);
				enabled = true;
			}
		}

		void DisableCatchpoint (Thread target)
		{
			lock (this) {
				if (enabled)
					target.RemoveEventHandler (Index);

				enabled = false;
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

		public override bool CheckBreakpointHit (Thread target, TargetAddress address)
		{
			TargetClassObject exc = exception.Language.CreateObject (target, address)
				as TargetClassObject;
			if (exc == null)
				return false; // OOOPS

			return IsSubclassOf (exc.Type, exception);
		}

		protected override void GetSessionData (SerializationInfo info)
		{
			base.GetSessionData (info);
			info.AddValue ("exception", exception.Name);
		}

		protected override void SetSessionData (SerializationInfo info, DebuggerClient client)
		{
			base.SetSessionData (info, client);

#if FIXME
			Language language = client.DebuggerServer.MonoLanguage;
			exception = language.LookupType (info.GetString ("exception"));
#endif
		}
	}
}
