using System;

[Serializable]
public class TestAppDomain
{
	static AppDomain CreateAppDomain ()
	{
		return AppDomain.CreateDomain ("testdomain");
	}

	public void Test ()
	{
		string name = AppDomain.CurrentDomain.FriendlyName;
		Console.WriteLine ("Hello from `{0}'.", name);
		if (name == "testdomain")
			throw new Exception ("Test");
	}

	static void Main ()
	{
		AppDomain domain = CreateAppDomain ();

		TestAppDomain test = new TestAppDomain ();
		test.Test ();
		domain.DoCallBack (new CrossAppDomainDelegate (test.Test));
	}
}
