MCS_FLAGS=-g

noinst_SCRIPTS = $(test_FILES)

runtest = $(srcdir)/runtest.pl
debugger = '$(MONO) ../lib/Interpreter.exe'

test_FILES = TestManagedTypes.exe TestLexicalScopes.exe

EXTRA_DIST = runtest.pl GC.cs MultiThread.cs TestAppDomain.cs \
	TestInternCall.cs TestLexicalScopes.cs TestManagedTypes.cs

%.exe:%.cs
	$(MCS) $(MCS_FLAGS) -o $@ $<

CLEANFILES = *.exe *.dbg a.out

test: $(test_FILES)
	for test in $(test_FILES) ; do \
		echo -n "Running $$test ... " ; \
		if $(runtest) $(debugger) ./$$test > /dev/null; then \
			echo OK; \
		else \
			echo FAILED; exit 1; \
		fi \
	done
	@echo "All tests passed."

