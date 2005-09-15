using System;
using Mono.Debugger;
using Mono.Debugger.Backends;
using Mono.Cecil;
using Mono.Cecil.Metadata;
using R = System.Reflection;
using C = Mono.CompilerServices.SymbolWriter;

namespace Mono.Debugger.Languages.Mono
{
	internal static class MonoDebuggerSupport
	{
		static GetTypeFunc get_type;
		static GetLocalTypeFromSignatureFunc local_type_from_sig;
		static GetGuidFunc get_guid;
		static CheckRuntimeVersionFunc check_runtime_version;

		delegate Type GetTypeFunc (R.Assembly assembly, int token);
		delegate R.MethodBase GetMethodFunc (R.Assembly assembly, int token);
		delegate Type GetLocalTypeFromSignatureFunc (R.Assembly assembly, byte[] sig);
		delegate Guid GetGuidFunc (R.Module module);
		delegate string CheckRuntimeVersionFunc (string filename);
		delegate Type ResolveTypeFunc (R.Module module, int token);
		delegate int GetTypeTokenFunc (Type type);

		static Delegate create_delegate (Type type, Type delegate_type, string name)
		{
			R.MethodInfo mi = type.GetMethod (name, R.BindingFlags.Static |
							  R.BindingFlags.NonPublic);
			if (mi == null)
				throw new InternalError ("Can't find " + name);

			return Delegate.CreateDelegate (delegate_type, mi);
		}

		static MonoDebuggerSupport ()
		{
			get_type = (GetTypeFunc) create_delegate (
				typeof (R.Assembly), typeof (GetTypeFunc),
				"MonoDebugger_GetType");

			local_type_from_sig = (GetLocalTypeFromSignatureFunc) create_delegate (
				typeof (R.Assembly), typeof (GetLocalTypeFromSignatureFunc),
				"MonoDebugger_GetLocalTypeFromSignature");

			get_guid = (GetGuidFunc) create_delegate (
				typeof (R.Module), typeof (GetGuidFunc), "Mono_GetGuid");

			check_runtime_version = (CheckRuntimeVersionFunc) create_delegate (
				typeof (R.Assembly), typeof (CheckRuntimeVersionFunc),
				"MonoDebugger_CheckRuntimeVersion");
		}

		public static Type GetType (R.Assembly assembly, int token)
		{
			return get_type (assembly, token);
		}

		public static int GetMethodToken (Cecil.IMethodDefinition method)
		{
			return (int) (method.MetadataToken.TokenType + method.MetadataToken.RID);
		}

		public static Cecil.IMethodDefinition GetMethod (Cecil.IModuleDefinition module, int token)
		{
			return (Cecil.IMethodDefinition) module.LookupByToken (
				Cecil.Metadata.TokenType.Method, token & 0xffffff);
		}

		public static Type GetLocalTypeFromSignature (R.Assembly assembly, byte[] sig)
		{
			return local_type_from_sig (assembly, sig);
		}

		public static string CheckRuntimeVersion (string filename)
		{
			return check_runtime_version (filename);
		}

		public static Guid GetGuid (R.Module module)
		{
			return get_guid (module);
		}

		public static Cecil.ITypeReference MakeArrayType (Cecil.ITypeReference type, int rank)
		{
			/// XXXX - TODO
			throw new NotImplementedException ();
		}

		public static Cecil.ITypeReference ResolveType (Cecil.IModuleDefinition module, int token)
		{
			return (Cecil.ITypeReference) module.LookupByToken (
				Cecil.Metadata.TokenType.TypeDef, token);
		}

		public static int GetTypeToken (Cecil.ITypeReference type)
		{
			return (int) (type.MetadataToken.TokenType + type.MetadataToken.RID);
		}

		public static MonoType GetLocalTypeFromSignature (MonoSymbolFile file, byte[] signature)
		{
			TargetBlob blob = new TargetBlob (signature, file.TargetInfo);
			TargetBinaryReader reader = blob.GetReader ();

			if (reader.ReadByte () != 7)
				throw new ArgumentException ();
			if (reader.ReadByte () != 1)
				throw new ArgumentException ();

			bool is_byref = false;
			if (reader.PeekByte () == 0x10) {
				is_byref = true;
				reader.Position++;
			}

			MonoType type = GetTypeFromSignature (file, reader);
			if (type != null)
				return type;

			return file.MonoLanguage.BuiltinTypes.VoidType;
		}

