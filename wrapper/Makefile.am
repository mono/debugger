wrapperdir = $(libdir)

wrapper_PROGRAMS = mono-debugger-mini-wrapper

INCLUDES = \
	-DG_LOG_DOMAIN=\"MonoDebuggerJitWrapper\" \
	@WRAPPER_CFLAGS@

mono_debugger_mini_wrapper_SOURCES = \
	mini-main.c			\
	wrapper.c			\
	thread-manager.c		\
	icall.c				\
	mono-debugger-jit-wrapper.h

mono_debugger_mini_wrapper_LDADD = \
	@WRAPPER_LIBS@

bin_SCRIPTS = mdb.exe mdb

MCS_FLAGS = -g

noinst_SCRIPTS = Mono.Debugger.dll

MDB_SRCLIST = \
	@top_srcdir@/frontends/command-line/DebuggerTextWriter.cs	\
	@top_srcdir@/frontends/command-line/GnuReadLine.cs		\
	@top_srcdir@/frontends/command-line/Main.cs			\
	@top_srcdir@/frontends/command-line/CL.cs			\
	@top_srcdir@/frontends/command-line/ExpressionParser.cs		\
	@top_srcdir@/frontends/command-line/Tokenizer.cs		\
	@top_srcdir@/frontends/scripting/Command.cs			\
	@top_srcdir@/frontends/scripting/Expression.cs			\
	@top_srcdir@/frontends/scripting/Interpreter.cs			\
	@top_srcdir@/frontends/scripting/MyTextReader.cs		\
	@top_srcdir@/frontends/scripting/ScriptingContext.cs		\
	@top_srcdir@/frontends/scripting/Session.cs			\
	@top_srcdir@/frontends/scripting/Style.cs			\
	AssemblyInfo.cs

LIBRARY_SRCLIST = \
	@top_srcdir@/interfaces/*.cs			\
	@top_srcdir@/classes/*.cs			\
	@top_srcdir@/backends/*.cs			\
	@top_srcdir@/backends/classes/*.cs		\
	@top_srcdir@/backends/ptrace/*.cs		\
	@top_srcdir@/backends/mono-csharp/*.cs		\
	@top_srcdir@/backends/native/*.cs		\
	@top_srcdir@/arch/*.cs				\
	AssemblyInfo.cs

LIBRARY_DEPS = \
	-r:Mono.GetOptions	\
	-r:Mono.CompilerServices.SymbolWriter

MDB_DEPS = \
	-r:Mono.GetOptions	\
	-r:Mono.CompilerServices.SymbolWriter	\
	-r:./Mono.Debugger.dll

Mono.Debugger.dll: $(LIBRARY_SRCLIST)
	$(MCS) /target:library /out:Mono.Debugger.dll $(MCS_FLAGS) -keyfile:@top_srcdir@/wrapper/mono.snk $(LIBRARY_DEPS) $(LIBRARY_SRCLIST)

mdb.exe: $(MDB_SRCLIST) Mono.Debugger.dll
	$(MCS) -o mdb.exe $(MDB_DEPS) $(MDB_SRCLIST) $(MCS_FLAGS)

AssemblyInfo.cs: AssemblyInfo.cs.in Makefile
	sed -e 's^\@libdir\@^$(libdir)^g' \
		-e 's^\@prefix\@^$(prefix)^g' \
		< $(srcdir)/AssemblyInfo.cs.in > assinfo.tmp \
	&& mv assinfo.tmp AssemblyInfo.cs

noinst_DATA = Mono.Debugger.dll.config

Mono.Debugger.dll.config: Mono.Debugger.dll.config.in Makefile
	sed -e 's^\@libdir\@^$(libdir)^g' \
		-e 's^\@LT_VERSION_SUFFIX\@^$(LT_VERSION_SUFFIX)^g' \
		< $(srcdir)/Mono.Debugger.dll.config.in > config.tmp \
	&& mv config.tmp Mono.Debugger.dll.config

ASSEMBLY_NAME = Mono.Debugger
ASSEMBLY = $(ASSEMBLY_NAME).dll

CLEANFILES = *.exe *.dll mdb AssemblyInfo.cs

EXTRA_DIST = \
	mdb.in mono.snk AssemblyInfo.cs.in Mono.Debugger.dll.config

mdb: mdb.in Makefile
	sed -e 's^\@bindir\@^$(bindir)^g' < $(srcdir)/mdb.in > mdb.tmp \
	&& mv mdb.tmp mdb 

install-data-local:
	$(GACUTIL) /i $(ASSEMBLY) /f $(GACUTIL_FLAGS) || exit 1;

uninstall-local:
	$(GACUTIL) /u $(ASSEMBLY_NAME) $(GACUTIL_FLAGS) || exit 1;

