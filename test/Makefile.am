MCS_FLAGS=-g

noinst_SCRIPTS = $(test_FILES)

runtest = $(srcdir)/runtest.pl
debugger = '$(MONO) ../lib/Interpreter.exe'

test_FILES = TestManagedTypes.exe TestLexicalScopes.exe

EXTRA_DIST = runtest.pl A.cs B.cs C.cs ContextMenu.cs CrashMe.cs D.cs E.cs \
	F.cs Foo.cs GC.cs G.cs H.cs Hello.cs I.cs J.cs K.cs L.cs M.cs \
	Monkey.cs MultiThread.cs N.cs Property.cs Simple.cs TestAppDomain.cs Test.cs \
	TestInternCall.cs TestLexicalScopes.cs TestLine.cs TestManagedTypes.cs \
	TestSourceView.cs

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

