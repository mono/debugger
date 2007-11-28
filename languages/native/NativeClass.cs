using System;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeClass : TargetClass
	{
		NativeStructType type;
		NativeFieldInfo[] fields;

		internal NativeClass (NativeStructType type, NativeFieldInfo[] fields)
		{
			this.type = type;
			this.fields = fields != null ? fields : new NativeFieldInfo [0];
		}

		public override TargetClassType Type {
			get { return type; }
		}

		public override bool HasParent {
			get { return false; }
		}

		public override TargetClass GetParent (Thread thread)
		{
			throw new InvalidOperationException ();
		}

		public override TargetFieldInfo[] GetFields (Thread thread)
		{
			return fields;
		}

		public override TargetObject GetField (Thread thread,
						       TargetClassObject instance,
						       TargetFieldInfo field)
		{
			if (field.HasConstValue)
				return type.Language.CreateInstance (thread, field.ConstValue);

			return (TargetObject) thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target)  {
					return GetField (target, instance, field);
			});
		}

		internal TargetObject GetField (TargetMemoryAccess target,
						TargetClassObject instance,
						TargetFieldInfo field)
		{
			TargetLocation field_loc = instance.Location.GetLocationAtOffset (field.Offset);

			if (field.Type.IsByRef)
				field_loc = field_loc.GetDereferencedLocation ();

			NativeFieldInfo nfield = (NativeFieldInfo) field;
			if (!field.Type.IsByRef && nfield.IsBitfield)
				field_loc = new BitfieldTargetLocation (
					field_loc, nfield.BitOffset, nfield.BitSize);

			return field.Type.GetObject (target, field_loc);
		}

		public override void SetField (Thread thread, TargetClassObject instance,
					       TargetFieldInfo field, TargetObject value)
		{
			TargetLocation field_loc = instance.Location.GetLocationAtOffset (field.Offset);

			if (field.Type.IsByRef)
				field_loc = field_loc.GetDereferencedLocation ();

			NativeFieldInfo nfield = (NativeFieldInfo) field;
			if (!field.Type.IsByRef && nfield.IsBitfield)
				field_loc = new BitfieldTargetLocation (
					field_loc, nfield.BitOffset, nfield.BitSize);

			// field.Type.SetObject (field_loc, value);
			throw new NotImplementedException ();
		}

#if FIXME
		public override TargetPropertyInfo[] Properties {
			get { return new TargetPropertyInfo [0]; }
		}

		public override TargetPropertyInfo[] StaticProperties {
			get { return new TargetPropertyInfo [0]; }
		}

		public override TargetEventInfo[] Events {
			get { return new TargetEventInfo [0]; }
		}

		public override TargetEventInfo[] StaticEvents {
			get { return new TargetEventInfo [0]; }
		}

		public override TargetMethodInfo[] Methods {
			get { return new TargetMethodInfo [0]; }
		}

		public override TargetMethodInfo[] StaticMethods {
			get { return new TargetMethodInfo [0]; }
		}

		public override TargetMethodInfo[] Constructors {
			get { return new TargetMethodInfo [0]; }
		}

		public override TargetMethodInfo[] StaticConstructors {
			get { return new TargetMethodInfo [0]; }
		}

		public override TargetMemberInfo FindMember (string name, bool search_static,
							     bool search_instance)
		{
			if (search_static) {
				foreach (TargetFieldInfo field in StaticFields)
					if (field.Name == name)
						return field;

				foreach (TargetPropertyInfo property in StaticProperties)
					if (property.Name == name)
						return property;

				foreach (TargetEventInfo ev in StaticEvents)
					if (ev.Name == name)
						return ev;
			}

			if (search_instance) {
				foreach (TargetFieldInfo field in Fields)
					if (field.Name == name)
						return field;

				foreach (TargetPropertyInfo property in Properties)
					if (property.Name == name)
						return property;

				foreach (TargetEventInfo ev in Events)
					if (ev.Name == name)
						return ev;
			}

			return null;
		}
#endif
	}
}
