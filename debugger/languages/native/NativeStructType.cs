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
		NativeClass class_info;

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

		public override bool HasClassType {
			get { return true; }
		}

		public override TargetClassType ClassType {
			get { return this; }
		}

		public override bool HasParent {
			get { return false; }
		}

		internal override TargetStructType GetParentType (TargetMemoryAccess target)
		{
			throw new InvalidOperationException ();
		}

		public override Module Module {
			get { throw new NotImplementedException (); }
		}

		internal void SetFields (NativeFieldInfo[] fields)
		{
			this.fields = fields;
		}

		public override bool IsCompilerGenerated {
			get { return false; }
		}

		public override string BaseName {
			get { return name; }
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

		public override TargetPropertyInfo[] Properties {
			get {
				return new TargetPropertyInfo [0];
			}
		}

		public override TargetEventInfo[] Events {
			get {
				return new TargetEventInfo [0];
			}
		}

		public override TargetMethodInfo[] Methods {
			get {
				return new TargetMethodInfo [0];
			}
		}

		public override TargetMethodInfo[] Constructors {
			get {
				return new TargetMethodInfo [0];
			}
		}

		internal override TargetClass GetClass (TargetMemoryAccess target)
		{
			if (class_info == null)
				class_info = new NativeClass (this, fields);

			return class_info;
		}

		protected override TargetObject DoGetObject (TargetMemoryAccess target, TargetLocation location)
		{
			return new NativeStructObject (this, location);
		}

		internal TargetObject GetField (TargetMemoryAccess target, TargetLocation location,
						NativeFieldInfo field)
		{
			TargetLocation field_loc = location.GetLocationAtOffset (field.Offset);

			if (field.Type.IsByRef)
				field_loc = field_loc.GetDereferencedLocation ();

			if (!field.Type.IsByRef && field.IsBitfield)
				field_loc = new BitfieldTargetLocation (
					field_loc, field.BitOffset, field.BitSize);

			return field.Type.GetObject (target, field_loc);
		}

		internal void SetField (TargetMemoryAccess target, TargetLocation location,
					NativeFieldInfo field, TargetObject obj)
		{
			TargetLocation field_loc = location.GetLocationAtOffset (field.Offset);

			if (field.Type.IsByRef)
				field_loc = field_loc.GetDereferencedLocation ();

			if (!field.Type.IsByRef && field.IsBitfield)
				field_loc = new BitfieldTargetLocation (
					field_loc, field.BitOffset, field.BitSize);

			// field.Type.SetObject (field_loc, obj);
			throw new NotImplementedException ();
		}
	}
}
