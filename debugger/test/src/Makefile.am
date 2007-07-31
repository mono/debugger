MCS_FLAGS=-debug -unsafe

AM_CFLAGS = -g -O0

INCLUDES = -DTEST_SRCDIR=\"$(top_srcdir)/test/src\" -DTEST_BUILDDIR=\"`pwd`\"

noinst_SCRIPTS = \
	TestManagedTypes.exe TestInvocation.exe TestInheritance.exe \
	TestDelegate.exe TestIndexer.exe TestEnum.exe \
	TestRestart.exe TestNull.exe TestByRef.exe \
	TestToString.exe TestObject.exe TestProperty.exe TestAbort.exe \
	TestBreakpoint.exe TestNamespace.exe TestException.exe \
	TestRestart.exe TestExec.exe TestChild.exe TestAttach.exe \
	TestMethodLookup.exe TestMultiThread.exe TestCCtor.exe \
	TestAppDomain.exe

EXTRA_DIST = \
	TestManagedTypes.cs TestInvocation.cs TestInheritance.cs \
	TestDelegate.cs TestIndexer.cs TestEnum.cs \
	TestRestart.cs TestNull.cs TestByRef.cs \
	TestToString.cs TestObject.cs TestProperty.cs TestAbort.cs \
	TestBreakpoint.cs TestNamespace.cs TestException.cs \
	TestRestart.cs TestExec.cs TestChild.cs TestAttach.cs \
	TestMethodLookup.cs TestMultiThread.cs TestSession.cs \
	TestCCtor.cs TestAppDomain.cs

noinst_PROGRAMS = \
	testnativefork testnativeexec testnativechild testnativeattach \
	testnativetypes

%.exe:%.cs
	$(MCS) $(MCS_FLAGS) -out:$@ $<

%.dll:%.cs
	$(MCS) $(MCS_FLAGS) /target:library -out:$@ $<

CLEANFILES = *.exe *.mdb *.dll *.so a.out *.log
