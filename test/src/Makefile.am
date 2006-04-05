MCS_FLAGS=-debug -unsafe

noinst_SCRIPTS = \
	TestManagedTypes.exe TestInvocation.exe

EXTRA_DIST = \
	TestManagedTypes.cs TestInvocation.cs

%.exe:%.cs
	$(MCS) $(MCS_FLAGS) -out:$@ $<

%.dll:%.cs
	$(MCS) $(MCS_FLAGS) /target:library -out:$@ $<

CLEANFILES = *.exe *.mdb *.dll *.so a.out *.log
