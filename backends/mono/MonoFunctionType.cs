using System;
using System.Collections;
using R = System.Reflection;
using C = Mono.CompilerServices.SymbolWriter;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoFunctionType : MonoType, IMonoTypeInfo, ITargetFunctionType
	{
		MonoClassType klass;
		R.MethodBase method_info;
		MonoType return_type;
		MonoType[] parameter_types;
		bool has_return_type;
		int token;

		public MonoFunctionType (MonoSymbolFile file, MonoClassType klass, R.MethodBase mbase)
			: base (file, TargetObjectKind.Function, mbase.ReflectedType)
		{
			this.klass = klass;
			this.method_info = mbase;
			this.token = MonoDebuggerSupport.GetMethodToken (mbase);

			Type rtype;
			if (mbase is R.ConstructorInfo) {
				rtype = mbase.DeclaringType;
				has_return_type = true;
			} else {
				rtype = ((R.MethodInfo) mbase).ReturnType;
				has_return_type = rtype != typeof (void);
			}
			return_type = file.MonoLanguage.LookupMonoType (rtype);

			R.ParameterInfo[] pinfo = mbase.GetParameters ();
			parameter_types = new MonoType [pinfo.Length];
			for (int i = 0; i < parameter_types.Length; i++)
				parameter_types [i] = file.MonoLanguage.LookupMonoType (
					pinfo [i].ParameterType);

			type_info = this;
		}

		public override bool IsByRef {
			get { return true; }
		}

		public MonoType ReturnType {
			get { return return_type; }
		}

		public bool HasReturnValue {
			get { return has_return_type; }
		}

		public MonoType[] ParameterTypes {
			get { return parameter_types; }
		}

		public int Token {
			get { return token; }
		}

		ITargetType ITargetFunctionType.ReturnType {
			get { return return_type; }
		}

		ITargetType[] ITargetFunctionType.ParameterTypes {
			get { return parameter_types; }
		}

		public SourceMethod Source {
			get {
				int token = MonoDebuggerSupport.GetMethodToken (method_info);
				return file.GetMethodByToken (token);
			}
		}

		object ITargetFunctionType.MethodHandle {
			get { return method_info; }
		}

		protected ITargetObject Invoke (StackFrame frame, TargetAddress this_object,
						MonoObject[] args, bool debug)
		{
			TargetAddress exc_object;

			MonoClassInfo class_info = klass.GetTypeInfo () as MonoClassInfo;
			if (class_info == null)
				return null;

			TargetAddress method = class_info.GetMethodAddress (
				frame.TargetAccess, Token);

			if (ParameterTypes.Length != args.Length)
				throw new ArgumentException ();

			TargetAddress[] arg_ptr = new TargetAddress [args.Length];
			for (int i = 0; i < args.Length; i++) {
				if (args [i].Location.HasAddress) {
					arg_ptr [i] = args [i].Location.Address;
					continue;
				}

				Heap heap = File.MonoLanguage.DataHeap;
				byte[] contents = args [i].RawContents;
				TargetLocation new_loc = heap.Allocate (
					frame.TargetAccess, contents.Length);
				frame.TargetAccess.TargetMemoryAccess.WriteBuffer (
					new_loc.Address, contents);

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

			MonoBuiltinTypeInfo builtin = File.MonoLanguage.BuiltinTypes;
			IMonoTypeInfo object_type = builtin.ObjectType.GetTypeInfo ();
			IMonoTypeInfo string_type = builtin.StringType.GetTypeInfo ();

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
			    (ReturnType == builtin.ObjectType))
				return retval_obj;
			else
				return retval_obj.DereferencedObject;
		}

		internal ITargetObject Invoke (TargetLocation location, MonoObject[] args,
					       bool debug)
		{
			return Invoke (location.StackFrame, location.Address, args, debug);
		}

		ITargetObject ITargetFunctionType.InvokeStatic (StackFrame frame,
								ITargetObject[] args,
								bool debug)
		{
			MonoObject[] margs = new MonoObject [args.Length];
			args.CopyTo (margs, 0);
			return InvokeStatic (frame, margs, debug);
		}

		public ITargetObject InvokeStatic (StackFrame frame, MonoObject[] args,
						   bool debug)
		{
			return Invoke (frame, TargetAddress.Null, args, debug);
		}

		protected override IMonoTypeInfo DoGetTypeInfo (TargetBinaryReader info)
		{
			throw new InvalidOperationException ();
		}

		public override bool HasFixedSize {
			get { return true; }
		}

		public override int Size {
			get { return File.TargetInfo.TargetAddressSize; }
		}

		ITargetType ITargetTypeInfo.Type {
			get { return this; }
		}

		MonoType IMonoTypeInfo.Type {
			get { return this; }
		}

		public MonoObject GetObject (TargetLocation location)
		{
			return new MonoFunctionObject (this, location);
		}

		public MonoFunctionObject GetStaticObject (StackFrame frame)
		{
			return new MonoFunctionObject (this, new AbsoluteTargetLocation (frame, TargetAddress.Null));
		}
	}
}
