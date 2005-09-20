using System;
using System.Collections;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoClassInfo : MarshalByRefObject
	{
		protected readonly MonoClassType type;
		protected readonly int size;
		protected readonly TargetAddress KlassAddress;

		int[] field_offsets;
		Hashtable methods;
		MonoDebuggerInfo debugger_info;
		MonoClassInfo parent;
		bool initialized;

		public MonoClassInfo (MonoClassType type, TargetBinaryReader info)
		{
			this.type = type;

			size = info.ReadLeb128 ();
			KlassAddress = new TargetAddress (
				type.File.GlobalAddressDomain, info.ReadAddress ());

			type.File.MonoLanguage.AddClass (KlassAddress, type);

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
			if (type.ParentType != null) {
				parent = (MonoClassInfo) type.ParentType.GetTypeInfo ();
				parent.initialize (target);
			}

			MonoBuiltinTypeInfo builtin = Type.File.MonoLanguage.BuiltinTypes;

			TargetAddress field_info = target.TargetMemoryAccess.ReadAddress (
				KlassAddress + builtin.KlassFieldOffset);
			int field_count = type.Fields.Length + type.StaticFields.Length;
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

			methods = new Hashtable ();
			TargetReader reader = new TargetReader (blob.Contents, target.TargetMemoryInfo);
			for (int i = 0; i < method_count; i++) {
				TargetAddress address = reader.ReadGlobalAddress ();

				TargetBlob method_blob = target.TargetMemoryAccess.ReadMemory (
					address + 4, 4);
				int token = method_blob.GetReader ().ReadInt32 ();
				if (token == 0)
					continue;

				methods.Add (token, address);
			}

			initialized = true;
			return null;
		}

		public ITargetObject GetField (TargetLocation location, int index)
		{
			try {
				initialize (location.TargetAccess);

				MonoFieldInfo finfo = type.Fields [index];

				int offset = field_offsets [finfo.Position];
				if (!type.IsByRef)
					offset -= 2 * location.TargetMemoryInfo.TargetAddressSize;
				TargetLocation field_loc = location.GetLocationAtOffset (
					offset, finfo.Type.IsByRef);

				if (field_loc.Address.IsNull)
					return null;

				return finfo.Type.GetObject (field_loc);
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

				MonoFieldInfo finfo = type.StaticFields [index];

				TargetLocation location = new AbsoluteTargetLocation (
					null, target, data_address);
				TargetLocation field_loc = location.GetLocationAtOffset (
					field_offsets [finfo.Position], finfo.Type.IsByRef);

				return finfo.Type.GetObject (field_loc);
			} catch (TargetException ex) {
				throw new LocationInvalidException (ex);
			}
		}

		internal TargetAddress GetMethodAddress (ITargetAccess target, int token)
		{
			try {
				initialize (target);
				if (!methods.Contains (token))
					throw new InternalError ();
				return (TargetAddress) methods [token];
			} catch (TargetException ex) {
				throw new LocationInvalidException (ex);
			}
		}

		public MonoClassType Type {
			get { return type; }
		}

		public int Size {
			get { return size; }
		}

		public MonoObject GetObject (TargetLocation location)
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
