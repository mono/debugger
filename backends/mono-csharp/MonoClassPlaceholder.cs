using System;

namespace Mono.Debugger.Languages.CSharp
{
	/* this merely serves as a non-entry for looking up types.  It
	 * permits basic name queries, but nothing more substantial.  The
	 * rest of the code in MonoCSharpLanguage.cs knows to deal with this
	 * class in a special way: it is never cached, and when
	 * fields/properties notice that their Type property is queried and
	 * their current type is a placeholder, they know to look up their
	 * real type.
	 */
	internal class MonoClassPlaceholder : MonoType
	{
		public MonoClassPlaceholder (Type type, int size)
			: base (TargetObjectKind.Class, type, size)
		{ }

		public override bool IsByRef {
			get {
				return true;
			}
		}

		public override MonoObject GetObject (TargetLocation location)
		{
		  Console.WriteLine ("argh");
		  return null;
		}
	}
}
