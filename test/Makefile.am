SUBDIRS = src testsuite

MCS_FLAGS=-debug -unsafe

noinst_SCRIPTS = \
	TestAssembly.exe TestModule.dll \
	MultiThread.exe Simple.exe TestException.exe

noinst_LTLIBRARIES=libfoo.la

noinst_PROGRAMS = testnativetypes module

EXTRA_DIST = \
	TestAssembly.cs TestModule.cs \
	MultiThread.cs Simple.cs TestException.cs mdb.exp

testnativetypes_SOURCES = testnativetypes.c

module_SOURCES = module.c
module_LDADD = -ldl

libfoo_la_SOURCES = foo.c

%.exe:%.cs
	$(MCS) $(MCS_FLAGS) -out:$@ $<

%.dll:%.cs
	$(MCS) $(MCS_FLAGS) /target:library -out:$@ $<

CLEANFILES = *.exe *.mdb *.dll *.so *.dbg a.out *.log *.sum dbg.out

