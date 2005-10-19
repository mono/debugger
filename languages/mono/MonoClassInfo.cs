using System;
using System.Collections;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoClassInfo : MarshalByRefObject
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
				type.File.AddressDomain, info.ReadAddress ());

			type.File.MonoLanguage.AddClass (KlassAddress, type);

			debugger_info = type.File.MonoLanguage.MonoDebuggerInfo;
		}

		public MonoClassInfo (MonoClassType type, TargetAccess target,
				      TargetAddress klass_address)
		{
			this.type = type;
			this.KlassAddress = klass_address;

			int offset = type.File.MonoLanguage.BuiltinTypes.KlassInstanceSizeOffset;
			size = target.TargetMemoryAccess.ReadInteger (klass_address + offset);

			type.File.MonoLanguage.AddClass (KlassAddress, type);

			debugger_info = type.File.MonoLanguage.MonoDebuggerInfo;

			do_initialize (target, null);
		}

		void initialize (TargetAccess target)
		{
			if (initialized)
				return;

			target.Invoke (do_initialize, null);
		}

		object do_initialize (TargetAccess target, object data)
		{
			if (type.ParentType != null) {
				parent = type.MonoParentType.GetTypeInfo ();
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
				TargetAddress address = reader.ReadAddress ();

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

		int GetFieldOffset (TargetFieldInfo field)
		{
			if (field.Position < type.FirstField)
				return parent.GetFieldOffset (field);

			return field_offsets [field.Position - type.FirstField];
		}

		internal TargetObject GetField (TargetAccess target, TargetLocation location,
						TargetFieldInfo finfo)
		{
			initialize (target);

			int offset = GetFieldOffset (finfo);
			if (!type.IsByRef)
				offset -= 2 * target.TargetMemoryInfo.TargetAddressSize;
			TargetLocation field_loc = location.GetLocationAtOffset (offset);

			if (finfo.Type.IsByRef)
				field_loc = field_loc.GetDereferencedLocation (target);

			if (field_loc.Address.IsNull)
				return null;

			return finfo.Type.GetObject (field_loc);
		}

		internal void SetField (TargetAccess target, TargetLocation location,
					TargetFieldInfo finfo, TargetObject obj)
		{
			initialize (target);

			int offset = GetFieldOffset (finfo);
			if (!type.IsByRef)
				offset -= 2 * target.TargetMemoryInfo.TargetAddressSize;
			TargetLocation field_loc = location.GetLocationAtOffset (offset);

			if (finfo.Type.IsByRef)
				field_loc = field_loc.GetDereferencedLocation (target);

			finfo.Type.SetObject (target, field_loc, obj);
		}

		internal TargetObject GetStaticField (TargetAccess target, TargetFieldInfo finfo)
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

		internal void SetStaticField (TargetAccess target, TargetFieldInfo finfo,
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

		internal TargetAddress GetMethodAddress (TargetAccess target, int token)
		{
			initialize (target);
			if (!methods.Contains (token))
				throw new InternalError ();
			return (TargetAddress) methods [token];
		}

		internal TargetAddress GetVirtualMethod (TargetAccess target, int token,
							 ref TargetClassObject instance)
		{
			TargetAddress method = GetMethodAddress (target, token);
			TargetAddress vmethod = target.CallMethod (
				debugger_info.GetVirtualMethod, instance.Location.Address, method);

			TargetAddress klass = target.TargetMemoryAccess.ReadAddress (vmethod + 8);
			TargetType class_type = type.File.MonoLanguage.GetClass (target, klass);

			if (!class_type.IsByRef) {
				TargetLocation new_loc = instance.Location.GetLocationAtOffset (
					2 * target.TargetMemoryInfo.TargetAddressSize);
				instance = (TargetClassObject) class_type.GetObject (new_loc);
			}

			return vmethod;
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

		internal MonoClassObject GetParentObject (TargetAccess target, TargetLocation location)
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

		internal MonoClassObject GetCurrentObject (TargetAccess target, TargetLocation location)
		{
			initialize (target);

			// location.Address resolves to the address of the MonoObject,
			// dereferencing it once gives us the vtable, dereferencing it
			// twice the class.
			TargetAddress address;
			address = target.TargetMemoryAccess.ReadAddress (location.Address);
			address = target.TargetMemoryAccess.ReadAddress (address);

			TargetType current = type.File.MonoLanguage.GetClass (target, address);
			return (MonoClassObject) current.GetObject (location);
		}
	}
}
