wrapperdir = $(libdir)
onedir = $(libdir)/mono/1.0

wrapper_PROGRAMS = mono-debugger-mini-wrapper

INCLUDES = \
	-DG_LOG_DOMAIN=\"MonoDebuggerJitWrapper\" \
	@WRAPPER_CFLAGS@ @glue_cflags@

mono_debugger_mini_wrapper_SOURCES = \
	mini-main.c			\
	wrapper.c			\
	thread-manager.c		\
	icall.c				\
	mono-debugger-jit-wrapper.h

mono_debugger_mini_wrapper_LDADD = \
	@WRAPPER_LIBS@

bin_SCRIPTS = mdb

one_SCRIPTS = mdb.exe 

MCS_FLAGS = -g -define:DEBUG

noinst_SCRIPTS = Mono.Debugger.dll

MDB_SRCLIST = \
	@top_srcdir@/frontend/CL.cs			\
	@top_srcdir@/frontend/Command.cs		\
	@top_srcdir@/frontend/Completer.cs		\
	@top_srcdir@/frontend/DebuggerTextWriter.cs	\
	@top_srcdir@/frontend/Expression.cs		\
	@top_srcdir@/frontend/ExpressionParser.cs	\
	@top_srcdir@/frontend/GnuReadLine.cs		\
	@top_srcdir@/frontend/Interpreter.cs		\
	@top_srcdir@/frontend/Main.cs			\
	@top_srcdir@/frontend/MyTextReader.cs		\
	@top_srcdir@/frontend/ScriptingContext.cs	\
	@top_srcdir@/frontend/Style.cs			\
	@top_srcdir@/frontend/Tokenizer.cs		\
	AssemblyInfo.cs

LIBRARY_SRCLIST = \
	@top_srcdir@/arch/*.cs				\
	@top_srcdir@/backends/*.cs			\
	@top_srcdir@/backends/classes/*.cs		\
	@top_srcdir@/backends/mono/*.cs			\
	@top_srcdir@/backends/native/*.cs		\
	@top_srcdir@/backends/ptrace/*.cs		\
	@top_srcdir@/classes/*.cs			\
	@top_srcdir@/interfaces/*.cs			\
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
	sed -e 's^\@onedir\@^$(onedir)^g' < $(srcdir)/mdb.in > mdb.tmp \
	&& mv mdb.tmp mdb 

install-data-local:
	$(GACUTIL) /i $(ASSEMBLY) /f $(GACUTIL_FLAGS) || exit 1;

uninstall-local:
	$(GACUTIL) /u $(ASSEMBLY_NAME) $(GACUTIL_FLAGS) || exit 1;

