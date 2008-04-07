using System;

namespace Mono.Debugger.Languages.Native
{
	internal class NativePointerType : TargetPointerType
	{
		public NativePointerType (Language language, string name, int size)
			: base (language, name, size)
		{ }

		public NativePointerType (Language language, TargetType target)
			: this (language, MakePointerName (target), target, target.Size)
		{ }

		public NativePointerType (Language language, string name,
					  TargetType target_type, int size)
			: this (language, name, size)
		{
			this.target_type = target_type;
		}

		TargetType target_type;

		public override bool HasClassType {
			get { return false; }
		}

		public override TargetClassType ClassType {
			get { throw new InvalidOperationException (); }
		}

		public override bool IsTypesafe {
			get { return false; }
		}

		public override bool HasStaticType {
			get { return target_type != null; }
		}

		public override bool CanDereference {
			get { return target_type != null; }
		}

		public override bool ContainsGenericParameters {
			get { return false; }
		}

		public override bool IsArray {
			get { return true; }
		}

		public override TargetType StaticType {
			get {
				if (target_type == null)
					throw new InvalidOperationException ();

				return target_type;
			}
		}

		internal static string MakePointerName (TargetType type)
		{
			NativeFunctionType func_type = type as NativeFunctionType;
			if (func_type != null)
				return func_type.GetPointerName ();

			NativeFunctionPointer func_ptr = type as NativeFunctionPointer;
			if (func_ptr != null)
				return ((NativeFunctionType) func_ptr.Type).GetPointerName ();

			return type.Name + "*";
		}

		protected override TargetObject DoGetObject (TargetMemoryAccess target,
							     TargetLocation location)
		{
			return new NativePointerObject (this, location);
		}

		public override TargetPointerObject GetObject (TargetAddress address)
		{
			return new NativePointerObject (this, new AbsoluteTargetLocation (address));
		}

		public override string ToString ()
		{
			return String.Format ("{0} [{1}:{2}:{3}]", GetType (),
					      Name, Size, target_type);
		}
	}
}
