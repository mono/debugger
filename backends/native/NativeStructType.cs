using System;
using Mono.Debugger.Backends;

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

		object ITargetMemberInfo.Handle {
			get {
				return null;
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

		public NativeFieldInfo (NativeType type, string name, int index, int offset)
			: base (type, name, index, false)
		{
			this.offset = offset;
		}

		public int Offset {
			get {
				return offset;
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

		public ITargetFunctionObject GetStaticMethod (StackFrame frame, int index)
		{
			throw new InvalidOperationException ();
		}

		public ITargetFunctionObject GetConstructor (StackFrame frame, int index)
		{
			throw new InvalidOperationException ();
		}

		public override NativeObject GetObject (TargetLocation location)
		{
			return new NativeStructObject (this, location);
		}

		internal NativeObject GetField (TargetLocation location, int index)
		{
			TargetLocation field_loc = location.GetLocationAtOffset (
				fields [index].Offset, fields [index].Type.IsByRef);

			return fields [index].Type.GetObject (field_loc);
		}
	}
}
