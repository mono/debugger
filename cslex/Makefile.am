
noinst_SCRIPTS=cslex.exe

CSLEX_SRCLIST = @top_srcdir@/cslex/Lex.cs \
		@top_srcdir@/cslex/Input.cs \
		@top_srcdir@/cslex/Minimize.cs \
		@top_srcdir@/cslex/NfaPair.cs \
		@top_srcdir@/cslex/Dfa.cs \
		@top_srcdir@/cslex/CharSet.cs \
		@top_srcdir@/cslex/Utility.cs \
		@top_srcdir@/cslex/Accept.cs \
		@top_srcdir@/cslex/Anchor.cs \
		@top_srcdir@/cslex/Error.cs \
		@top_srcdir@/cslex/Bunch.cs \
		@top_srcdir@/cslex/Alloc.cs \
		@top_srcdir@/cslex/MakeNfa.cs \
		@top_srcdir@/cslex/Nfa.cs \
		@top_srcdir@/cslex/Nfa2Dfa.cs \
		@top_srcdir@/cslex/Gen.cs \
		@top_srcdir@/cslex/Spec.cs \
		@top_srcdir@/cslex/DTrans.cs \
		@top_srcdir@/cslex/Emit.cs \
		@top_srcdir@/cslex/SimplifyNFA.cs \
		@top_srcdir@/cslex/BitSet.cs

cslex.exe: $(CSLEX_SRCLIST)
	$(MCS) /target:exe /out:cslex.exe $(CSLEX_SRCLIST)

EXTRA_DIST= $(CSLEX_SRCLIST)
