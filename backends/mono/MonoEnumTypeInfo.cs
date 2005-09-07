using System;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoEnumTypeInfo : MonoTypeInfo
	{
		new public readonly MonoEnumType Type;

		int[] member_offsets;
		MonoDebuggerInfo debugger_info;
		bool initialized;

		public MonoEnumTypeInfo (MonoEnumType type, TargetBinaryReader info)
			: base (type, info)
		{
			this.Type = type;

			debugger_info = type.File.MonoLanguage.MonoDebuggerInfo;
		}

		void initialize (ITargetAccess target)
		{
			if (initialized)
				return;

			MonoBuiltinTypeInfo builtin = Type.File.MonoLanguage.BuiltinTypes;

			TargetAddress member_info = target.TargetMemoryAccess.ReadAddress (
				KlassAddress + builtin.KlassFieldOffset);
			int member_count = 1 + Type.Members.Length;
			TargetBinaryReader info = target.TargetMemoryAccess.ReadMemory (
				member_info, member_count * builtin.FieldInfoSize).GetReader ();

			member_offsets = new int [member_count];
			for (int i = 0; i < member_count; i++) {
				info.Position = i * builtin.FieldInfoSize +
					2 * info.TargetInfo.TargetAddressSize;
				member_offsets [i] = info.ReadInt32 ();
			}

			initialized = true;
		}

		public ITargetObject GetValue (TargetLocation location)
		{
			try {
				initialize (location.TargetAccess);

				MonoFieldInfo finfo = Type.Value;
				IMonoTypeInfo ftype = finfo.Type.GetTypeInfo ();
				if (ftype == null)
					return null;

				int offset = member_offsets [finfo.Position];
				if (!Type.IsByRef)
					offset -= 2 * location.TargetMemoryInfo.TargetAddressSize;
				TargetLocation member_loc = location.GetLocationAtOffset (
					offset, ftype.Type.IsByRef);

				return ftype.GetObject (member_loc);
			} catch (TargetException ex) {
				throw new LocationInvalidException (ex);
			}
		}

		public ITargetObject GetMember (StackFrame frame, int index)
		{
			try {
				initialize (frame.TargetAccess);
				TargetAddress data_address = frame.Process.CallMethod (
					debugger_info.ClassGetStaticFieldData, KlassAddress,
					TargetAddress.Null);

				MonoFieldInfo finfo = Type.Members [index];
				IMonoTypeInfo ftype = finfo.Type.GetTypeInfo ();
				if (ftype == null)
					return null;

				TargetLocation location = new AbsoluteTargetLocation (
					frame, data_address);
				TargetLocation member_loc = location.GetLocationAtOffset (
					member_offsets [finfo.Position], ftype.Type.IsByRef);

				return ftype.GetObject (member_loc);
			} catch (TargetException ex) {
				throw new LocationInvalidException (ex);
			}
		}

		public override MonoObject GetObject (TargetLocation location)
		{
			return new MonoEnumObject (this, location);
		}
	}
}
