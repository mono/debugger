using System;
using System.Collections;
using System.Reflection;

using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoStructType : MonoType, ITargetStructType
	{
		protected readonly MonoClass Klass;
		bool is_byref;

		public MonoStructType (Type type, int size, TargetAddress klass_address,
				       TargetBinaryReader info, MonoSymbolTable table)
			: this (TargetObjectKind.Struct, type, size, klass_address, info, table, true)
		{ }

		protected MonoStructType (TargetObjectKind kind, Type type, int size, TargetAddress klass_address,
					  TargetBinaryReader info, MonoSymbolTable table, bool has_fixed_size)
			: base (kind, type, size, klass_address, has_fixed_size)
		{
			is_byref = kind == TargetObjectKind.Class;
			int klass_offset = info.ReadInt32 ();
			Klass = GetClass (type, klass_offset, table);
		}

		protected MonoStructType (TargetObjectKind kind, MonoClass klass)
			: base (kind, klass.Type, klass.InstanceSize, klass.KlassAddress, true)
		{
			is_byref = kind == TargetObjectKind.Class;
			this.Klass = klass;
		}

		public ITargetFieldInfo[] Fields {
			get {
				return Klass.Fields;
			}
		}

		internal ITargetObject GetField (TargetLocation location, int index)
		{
			return Klass.GetField (location, index);
		}

		public ITargetFieldInfo[] Properties {
			get {
				return Klass.Properties;
			}
		}

		internal ITargetObject GetProperty (TargetLocation location, int index)
		{
			return Klass.GetProperty (location, index);
		}

		public ITargetMethodInfo[] Methods {
			get {
				return Klass.Methods;
			}
		}

		internal ITargetFunctionObject GetMethod (TargetLocation location, int index)
		{
			return Klass.GetMethod (location, index);
		}

		public override bool IsByRef {
			get {
				return is_byref;
			}
		}

		public override MonoObject GetObject (TargetLocation location)
		{
			return new MonoStructObject (this, location);
		}

		public string PrintObject (TargetLocation location)
		{
			throw new NotImplementedException ();
		}
	}
}
