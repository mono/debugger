SUBDIRS = mono-debugger.tests

AUTOMAKE_OPTIONS = dejagnu

MCS_FLAGS=-g

noinst_SCRIPTS = TestManagedTypes.exe TestAssembly.exe TestModule.dll
noinst_PROGRAMS = testnativetypes module libfoo.so

EXTRA_DIST = TestManagedTypes.cs TestAssembly.cs TestModule.cs

testnativetypes_SOURCES = testnativetypes.c

module_SOURCES = module.c
module_LDADD = -ldl

libfoo_so_SOURCES = foo.c
libfoo_so_LDFLAGS = -shared

%.exe:%.cs
	$(MCS) $(MCS_FLAGS) -o $@ $<

%.dll:%.cs
	$(MCS) $(MCS_FLAGS) /target:library -o $@ $<

CLEANFILES = *.exe *.dll *.so *.dbg a.out *.log *.sum dbg.out

