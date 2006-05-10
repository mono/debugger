using System;
using System.Collections;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoClassInfo : DebuggerMarshalByRefObject
	{
		protected readonly MonoClassType type;
		protected readonly int size;
		internal readonly TargetAddress KlassAddress;

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
				info.TargetInfo.AddressDomain, info.ReadAddress ());

			type.File.MonoLanguage.AddClass (KlassAddress, type);

			debugger_info = type.File.MonoLanguage.MonoDebuggerInfo;
		}

		public MonoClassInfo (MonoClassType type, Thread target,
				      TargetAddress klass_address)
		{
			this.type = type;
			this.KlassAddress = klass_address;

			MonoLanguageBackend mono = type.File.MonoLanguage;

			int offset = mono.MonoMetadataInfo.KlassInstanceSizeOffset;
			size = target.ReadInteger (klass_address + offset);

			type.File.MonoLanguage.AddClass (KlassAddress, type);

			debugger_info = mono.MonoDebuggerInfo;

			if (type.ParentType != null) {
				TargetAddress parent_klass = target.ReadAddress (
					klass_address + mono.MonoMetadataInfo.KlassParentOffset);
				type.MonoParentType.ClassResolved (target, parent_klass);
			}

			do_initialize (target, null);
		}

		void initialize (Thread target)
		{
			if (initialized)
				return;

			target.ThreadServant.Invoke (do_initialize, null);
		}

		object do_initialize (Thread target, object data)
		{
			if (type.ParentType != null) {
				parent = type.MonoParentType.GetTypeInfo ();
				parent.do_initialize (target, data);
			}

			MonoMetadataInfo metadata_info = Type.File.MonoLanguage.MonoMetadataInfo;

			TargetAddress field_info = target.ReadAddress (
				KlassAddress + metadata_info.KlassFieldOffset);
			int field_count = type.Fields.Length + type.StaticFields.Length;
			TargetBinaryReader info = target.ReadMemory (
				field_info, field_count * metadata_info.FieldInfoSize).GetReader ();

			field_offsets = new int [field_count];
			for (int i = 0; i < field_count; i++) {
				info.Position = i * metadata_info.FieldInfoSize +
					2 * target.TargetInfo.TargetAddressSize;
				field_offsets [i] = info.ReadInt32 ();
			}

			TargetAddress method_info = target.ReadAddress (
				KlassAddress + metadata_info.KlassMethodsOffset);
			int method_count = target.ReadInteger (
				KlassAddress + metadata_info.KlassMethodCountOffset);
			TargetBlob blob = target.ReadMemory (
				method_info, method_count * target.TargetInfo.TargetAddressSize);

			methods = new Hashtable ();
			TargetReader reader = new TargetReader (blob.Contents, target.TargetInfo);
			for (int i = 0; i < method_count; i++) {
				TargetAddress address = reader.ReadAddress ();

				TargetBlob method_blob = target.ReadMemory (
					address + 4, 4);
				int token = method_blob.GetReader ().ReadInt32 ();
				if (token == 0)
					continue;

				methods.Add (token, address);
			}

			initialized = true;
			return null;
		}

		int GetFieldOffset (TargetFieldInfo field)
		{
			if (field.Position < type.FirstField)
				return parent.GetFieldOffset (field);

			return field_offsets [field.Position - type.FirstField];
		}

		internal TargetObject GetField (Thread target, TargetLocation location,
						TargetFieldInfo finfo)
		{
			initialize (target);

			int offset = GetFieldOffset (finfo);
			if (!type.IsByRef)
				offset -= 2 * target.TargetInfo.TargetAddressSize;
			TargetLocation field_loc = location.GetLocationAtOffset (offset);

			if (finfo.Type.IsByRef)
				field_loc = field_loc.GetDereferencedLocation (target);

			if (field_loc.Address.IsNull)
				return null;

			return finfo.Type.GetObject (field_loc);
		}

		internal void SetField (Thread target, TargetLocation location,
					TargetFieldInfo finfo, TargetObject obj)
		{
			initialize (target);

			int offset = GetFieldOffset (finfo);
			if (!type.IsByRef)
				offset -= 2 * target.TargetInfo.TargetAddressSize;
			TargetLocation field_loc = location.GetLocationAtOffset (offset);

			if (finfo.Type.IsByRef)
				field_loc = field_loc.GetDereferencedLocation (target);

			finfo.Type.SetObject (target, field_loc, obj);
		}

		internal TargetObject GetStaticField (Thread target, TargetFieldInfo finfo)
		{
			initialize (target);

			TargetAddress data_address = target.CallMethod (
				debugger_info.ClassGetStaticFieldData, KlassAddress,
				TargetAddress.Null);

			int offset = GetFieldOffset (finfo);

			TargetLocation location = new AbsoluteTargetLocation (data_address);
			TargetLocation field_loc = location.GetLocationAtOffset (offset);

			if (finfo.Type.IsByRef)
				field_loc = field_loc.GetDereferencedLocation (target);

			return finfo.Type.GetObject (field_loc);
		}

		internal void SetStaticField (Thread target, TargetFieldInfo finfo,
					      TargetObject obj)
		{
			initialize (target);

			int offset = GetFieldOffset (finfo);

			TargetAddress data_address = target.CallMethod (
				debugger_info.ClassGetStaticFieldData, KlassAddress,
				TargetAddress.Null);

			TargetLocation location = new AbsoluteTargetLocation (data_address);
			TargetLocation field_loc = location.GetLocationAtOffset (offset);

			if (finfo.Type.IsByRef)
				field_loc = field_loc.GetDereferencedLocation (target);

			finfo.Type.SetObject (target, field_loc, obj);
		}

		internal TargetAddress GetMethodAddress (Thread target, int token)
		{
			initialize (target);
			if (!methods.Contains (token))
				throw new InternalError ();
			return (TargetAddress) methods [token];
		}

		public MonoClassType Type {
			get { return type; }
		}

		public int Size {
			get { return size; }
		}

		public TargetObject GetObject (TargetLocation location)
		{
			return new MonoClassObject (this, location);
		}

		internal MonoClassObject GetParentObject (Thread target, TargetLocation location)
		{
			initialize (target);

			if (parent == null)
				throw new InvalidOperationException ();

			if (!type.IsByRef && parent.Type.IsByRef) {
				TargetAddress boxed = target.CallMethod (
					debugger_info.GetBoxedObjectMethod,
					KlassAddress, location.Address);
				TargetLocation new_loc = new AbsoluteTargetLocation (boxed);
				return new MonoClassObject (parent, new_loc);
			}

			return new MonoClassObject (parent, location);
		}

		internal MonoClassObject GetCurrentObject (Thread target, TargetLocation location)
		{
			initialize (target);

			// location.Address resolves to the address of the MonoObject,
			// dereferencing it once gives us the vtable, dereferencing it
			// twice the class.
			TargetAddress address;
			address = target.ReadAddress (location.Address);
			address = target.ReadAddress (address);

			TargetType current = type.File.MonoLanguage.GetClass (target, address);
			return (MonoClassObject) current.GetObject (location);
		}
	}
}
