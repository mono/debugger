using System;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoClassInfo : MonoTypeInfo
	{
		new public readonly MonoClassType Type;

		int[] field_offsets;
		TargetAddress[] methods;
		MonoDebuggerInfo debugger_info;
		MonoClassInfo parent;
		bool initialized;

		public MonoClassInfo (MonoClassType type, TargetBinaryReader info)
			: base (type, info)
		{
			this.Type = type;

			debugger_info = type.File.MonoLanguage.MonoDebuggerInfo;
		}

		void initialize (ITargetAccess target)
		{
			if (initialized)
				return;

			if (Type.ParentType != null) {
				parent = (MonoClassInfo) Type.ParentType.GetTypeInfo ();
				parent.initialize (target);
			}

			MonoBuiltinTypeInfo builtin = Type.File.MonoLanguage.BuiltinTypes;

			TargetAddress field_info = target.ReadAddress (KlassAddress + builtin.KlassFieldOffset);
			int field_count = Type.Fields.Length + Type.StaticFields.Length;
			ITargetMemoryReader info = target.ReadMemory (field_info, field_count * builtin.FieldInfoSize);

			field_offsets = new int [field_count];
			for (int i = 0; i < field_count; i++) {
				info.Offset = i * builtin.FieldInfoSize + 2 * target.TargetAddressSize;
				field_offsets [i] = info.ReadInteger ();
			}

			TargetAddress method_info = target.ReadAddress (KlassAddress + builtin.KlassMethodsOffset);
			int method_count = target.ReadInteger (KlassAddress + builtin.KlassMethodCountOffset);
			info = target.ReadMemory (method_info, method_count * target.TargetAddressSize);

			methods = new TargetAddress [method_count];
			for (int i = 0; i < method_count; i++)
				methods [i] = info.ReadGlobalAddress ();

			initialized = true;
		}

		public ITargetObject GetField (TargetLocation location, int index)
		{
			try {
				initialize (location.TargetAccess);

				MonoFieldInfo finfo = Type.Fields [index];
				MonoTypeInfo ftype = finfo.Type.GetTypeInfo ();
				if (ftype == null)
					return null;

				int offset = field_offsets [finfo.Position];
				if (!Type.IsByRef)
					offset -= 2 * location.TargetAccess.TargetAddressSize;
				TargetLocation field_loc = location.GetLocationAtOffset (
					offset, ftype.Type.IsByRef);

				if (field_loc.Address.IsNull)
					return null;

				return ftype.GetObject (field_loc);
			} catch (TargetException ex) {
				throw new LocationInvalidException (ex);
			}
		}

		public ITargetObject GetStaticField (StackFrame frame, int index)
		{
			try {
				initialize (frame.TargetAccess);
				TargetAddress data_address = frame.Process.CallMethod (
					debugger_info.ClassGetStaticFieldData, KlassAddress,
					TargetAddress.Null);

				MonoFieldInfo finfo = Type.StaticFields [index];
				MonoTypeInfo ftype = finfo.Type.GetTypeInfo ();
				if (ftype == null)
					return null;

				TargetLocation location = new AbsoluteTargetLocation (
					frame, data_address);
				TargetLocation field_loc = location.GetLocationAtOffset (
					field_offsets [finfo.Position], ftype.Type.IsByRef);

				return ftype.GetObject (field_loc);
			} catch (TargetException ex) {
				throw new LocationInvalidException (ex);
			}
		}

		internal ITargetFunctionObject GetMethod (TargetLocation location, int index)
		{
			try {
				initialize (location.TargetAccess);

				MonoMethodInfo minfo = Type.GetMethod (index);
				MonoTypeInfo mtype = minfo.Type.GetTypeInfo ();
				if (mtype == null)
					return null;

				return (ITargetFunctionObject) mtype.GetObject (location);
			} catch (TargetException ex) {
				throw new LocationInvalidException (ex);
			}
		}

		internal TargetAddress GetMethodAddress (ITargetAccess target, int index)
		{
			try {
				initialize (target);
				return methods [index];
			} catch (TargetException ex) {
				throw new LocationInvalidException (ex);
			}
		}

		internal ITargetObject GetProperty (TargetLocation location, int index)
		{
			try {
				initialize (location.TargetAccess);

				MonoPropertyInfo pinfo = Type.Properties [index];
				return pinfo.Get (location);
			} catch (TargetException ex) {
				throw new LocationInvalidException (ex);
			}
		}

		public override bool HasFixedSize {
			get { return true; }
		}

		public override MonoObject GetObject (TargetLocation location)
		{
			return new MonoClassObject (this, location);
		}

		internal MonoClassObject GetParentObject (TargetLocation location)
		{
			try {
				initialize (location.TargetAccess);

				if (parent == null)
					throw new InvalidOperationException ();

				return new MonoClassObject (parent, location);
			} catch (TargetException ex) {
				throw new LocationInvalidException (ex);
			}
		}				
	}
}
