MCS_FLAGS=-debug -unsafe

AM_CFLAGS = -g -O0

INCLUDES = -DTEST_SRCDIR=\"$(top_srcdir)/test/src\" -DTEST_BUILDDIR=\"`pwd`\"

TEST_SRC = \
	TestManagedTypes.cs TestInvocation.cs TestInheritance.cs \
	TestDelegate.cs TestIndexer.cs TestEnum.cs \
	TestRestart.cs TestNull.cs TestByRef.cs \
	TestToString.cs TestObject.cs TestProperty.cs TestAbort.cs \
	TestBreakpoint.cs TestNamespace.cs TestException.cs \
	TestRestart.cs TestExec.cs TestChild.cs TestAttach.cs \
	TestMethodLookup.cs TestMultiThread.cs TestSession.cs \
	TestCCtor.cs TestAppDomain.cs

TEST_EXE = $(TEST_SRC:.cs=.exe)

EXTRA_DIST = $(TEST_SRC)

noinst_PROGRAMS = \
	testnativefork testnativeexec testnativechild testnativeattach \
	testnativetypes

check-local: $(TEST_EXE)

%.exe:%.cs
	$(MCS) $(MCS_FLAGS) -out:$@ $<

%.dll:%.cs
	$(MCS) $(MCS_FLAGS) /target:library -out:$@ $<

CLEANFILES = *.exe *.mdb *.dll *.so a.out *.log
