using System;
using System.IO;
using System.Collections;
using System.Reflection;
using NUnit.Core;
using NUnit.Core.Builders;
using NUnit.Core.Extensibility;

namespace Mono.Debugger.Test.Framework
{
	[NUnitAddin (Name = "Mono Debugger Test Extension")]
	public class DebuggerTestAddIn : NUnitTestFixtureBuilder, IAddin
	{
		public const string DebuggerTestFixtureAttribute = "Mono.Debugger.Test.Framework.DebuggerTestFixtureAttribute";
		public const int DefaultTimeout = 5000;

		static DebuggerTestAddIn ()
		{
			DebuggerTestHost.InitializeRemoting ();
		}

		public bool Install (IExtensionHost host)
		{
			IExtensionPoint builders = host.GetExtensionPoint("SuiteBuilders");
			if (builders == null)
				return false;

			builders.Install (this);
			return true;
		}

		public override bool CanBuildFrom (Type type)
		{
			return Reflect.HasAttribute (type, DebuggerTestFixtureAttribute, false);
		}

		protected override NUnit.Core.TestSuite MakeSuite (Type type)
		{
			return new DebuggerTestSuite (type);
		}
	}

	public class DebuggerTestSuite : TestSuite
	{
		public Type Type {
			get; private set;
		}

		public DebuggerTestFixtureAttribute Attribute {
			get; private set;
		}

		public DebuggerTestSuite (Type type)
			: base (type)
		{
			this.Type = type;
			this.Attribute = (DebuggerTestFixtureAttribute) Reflect.GetAttribute (
				type, DebuggerTestAddIn.DebuggerTestFixtureAttribute, false);
		}

		public override TestResult Run (EventListener listener, ITestFilter filter)
		{
			TestSuiteResult suite_result = new TestSuiteResult (new TestInfo (this), TestName.FullName);

			DebuggerTestHost host = DebuggerTestHost.Create ();
			if (host == null) {
				TestCaseResult error = new TestCaseResult (new TestInfo (this));
				string msg = String.Format ("Failed to create DebuggerTestHost in {0}", FixtureType.Name);
				error.Failure (msg, null, FailureSite.Parent);
				suite_result.AddResult (error);
				return suite_result;
			}

			int timeout;
			if (Attribute.Timeout != 0)
				timeout = Attribute.Timeout;
			else
				timeout = DebuggerTestAddIn.DefaultTimeout;

			int repeat = 1;
			if (Attribute.Repeat != 0)
				repeat = Attribute.Repeat;

			try {
				for (int i = 0; i < repeat; i++) {
					if (!host.Run (new TestInfo (this), suite_result, Type.AssemblyQualifiedName, listener, filter, timeout))
						break;
				}

				return suite_result;
			} finally {
				host.Dispose ();
			}
		}
	}
}
