using System;

namespace Mono.Debugger.Languages.Native
{
	[Serializable]
	internal abstract class NativeStructMember : ITargetMemberInfo
	{
		public readonly TargetType Type;
		public readonly string Name;
		public readonly int Index;
		public readonly bool IsStatic;

		public NativeStructMember (TargetType type, string name, int index, bool is_static)
		{
			this.Type = type;
			this.Name = name;
			this.Index = index;
			this.IsStatic = is_static;
		}

		ITargetType ITargetMemberInfo.Type {
			get {
				return Type;
			}
		}

		string ITargetMemberInfo.Name {
			get {
				return Name;
			}
		}

		int ITargetMemberInfo.Index {
			get {
				return Index;
			}
		}

		bool ITargetMemberInfo.IsStatic {
			get {
				return IsStatic;
			}
		}

		public override string ToString ()
		{
			return String.Format ("{0} ({1}:{2}:{3}:{4})",
					      GetType (), Name, Type, Index, IsStatic);
		}
	}

	[Serializable]
	internal class NativeFieldInfo : NativeStructMember, ITargetFieldInfo
	{
		int offset;
		int bit_offset, bit_size;
		bool is_bitfield;
		bool has_const_value;
		int const_value;

		public NativeFieldInfo (TargetType type, string name, int index, int offset)
			: base (type, name, index, false)
		{
			this.offset = offset;
		}

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
			: base (type, name, index, false)
		{
			this.has_const_value = has_const_value;
			this.const_value = const_value;
		}

		public bool HasConstValue {
			get {
				return has_const_value;
			}
		}

		public int ConstValue {
			get {
				if (!has_const_value)
					throw new InvalidOperationException ();

				return const_value;
			}
		}

		public ITargetObject GetConstValue (ITargetAccess target)
		{
			return null;
		}

		public int Offset {
			get {
				return offset;
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

	internal class NativeMethodInfo  : NativeStructMember, ITargetMethodInfo
	{
		public new readonly NativeFunctionType Type;

		public NativeMethodInfo (string name, int index, NativeFunctionType function_type)
			: base (function_type, name, index, false)
		{
			this.Type = function_type;
		}

		ITargetFunctionType ITargetMethodInfo.Type {
			get {
				return Type;
			}
		}

		string ITargetMethodInfo.FullName {
			get {
				return Name;
			}
		}
	}

	internal class NativeStructType : TargetType, ITargetStructType
	{
		string name;
		int size;
		NativeFieldInfo[] fields;

		internal NativeStructType (ILanguage language, string name, int size)
			: base (language, TargetObjectKind.Struct)
		{
			this.name = name;
			this.size = size;
		}

		internal NativeStructType (ILanguage language, string name,
					   NativeFieldInfo[] fields, int size)
			: this (language, name, size)
		{
			this.fields = fields;
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
			get {
				return false;
			}
		}

		public ITargetFieldInfo[] Fields {
			get {
				return fields;
			}
		}

		public ITargetFieldInfo[] StaticFields {
			get {
				return new ITargetFieldInfo [0];
			}
		}

		public ITargetObject GetStaticField (ITargetAccess target, int index)
		{
			throw new InvalidOperationException ();
		}

		public void SetStaticField (ITargetAccess target, int index, ITargetObject obj)
		{
			throw new InvalidOperationException ();
		}

		public ITargetPropertyInfo[] Properties {
			get {
				return new ITargetPropertyInfo [0];
			}
		}

		public ITargetPropertyInfo[] StaticProperties {
			get {
				return new ITargetPropertyInfo [0];
			}
		}

		public ITargetObject GetStaticProperty (ITargetAccess target, int index)
		{
			throw new InvalidOperationException ();
		}

		public ITargetEventInfo[] Events {
			get {
				return new ITargetEventInfo [0];
			}
		}

		public ITargetEventInfo[] StaticEvents {
			get {
				return new ITargetEventInfo [0];
			}
		}

		public ITargetObject GetStaticEvent (StackFrame frame, int index)
		{
			throw new InvalidOperationException ();
		}

		public ITargetMethodInfo[] Methods {
			get {
				return new ITargetMethodInfo [0];
			}
		}

		public ITargetMethodInfo[] StaticMethods {
			get {
				return new ITargetMethodInfo [0];
			}
		}

		public ITargetMethodInfo[] Constructors {
			get {
				return new ITargetMethodInfo [0];
			}
		}

		public ITargetMethodInfo[] StaticConstructors {
			get {
				return new ITargetMethodInfo [0];
			}
		}

		public bool ResolveClass (ITargetAccess target)
		{
			return true;
		}

		internal override TargetObject GetObject (TargetLocation location)
		{
			return new NativeStructObject (this, location);
		}

		internal TargetObject GetField (TargetLocation location, int index)
		{
			NativeFieldInfo field = fields [index];

			TargetLocation field_loc = location.GetLocationAtOffset (field.Offset);

			if (field.Type.IsByRef)
				field_loc = field_loc.GetDereferencedLocation (
					location.TargetAccess);

			if (!field.Type.IsByRef && field.IsBitfield)
				field_loc = new BitfieldTargetLocation (
					field_loc, field.BitOffset, field.BitSize);

			return field.Type.GetObject (field_loc);
		}

		internal void SetField (TargetLocation location, int index, TargetObject obj)
		{
			NativeFieldInfo field = fields [index];

			TargetLocation field_loc = location.GetLocationAtOffset (field.Offset);

			if (field.Type.IsByRef)
				field_loc = field_loc.GetDereferencedLocation (
					location.TargetAccess);

			if (!field.Type.IsByRef && field.IsBitfield)
				field_loc = new BitfieldTargetLocation (
					field_loc, field.BitOffset, field.BitSize);

			// field.Type.SetObject (field_loc, obj);
			throw new NotImplementedException ();
		}

		public ITargetMemberInfo FindMember (string name, bool search_static,
						     bool search_instance)
		{
			if (search_static) {
				foreach (ITargetFieldInfo field in StaticFields)
					if (field.Name == name)
						return field;

				foreach (ITargetPropertyInfo property in StaticProperties)
					if (property.Name == name)
						return property;

				foreach (ITargetEventInfo ev in StaticEvents)
					if (ev.Name == name)
						return ev;
			}

			if (search_instance) {
				foreach (ITargetFieldInfo field in Fields)
					if (field.Name == name)
						return field;

				foreach (ITargetPropertyInfo property in Properties)
					if (property.Name == name)
						return property;

				foreach (ITargetEventInfo ev in Events)
					if (ev.Name == name)
						return ev;
			}

			return null;
		}
	}
}
