SUBDIRS = mono-debugger.tests

AUTOMAKE_OPTIONS = dejagnu

MCS_FLAGS=-g

EXTRA_DIST = runtest.pl GC.cs MultiThread.cs TestAppDomain.cs \
	TestInternCall.cs TestLexicalScopes.cs TestManagedTypes.cs

%.exe:%.cs
	$(MCS) $(MCS_FLAGS) -o $@ $<

CLEANFILES = *.exe *.dbg a.out *.log *.sum dbg.out

