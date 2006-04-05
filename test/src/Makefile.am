MCS_FLAGS=-debug -unsafe

noinst_SCRIPTS = \
	TestManagedTypes.exe TestInvocation.exe TestInheritance.exe

EXTRA_DIST = \
	TestManagedTypes.cs TestInvocation.cs TestInheritance.cs

%.exe:%.cs
	$(MCS) $(MCS_FLAGS) -out:$@ $<

%.dll:%.cs
	$(MCS) $(MCS_FLAGS) /target:library -out:$@ $<

CLEANFILES = *.exe *.mdb *.dll *.so a.out *.log
