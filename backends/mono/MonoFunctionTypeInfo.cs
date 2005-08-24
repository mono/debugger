using System;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoFunctionTypeInfo : MonoTypeInfo
	{
		new public readonly MonoFunctionType Type;
		public MonoClassInfo Klass;

		public MonoFunctionTypeInfo (MonoFunctionType type, MonoClassInfo klass)
			: base (type, type.File.TargetInfo.TargetAddressSize)
		{
			this.Type = type;
			this.Klass = klass;
		}

		public override bool HasFixedSize {
			get { return true; }
		}

		protected ITargetObject Invoke (StackFrame frame, TargetAddress this_object,
						MonoObject[] args, bool debug)
		{
			TargetAddress exc_object;

			TargetAddress method = Klass.GetMethodAddress (frame.TargetAccess, Type.Index);

			if (Type.ParameterTypes.Length != args.Length)
				throw new ArgumentException ();

			TargetAddress[] arg_ptr = new TargetAddress [args.Length];
			for (int i = 0; i < args.Length; i++) {
				if (args [i].Location.HasAddress) {
					arg_ptr [i] = args [i].Location.Address;
					continue;
				}

				Heap heap = Type.File.MonoLanguage.DataHeap;
				byte[] contents = args [i].RawContents;
				TargetLocation new_loc = heap.Allocate (frame, contents.Length);
				frame.TargetAccess.WriteBuffer (new_loc.Address, contents);

				arg_ptr [i] = new_loc.Address;
			}

			if (debug) {
				frame.Process.RuntimeInvoke (
					frame, method, this_object, arg_ptr);
				return null;
			}

			bool exc;
			TargetAddress retval = frame.Process.RuntimeInvoke (
				frame, method, this_object, arg_ptr, out exc);

			if (exc) {
				exc_object = retval;
				retval = TargetAddress.Null;
			} else {
				exc_object = TargetAddress.Null;
			}

			MonoBuiltinTypeInfo builtin = Type.File.MonoLanguage.BuiltinTypes;
			MonoTypeInfo object_type = builtin.ObjectType.GetTypeInfo ();
			MonoTypeInfo string_type = builtin.StringType.GetTypeInfo ();

			if (retval.IsNull) {
				if (exc_object.IsNull)
					return null;

				TargetLocation exc_loc = new AbsoluteTargetLocation (frame, exc_object);
				MonoStringObject exc_obj = (MonoStringObject) string_type.GetObject (exc_loc);
				string exc_message = (string) exc_obj.Object;

				throw new TargetException (
					TargetError.InvocationException, exc_message);
			}

			TargetLocation retval_loc = new AbsoluteTargetLocation (frame, retval);
			MonoObjectObject retval_obj = (MonoObjectObject) object_type.GetObject (retval_loc);

			if ((retval_obj == null) || !retval_obj.HasDereferencedObject ||
			    (Type.ReturnType == builtin.ObjectType))
				return retval_obj;
			else
				return retval_obj.DereferencedObject;
		}

		internal ITargetObject Invoke (TargetLocation location, MonoObject[] args,
					       bool debug)
		{
			return Invoke (location.StackFrame, location.Address, args, debug);
		}

		public ITargetObject InvokeStatic (StackFrame frame, MonoObject[] args,
						   bool debug)
		{
			return Invoke (frame, TargetAddress.Null, args, debug);
		}

		public override MonoObject GetObject (TargetLocation location)
		{
			return new MonoFunctionObject (this, location);
		}

		public MonoFunctionObject GetStaticObject (StackFrame frame)
		{
			return new MonoFunctionObject (this, new AbsoluteTargetLocation (frame, TargetAddress.Null));
		}
	}
}
