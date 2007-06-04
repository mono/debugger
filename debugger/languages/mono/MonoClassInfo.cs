using System;
using System.Collections;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoClassInfo : DebuggerMarshalByRefObject
	{
		public readonly int Token;
		public readonly TargetAddress KlassAddress;
		public readonly TargetAddress GenericContainer;
		public readonly TargetAddress GenericClass;

		protected readonly int[] field_offsets;
		protected readonly Hashtable methods;
		protected readonly MonoClassInfo parent;

		internal MonoClassInfo (TargetMemoryAccess target, MonoLanguageBackend mono,
					int token, TargetAddress klass_address)
		{
			this.KlassAddress = klass_address;
			this.Token = token;

			TargetAddress parent_klass = target.ReadAddress (
				KlassAddress + mono.MonoMetadataInfo.KlassParentOffset);
			if (!parent_klass.IsNull)
				parent = mono.GetClassInfo (target, parent_klass);

			GenericContainer = target.ReadAddress (
				KlassAddress + mono.MonoMetadataInfo.KlassGenericContainerOffset);
			GenericClass = target.ReadAddress (
				KlassAddress + mono.MonoMetadataInfo.KlassGenericClassOffset);

			TargetAddress field_info = target.ReadAddress (
				KlassAddress + mono.MonoMetadataInfo.KlassFieldOffset);
			int field_count = target.ReadInteger (
				KlassAddress + mono.MonoMetadataInfo.KlassFieldCountOffset);

			TargetBinaryReader field_blob = target.ReadMemory (
				field_info, field_count * mono.MonoMetadataInfo.FieldInfoSize).GetReader ();

			field_offsets = new int [field_count];
			for (int i = 0; i < field_count; i++) {
				field_blob.Position = i * mono.MonoMetadataInfo.FieldInfoSize +
					2 * target.TargetInfo.TargetAddressSize;
				field_offsets [i] = field_blob.ReadInt32 ();
			}

			TargetAddress method_info = target.ReadAddress (
				KlassAddress + mono.MonoMetadataInfo.KlassMethodsOffset);
			int method_count = target.ReadInteger (
				KlassAddress + mono.MonoMetadataInfo.KlassMethodCountOffset);

			TargetBlob blob = target.ReadMemory (
				method_info, method_count * target.TargetInfo.TargetAddressSize);

			methods = new Hashtable ();
			TargetReader method_reader = new TargetReader (blob.Contents, target.TargetInfo);
			for (int i = 0; i < method_count; i++) {
				TargetAddress address = method_reader.ReadAddress ();

				int mtoken = target.ReadInteger (address + 4);
				if (mtoken == 0)
					continue;

				methods.Add (mtoken, address);
			}
		}

		public bool IsGenericClass {
			get { return !GenericClass.IsNull; }
		}

		public int[] FieldOffsets {
			get { return field_offsets; }
		}

		internal TargetAddress GetMethodAddress (Thread target, int token)
		{
			if (!methods.Contains (token))
				throw new InternalError ();
			return (TargetAddress) methods [token];
		}
	}
}
