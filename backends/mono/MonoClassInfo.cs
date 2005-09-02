using System;
using Mono.Debugger.Backends;

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

			target.Invoke (do_initialize, null);
		}

		object do_initialize (ITargetAccess target, object data)
		{
			if (Type.ParentType != null) {
				parent = (MonoClassInfo) Type.ParentType.GetTypeInfo ();
				parent.initialize (target);
			}

			MonoBuiltinTypeInfo builtin = Type.File.MonoLanguage.BuiltinTypes;

			TargetAddress field_info = target.TargetMemoryAccess.ReadAddress (
				KlassAddress + builtin.KlassFieldOffset);
			int field_count = Type.Fields.Length + Type.StaticFields.Length;
			TargetBinaryReader info = target.TargetMemoryAccess.ReadMemory (
				field_info, field_count * builtin.FieldInfoSize).GetReader ();

			field_offsets = new int [field_count];
			for (int i = 0; i < field_count; i++) {
				info.Position = i * builtin.FieldInfoSize +
					2 * target.TargetMemoryInfo.TargetAddressSize;
				field_offsets [i] = info.ReadInt32 ();
			}

			TargetAddress method_info = target.TargetMemoryAccess.ReadAddress (
				KlassAddress + builtin.KlassMethodsOffset);
			int method_count = target.TargetMemoryAccess.ReadInteger (
				KlassAddress + builtin.KlassMethodCountOffset);
			TargetBlob blob = target.TargetMemoryAccess.ReadMemory (
				method_info, method_count * target.TargetMemoryInfo.TargetAddressSize);

			methods = new TargetAddress [method_count];
			TargetReader reader = new TargetReader (blob.Contents, target.TargetMemoryInfo);
			for (int i = 0; i < method_count; i++)
				methods [i] = reader.ReadGlobalAddress ();

			initialized = true;
			return null;
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
					offset -= 2 * location.TargetMemoryInfo.TargetAddressSize;
				TargetLocation field_loc = location.GetLocationAtOffset (
					offset, ftype.Type.IsByRef);

				if (field_loc.Address.IsNull)
					return null;

				return ftype.GetObject (field_loc);
			} catch (TargetException ex) {
				throw new LocationInvalidException (ex);
			}
		}

		public ITargetObject GetStaticField (ITargetAccess target, int index)
		{
			try {
				initialize (target);
				TargetAddress data_address = target.CallMethod (
					debugger_info.ClassGetStaticFieldData, KlassAddress,
					TargetAddress.Null);

				MonoFieldInfo finfo = Type.StaticFields [index];
				MonoTypeInfo ftype = finfo.Type.GetTypeInfo ();
				if (ftype == null)
					return null;

				TargetLocation location = new AbsoluteTargetLocation (
					null, target, data_address);
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
