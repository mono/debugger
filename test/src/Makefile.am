MCS_FLAGS=-debug -unsafe

noinst_SCRIPTS = \
	TestManagedTypes.exe TestInvocation.exe TestInheritance.exe \
	TestDelegate.exe TestIndexer.exe TestEnum.exe \
	TestRestart.exe TestNull.exe TestByRef.exe \
	TestToString.exe TestObject.exe TestProperty.exe TestAbort.exe \
	TestBreakpoint.exe

EXTRA_DIST = \
	TestManagedTypes.cs TestInvocation.cs TestInheritance.cs \
	TestDelegate.cs TestIndexer.cs TestEnum.cs \
	TestRestart.cs TestNull.cs TestByRef.cs TestToString.cs \
	TestObject.cs TestProperty.cs TestAbort.cs TestBreakpoint.exe

%.exe:%.cs
	$(MCS) $(MCS_FLAGS) -out:$@ $<

%.dll:%.cs
	$(MCS) $(MCS_FLAGS) /target:library -out:$@ $<

CLEANFILES = *.exe *.mdb *.dll *.so a.out *.log
