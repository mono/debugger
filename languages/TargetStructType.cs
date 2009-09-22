using System;
using System.Diagnostics;

namespace Mono.Debugger.Languages
{
	public abstract class TargetStructType : TargetType
	{
		protected TargetStructType (Language language, TargetObjectKind kind)
			: base (language, kind)
		{ }

		public abstract string BaseName {
			get;
		}

		public abstract Module Module {
			get;
		}

		public abstract bool HasParent {
			get;
		}

		#region Members

		public abstract TargetFieldInfo[] Fields {
			get;
		}

		public abstract TargetPropertyInfo[] Properties {
			get;
		}

		public abstract TargetEventInfo[] Events {
			get;
		}

		public abstract TargetMethodInfo[] Methods {
			get;
		}

		public abstract TargetMethodInfo[] Constructors {
			get;
		}

		public virtual TargetMemberInfo FindMember (string name, bool search_static,
							    bool search_instance)
		{
			foreach (TargetFieldInfo field in Fields) {
				if (field.IsStatic && !search_static)
					continue;
				if (!field.IsStatic && !search_instance)
					continue;
				if (field.Name == name)
					return field;
			}

			foreach (TargetPropertyInfo property in Properties) {
				if (property.IsStatic && !search_static)
					continue;
				if (!property.IsStatic && !search_instance)
					continue;
				if (property.Name == name)
					return property;
			}

			foreach (TargetEventInfo ev in Events) {
				if (ev.IsStatic && !search_static)
					continue;
				if (!ev.IsStatic && !search_instance)
					continue;
				if (ev.Name == name)
					return ev;
			}

			return null;
		}

		#endregion

		#region Debuggable Attributes

		public virtual bool IsCompilerGenerated {
			get { return false; }
		}  

		public virtual DebuggerDisplayAttribute DebuggerDisplayAttribute {
			get { return null; }
		}

		public virtual DebuggerTypeProxyAttribute DebuggerTypeProxyAttribute {
			get { return null; }
		}

		#endregion

		internal abstract TargetClassType GetParentType (TargetMemoryAccess target);

		public TargetClassType GetParentType (Thread thread)
		{
			return (TargetClassType) thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target) {
					return GetParentType (target);
			});
		}

		public abstract bool IsClassInitialized {
			get;
		}

		internal abstract TargetClass GetClass (TargetMemoryAccess target);

		public TargetClass GetClass (Thread thread)
		{
			return (TargetClass) thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target) {
					return GetClass (target);
			});
		}

		public virtual TargetClass ForceClassInitialization (Thread thread)
		{
			return GetClass (thread);
		}
	}
}
