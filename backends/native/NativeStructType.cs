using System;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeFieldInfo : ITargetFieldInfo
	{
		NativeType type;
		string name;
		int index;
		int offset;

		public NativeFieldInfo (NativeType type, string name, int index, int offset)
		{
			this.type = type;
			this.name = name;
			this.index = index;
			this.offset = offset;
		}

		ITargetType ITargetFieldInfo.Type {
			get {
				return type;
			}
		}

		public NativeType Type {
			get {
				return type;
			}
		}

		public string Name {
			get {
				return name;
			}
		}

		public int Index {
			get {
				return index;
			}
		}

		public object FieldHandle {
			get {
				return null;
			}
		}

		public int Offset {
			get {
				return offset;
			}
		}
	}

	internal class NativeStructType : NativeType, ITargetStructType
	{
		NativeFieldInfo[] fields;

		public NativeStructType (string name, int size, NativeFieldInfo[] fields)
			: base (name, TargetObjectKind.Struct, size)
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

		public ITargetFieldInfo[] Properties {
			get {
				return new ITargetFieldInfo [0];
			}
		}

		public ITargetMethodInfo[] Methods {
			get {
				return new ITargetMethodInfo [0];
			}
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
