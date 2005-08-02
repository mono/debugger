using System;
using System.Reflection;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Activation;
using System.Runtime.Remoting.Channels;

namespace Mono.Debugger.Remoting
{
	public class DebuggerTest : MarshalByRefObject
	{
		static string TheURL = "mdb://gondor:/home/martin/monocvs/debugger/backends/remoting/Sleep.exe!Foo";

		public static int Main ()
		{
			DebuggerChannel channel = new DebuggerChannel ();
			ChannelServices.RegisterChannel (channel);


#if FIXME
			object[] url = new object[] { new UrlAttribute (TheURL) };
			Foo foo = (Foo) Activator.CreateInstance (typeof (Foo), null, url);
#endif

			Foo foo = (Foo) Activator.GetObject (
				typeof (Foo),
				"mdb://gondor:/home/martin/monocvs/debugger/backends/remoting/Sleep.exe!Foo");

			Console.WriteLine (foo);
			Bar bar = foo.Test ();

			Console.WriteLine ("DONE");

			ChannelServices.UnregisterChannel (channel);

			return 0;
		}
	}
}
