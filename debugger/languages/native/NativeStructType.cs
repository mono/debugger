using System;

namespace Mono.Debugger.Languages.Native
{
	[Serializable]
	internal class NativeFieldInfo : TargetFieldInfo
	{
		int bit_offset, bit_size;
		bool is_bitfield;
		int const_value;

		public NativeFieldInfo (TargetType type, string name, int index, int offset)
			: base (type, name, index, false, 0, offset, false)
		{ }

		public NativeFieldInfo (TargetType type, string name, int index, int offset,
					int bit_offset, int bit_size)
			: this (type, name, index, offset)
		{
			this.bit_offset = bit_offset;
			this.bit_size = bit_size;
			this.is_bitfield = true;
		}

		public NativeFieldInfo (TargetType type, string name, int index,
					bool has_const_value, int const_value)
			: base (type, name, index, false, 0, 0, has_const_value)
		{
			this.const_value = const_value;
		}

		public override object ConstValue {
			get {
				if (!HasConstValue)
					throw new InvalidOperationException ();

				return const_value;
			}
		}

		public int BitOffset {
			get {
				return bit_offset;
			}
		}

		public int BitSize {
			get {
				return bit_size;
			}
		}

		public bool IsBitfield {
			get {
				return is_bitfield;
			}
		}
	}

	internal class NativeMethodInfo  : TargetMethodInfo
	{
		public new readonly NativeFunctionType FunctionType;

		public NativeMethodInfo (NativeFunctionType type, string name, int index)
			: base (type, name, index, false, name)
		{
			this.FunctionType = type;
		}
	}

	internal class NativeStructType : TargetClassType
	{
		string name;
		int size;
		NativeFieldInfo[] fields;

		internal NativeStructType (Language language, string name, int size)
			: base (language, TargetObjectKind.Struct)
		{
			this.name = name;
			this.size = size;
		}

		internal NativeStructType (Language language, string name,
					   NativeFieldInfo[] fields, int size)
			: this (language, name, size)
		{
			this.fields = fields;
		}

		public override bool HasParent {
			get { return false; }
		}

		public override TargetClassType ParentType {
			get { throw new InvalidOperationException (); }
		}

		public override Module Module {
			get { throw new NotImplementedException (); }
		}

		internal void SetFields (NativeFieldInfo[] fields)
		{
			this.fields = fields;
		}

		public override string Name {
			get { return name; }
		}

		public override int Size {
			get { return size; }
		}

		public override bool HasFixedSize {
			get { return true; }
		}

		public override bool IsByRef {
			get { return false; }
		}

		public bool IsCompleteType {
			get { return fields != null; }
		}

		public override TargetFieldInfo[] Fields {
			get {
				if (fields != null)
					return fields;

				return new TargetFieldInfo [0];
			}
		}

		public override TargetFieldInfo[] StaticFields {
			get {
				return new TargetFieldInfo [0];
			}
		}

		public override TargetObject GetStaticField (Thread target,
							     TargetFieldInfo field)
		{
			throw new InvalidOperationException ();
		}

		public override void SetStaticField (Thread target, TargetFieldInfo field,
						     TargetObject obj)
		{
			throw new InvalidOperationException ();
		}

		public override TargetPropertyInfo[] Properties {
			get {
				return new TargetPropertyInfo [0];
			}
		}

		public override TargetPropertyInfo[] StaticProperties {
			get {
				return new TargetPropertyInfo [0];
			}
		}

		public override TargetEventInfo[] Events {
			get {
				return new TargetEventInfo [0];
			}
		}

		public override TargetEventInfo[] StaticEvents {
			get {
				return new TargetEventInfo [0];
			}
		}

		public override TargetMethodInfo[] Methods {
			get {
				return new TargetMethodInfo [0];
			}
		}

		public override TargetMethodInfo[] StaticMethods {
			get {
				return new TargetMethodInfo [0];
			}
		}

		public override TargetMethodInfo[] Constructors {
			get {
				return new TargetMethodInfo [0];
			}
		}

		public override TargetMethodInfo[] StaticConstructors {
			get {
				return new TargetMethodInfo [0];
			}
		}

		internal override TargetObject GetObject (TargetLocation location)
		{
			return new NativeStructObject (this, location);
		}

		internal TargetObject GetField (Thread target, TargetLocation location,
						NativeFieldInfo field)
		{
			TargetLocation field_loc = location.GetLocationAtOffset (field.Offset);

			if (field.Type.IsByRef)
				field_loc = field_loc.GetDereferencedLocation (target);

			if (!field.Type.IsByRef && field.IsBitfield)
				field_loc = new BitfieldTargetLocation (
					field_loc, field.BitOffset, field.BitSize);

			return field.Type.GetObject (field_loc);
		}

		internal void SetField (Thread target, TargetLocation location,
					NativeFieldInfo field, TargetObject obj)
		{
			TargetLocation field_loc = location.GetLocationAtOffset (field.Offset);

			if (field.Type.IsByRef)
				field_loc = field_loc.GetDereferencedLocation (target);

			if (!field.Type.IsByRef && field.IsBitfield)
				field_loc = new BitfieldTargetLocation (
					field_loc, field.BitOffset, field.BitSize);

			// field.Type.SetObject (field_loc, obj);
			throw new NotImplementedException ();
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
	}
}