		static int ReadCompressedInteger (TargetBinaryReader reader)
		{
			int integer = 0;
			byte data = reader.ReadByte ();
			if ((data & 0x80) == 0) {
				integer = data;
			} else if ((data & 0x40) == 0) {
				integer = (data & ~0x80) << 8;
				integer |= reader.ReadByte ();
			} else {
				integer = (data & ~0xc0) << 24;
				integer |= reader.ReadByte () << 16;
				integer |= reader.ReadByte () << 8;
				integer |= reader.ReadByte ();
			}
			return integer;
		}

		static MonoType GetTypeFromSignature (MonoSymbolFile file,
						      TargetBinaryReader reader)
		{
			byte value = reader.ReadByte ();
			switch (value) {
			case 0x01:
				return file.MonoLanguage.BuiltinTypes.VoidType;
			case 0x02:
				return file.MonoLanguage.BuiltinTypes.BooleanType;
			case 0x03:
				return file.MonoLanguage.BuiltinTypes.CharType;
			case 0x04:
				return file.MonoLanguage.BuiltinTypes.SByteType;
			case 0x05:
				return file.MonoLanguage.BuiltinTypes.ByteType;
			case 0x06:
				return file.MonoLanguage.BuiltinTypes.Int16Type;
			case 0x07:
				return file.MonoLanguage.BuiltinTypes.UInt16Type;
			case 0x08:
				return file.MonoLanguage.BuiltinTypes.Int32Type;
			case 0x09:
				return file.MonoLanguage.BuiltinTypes.UInt32Type;
			case 0x0a:
				return file.MonoLanguage.BuiltinTypes.Int64Type;
			case 0x0b:
				return file.MonoLanguage.BuiltinTypes.UInt64Type;
			case 0x0c:
				return file.MonoLanguage.BuiltinTypes.SingleType;
			case 0x0d:
				return file.MonoLanguage.BuiltinTypes.DoubleType;
			case 0x0e:
				return file.MonoLanguage.BuiltinTypes.StringType;

			case 0x11: /* VALUETYPE */
			case 0x12: /* CLASS */ {
				uint dor = (uint) ReadCompressedInteger (reader);
				return GetTypeFromDefOrRef (file, dor);
			}

			case 0x14: /* ARRAY */ {
				MonoType element_type = GetTypeFromSignature (file, reader);
				if (element_type == null)
					return null;

				int rank = ReadCompressedInteger (reader);

				int bound_count = ReadCompressedInteger (reader);
				if (bound_count != 0)
					throw new ArgumentException ();

				return new MonoArrayType (element_type, rank);
			}

			case 0x18:
				return file.MonoLanguage.BuiltinTypes.IntType;
			case 0x19:
				return file.MonoLanguage.BuiltinTypes.UIntType;

			case 0x1c:
				return file.MonoLanguage.BuiltinTypes.ObjectType;

			case 0x1d: /* SZARRAY */ {
				MonoType element_type = GetTypeFromSignature (file, reader);
				if (element_type == null)
					return null;

				return new MonoArrayType (element_type, 1);
			}

			default:
				Console.WriteLine ("UNKNOWN TYPE: {0}", value);
				break;
			}

			return null;
		}

		static MonoType GetTypeFromDefOrRef (MonoSymbolFile file, uint dor)
		{
			Cecil.Metadata.MetadataToken token;

			uint rid = dor >> 2;
			switch (dor & 3) {
			case 0:
				token = new Cecil.Metadata.MetadataToken (
					Cecil.Metadata.TokenType.TypeDef, rid);
				break;
			case 1:
				token = new Cecil.Metadata.MetadataToken (
					Cecil.Metadata.TokenType.TypeRef, rid);
				break;
			case 2:
				token = new Cecil.Metadata.MetadataToken (
					Cecil.Metadata.TokenType.TypeSpec, rid);
				break;
			default :
				throw new ArgumentException ();
			}

			Cecil.ITypeReference type =
				(Cecil.ITypeReference) file.Module.LookupByToken (token);
			if (type == null)
				return null;

			MonoType retval = file.MonoLanguage.LookupMonoType (type);
			return retval;
		}
	}
}
