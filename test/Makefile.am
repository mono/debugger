MCS_FLAGS=-g

noinst_SCRIPTS = $(test_FILES)

test_FILES = TestManagedTypes.exe

EXTRA_DIST = @srcdir@/*.cs

%.exe:%.cs
	$(MCS) $(MCS_FLAGS) $<

CLEANFILES = *.exe *.dbg a.out

test: $(test_FILES)
	for test in $(test_FILES) ; do \
		echo -n "Running $$test ... " ; \
		if $(MONO) ./$$test > /dev/null; then \
			echo OK; \
		else \
			echo FAILED; exit 1; \
		fi \
	done
	@echo "All tests passed."

