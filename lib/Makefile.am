bin_SCRIPTS = Interpreter.exe mdb mono-debug 

MCS_FLAGS = -g

assdir = $(libdir)

ass_SCRIPTS = Mono.Debugger.dll

noinst_SCRIPTS = SymbolFileReader.exe

EXTRA_SCRIPTS = Debugger.exe

INTERPRETER_SRCLIST = \
	@top_srcdir@/frontends/command-line/*.cs	\
	@top_srcdir@/frontends/scripting/*.cs

LIBRARY_SRCLIST = \
	@top_srcdir@/interfaces/*.cs			\
	@top_srcdir@/classes/*.cs			\
	@top_srcdir@/backends/*.cs			\
	@top_srcdir@/backends/classes/*.cs		\
	@top_srcdir@/backends/ptrace/*.cs		\
	@top_srcdir@/backends/mono-csharp/*.cs		\
	@top_srcdir@/backends/native/*.cs		\
	@top_srcdir@/arch/*.cs

SYMFILE_READER_SRCLIST = \
	@top_srcdir@/lib/SymbolFileReader.cs

LIBRARY_DEPS = \
	-r Mono.GetOptions	\
	-r Mono.CSharp.Debugger

INTERPRETER_DEPS = \
	-r Mono.GetOptions	\
	-r Mono.CSharp.Debugger	\
	-r Mono.Debugger.dll

SYMFILE_READER_DEPS = \
	-r Mono.GetOptions	\
	-r Mono.CSharp.Debugger

Mono.Debugger.dll: $(LIBRARY_SRCLIST)
	$(MCS) /target:library /out:Mono.Debugger.dll $(MCS_FLAGS) $(LIBRARY_DEPS) $(LIBRARY_SRCLIST)

Interpreter.exe: $(INTERPRETER_SRCLIST) Mono.Debugger.dll
	$(MCS) -o Interpreter.exe $(INTERPRETER_DEPS) $(INTERPRETER_SRCLIST) $(MCS_FLAGS)

SymbolFileReader.exe: $(SYMFILE_READER_SRCLIST)
	$(MCS) -o SymbolFileReader.exe $(SYMFILE_READER_DEPS) $(SYMFILE_READER_SRCLIST) $(MCS_FLAGS)

Interpreter.exe.config: Interpreter.exe.config.in Makefile
	sed -e 's,\@MONO\@,${libexecdir}/mono-debugger-mini-wrapper,g' \
		-e 's,\@PATH\@,${PATH},g' \
		-e 's,\@LD_LIBRARY_PATH\@,${libdir}:${LD_LIBRARY_PATH},g' \
		< $< > tmp-t && mv tmp-t $@

configdir = $(bindir)
config_DATA = Interpreter.exe.config

CLEANFILES = *.exe *.dll

EXTRA_DIST = \
	mdb.in SymbolFileReader.cs \
	Interpreter.exe.config.in

mdb: mdb.in
	sed -e 's^\@bindir\@^$(bindir)^g' < $(srcdir)/mdb.in > mdb.tmp \
	&& mv mdb.tmp mdb 

