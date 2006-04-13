MCS_FLAGS=-debug -unsafe

AM_CFLAGS = -g -O0

INCLUDES = -DTEST_SRCDIR=\"$(top_srcdir)/test/src\" -DTEST_BUILDDIR=\"`pwd`\"

noinst_SCRIPTS = \
	TestManagedTypes.exe TestInvocation.exe TestInheritance.exe \
	TestDelegate.exe TestIndexer.exe TestEnum.exe \
	TestRestart.exe TestNull.exe TestByRef.exe \
	TestToString.exe TestObject.exe TestProperty.exe TestAbort.exe \
	TestBreakpoint.exe TestNamespace.exe TestException.exe \
	TestRestart.exe TestExec.exe TestChild.exe

EXTRA_DIST = \
	TestManagedTypes.cs TestInvocation.cs TestInheritance.cs \
	TestDelegate.cs TestIndexer.cs TestEnum.cs \
	TestRestart.cs TestNull.cs TestByRef.cs \
	TestToString.cs TestObject.cs TestProperty.cs TestAbort.cs \
	TestBreakpoint.cs TestNamespace.cs TestException.cs \
	TestRestart.cs TestExec.cs TestChild.cs

noinst_PROGRAMS = testnativefork testnativeexec testnativechild testnativeexec-2

%.exe:%.cs
	$(MCS) $(MCS_FLAGS) -out:$@ $<

%.dll:%.cs
	$(MCS) $(MCS_FLAGS) /target:library -out:$@ $<

CLEANFILES = *.exe *.mdb *.dll *.so a.out *.log
