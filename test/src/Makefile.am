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
	TestCCtor.cs TestSimpleGenerics.cs TestRecursiveGenerics.cs \
	TestAnonymous.cs TestSSE.cs TestIterator.cs TestLineHidden.cs \
	TestMultiThread2.cs TestActivateBreakpoints.cs TestActivateBreakpoints2.cs \
	TestToString2.cs TestNestedBreakStates.cs TestExpressionEvaluator.cs

EXTRA_TEST_SRC = \
	TestAppDomain.cs TestAppDomain-Module.cs TestAppDomain-Hello.cs \
	IHelloInterface.cs TestBreakpoint2.cs TestBreakpoint2-Module.cs

TEST_EXE = $(TEST_SRC:.cs=.exe) $(noinst_PROGRAMS) $(EXTRA_TEST_EXE)

EXTRA_TEST_EXE = TestAppDomain.exe TestAppDomain-Module.exe TestAppDomain-Hello.dll \
	IHelloInterface.dll TestBreakpoint2-Module.dll TestBreakpoint2.exe

EXTRA_DIST = $(srcdir)/*.cs $(srcdir)/*.c

noinst_PROGRAMS = \
	testnativefork testnativeexec testnativechild testnativeattach \
	testnativetypes testnativenoforkexec

all: $(TEST_EXE)

%.exe:%.cs
	$(TARGET_MCS) $(MCS_FLAGS) -out:$@ $<

%.dll:%.cs
	$(TARGET_MCS) $(MCS_FLAGS) /target:library -out:$@ $<

TestAppDomain-Hello.dll: TestAppDomain-Hello.cs IHelloInterface.dll
	$(TARGET_MCS) $(MCS_FLAGS) /target:library /r:IHelloInterface.dll -out:$@ $<

TestAppDomain.exe: TestAppDomain.cs IHelloInterface.dll
	$(TARGET_MCS) $(MCS_FLAGS) /r:IHelloInterface.dll -out:$@ $<

TestAppDomain-Module.exe: TestAppDomain-Module.cs IHelloInterface.dll
	$(TARGET_MCS) $(MCS_FLAGS) /r:IHelloInterface.dll -out:$@ $<

TestBreakpoint2-Module.dll: TestBreakpoint2-Module.cs
	$(TARGET_MCS) $(MCS_FLAGS) /target:library -out:$@ $<

TestBreakpoint2.exe: TestBreakpoint2.cs TestBreakpoint2-Module.dll
	$(TARGET_MCS) $(MCS_FLAGS) /r:TestBreakpoint2-Module.dll -out:$@ $<

CLEANFILES = *.exe *.mdb *.dll *.so a.out *.log
