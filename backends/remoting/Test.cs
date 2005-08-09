using System;
using System.Reflection;
using System.Threading;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Activation;
using System.Runtime.Remoting.Channels;

namespace Mono.Debugger.Remoting
{
	public class Test
	{
		static string TheURL = "mdb://gondor:/home/martin/monocvs/debugger/backends/remoting/Server.exe!Foo";

		static Foo foo;

		static void test ()
		{
			Thread.Sleep (1000);
			Console.WriteLine ("TEST!");

			Bar bar = foo.Test ();
			bar.Hello ();

			Console.WriteLine ("TEST DONE!");
		}

		public static int Main ()
		{
			DebuggerClientChannel channel = new DebuggerClientChannel ();
			ChannelServices.RegisterChannel (channel);

#if FIXME
			object[] url = new object[] { new UrlAttribute (TheURL) };
			foo = (Foo) Activator.CreateInstance (typeof (Foo), null, url);
#else
			foo = (Foo) Activator.GetObject (typeof (Foo), TheURL);
#endif

			Thread thread = new Thread (test);
			thread.Start ();

			Console.WriteLine ("OK");
			foo.Hello ();
			Console.WriteLine ("OK");

			Bar bar = foo.Test ();
			// Console.WriteLine (bar);

			Console.WriteLine ("DONE");

			ChannelServices.UnregisterChannel (channel);
			channel.Dispose ();

			return 0;
		}
	}
}
