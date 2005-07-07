using System;

namespace Mono.Debugger.Languages.Native
{
	internal abstract class NativeStructMember : ITargetMemberInfo
	{
		public readonly NativeType Type;
		public readonly string Name;
		public readonly int Index;
		public readonly bool IsStatic;

		public NativeStructMember (NativeType type, string name, int index, bool is_static)
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

	internal class NativeFieldInfo : NativeStructMember, ITargetFieldInfo
	{
		int offset;
		int bit_offset, bit_size;
		bool is_bitfield;
		bool has_const_value;
		int const_value;

		public NativeFieldInfo (NativeType type, string name, int index, int offset)
			: base (type, name, index, false)
		{
			this.offset = offset;
		}

		public NativeFieldInfo (NativeType type, string name, int index, int offset,
					int bit_offset, int bit_size)
			: this (type, name, index, offset)
		{
			this.bit_offset = bit_offset;
			this.bit_size = bit_size;
			this.is_bitfield = true;
		}

		public NativeFieldInfo (NativeType type, string name, int index,
					bool has_const_value, int const_value)
			: base (type, name, index, false)
		{
			this.has_const_value = has_const_value;
			this.const_value = const_value;
		}

		public bool HasConstValue {
			get {
			  Console.WriteLine ("HasConstvalue = {0}", has_const_value);
				return has_const_value;
			}
		}

		public ITargetObject GetConstValue (StackFrame frame) {
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

	internal class NativeStructType : NativeType, ITargetStructType
	{
		NativeFieldInfo[] fields;

		internal NativeStructType (string name, int size)
			: base (name, TargetObjectKind.Struct, size)
		{ }

		internal NativeStructType (string name, NativeFieldInfo[] fields, int size)
			: this (name, size)
		{
			this.fields = fields;
		}

		internal void SetFields (NativeFieldInfo[] fields)
		{
			this.fields = fields;
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

		public ITargetObject GetStaticField (StackFrame frame, int index)
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

		public ITargetObject GetStaticProperty (StackFrame frame, int index)
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

		public ITargetFunctionObject GetStaticMethod (StackFrame frame, int index)
		{
			throw new InvalidOperationException ();
		}

		public ITargetFunctionObject GetConstructor (StackFrame frame, int index)
		{
			throw new InvalidOperationException ();
		}

		public ITargetFunctionObject GetStaticConstructor (StackFrame frame, int index)
		{
			throw new InvalidOperationException ();
		}

		public override NativeObject GetObject (TargetLocation location)
		{
			return new NativeStructObject (this, location);
		}

		internal NativeObject GetField (TargetLocation location, int index)
		{
			NativeFieldInfo field = fields [index];

			TargetLocation field_loc = location.GetLocationAtOffset (
				field.Offset, field.Type.IsByRef);

			if (!field.Type.IsByRef && field.IsBitfield)
				field_loc = new BitfieldTargetLocation (
					field_loc, field.BitOffset, field.BitSize);

			return field.Type.GetObject (field_loc);
		}
	}
}
