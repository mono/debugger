SUBDIRS = src testsuite
EXTRA_SUBDIRS = mono-debugger.tests

MCS_FLAGS=-debug -unsafe

noinst_SCRIPTS = \
	TestAssembly.exe TestNamespace.exe TestModule.dll \
	MultiThread.exe TestInheritance.exe Simple.exe TestException.exe \
	TestRestart.exe TestDelegate.exe TestNull.exe TestIndexer.exe TestByRef.exe \
	TestToString.exe TestEnum.exe TestObject.exe TestProperty.exe TestAbort.exe \
	TestBreakpoint.exe

noinst_LTLIBRARIES=libfoo.la

noinst_PROGRAMS = testnativetypes module

EXTRA_DIST = \
	TestAssembly.cs TestNamespace.cs TestModule.cs \
	MultiThread.cs TestInheritance.cs Simple.cs TestException.cs mdb.exp \
	TestRestart.cs TestDelegate.cs TestNull.cs TestIndexer.cs TestByRef.cs TestToString.cs \
	TestEnum.cs TestObject.cs TestProperty.cs TestAbort.cs TestBreakpoint.exe

testnativetypes_SOURCES = testnativetypes.c

module_SOURCES = module.c
module_LDADD = -ldl

libfoo_la_SOURCES = foo.c

%.exe:%.cs
	$(MCS) $(MCS_FLAGS) -out:$@ $<

%.dll:%.cs
	$(MCS) $(MCS_FLAGS) /target:library -out:$@ $<

CLEANFILES = *.exe *.mdb *.dll *.so *.dbg a.out *.log *.sum dbg.out

