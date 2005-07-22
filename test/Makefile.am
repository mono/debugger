SUBDIRS = mono-debugger.tests

AUTOMAKE_OPTIONS = dejagnu

MCS_FLAGS=-debug

noinst_SCRIPTS = \
	TestManagedTypes.exe TestAssembly.exe TestNamespace.exe TestModule.dll \
	MultiThread.exe TestInvocation.exe TestInheritance.exe Simple.exe TestException.exe \
	TestRestart.exe

noinst_LTLIBRARIES=libfoo.la

noinst_PROGRAMS = testnativetypes module

EXTRA_DIST = \
	TestManagedTypes.cs TestAssembly.cs TestNamespace.cs TestModule.cs \
	MultiThread.cs TestInvocation.cs TestInheritance.cs Simple.cs TestException.cs mdb.exp \
	TestRestart.cs

RUNTESTFLAGS = --ignore "MultiThread.exp module.exp start-restart.exp TestModule.exp TestAssembly.exp"

testnativetypes_SOURCES = testnativetypes.c

module_SOURCES = module.c
module_LDADD = -ldl

libfoo_la_SOURCES = foo.c

%.exe:%.cs
	$(MCS) $(MCS_FLAGS) -out:$@ $<

%.dll:%.cs
	$(MCS) $(MCS_FLAGS) /target:library -out:$@ $<

CLEANFILES = *.exe *.mdb *.dll *.so *.dbg a.out *.log *.sum dbg.out

