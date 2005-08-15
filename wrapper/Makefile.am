onedir = $(prefix)/lib/mono/1.0
wrapperdir = $(onedir)

wrapper_PROGRAMS = mono-debugger-mini-wrapper

INCLUDES = \
	-DG_LOG_DOMAIN=\"MonoDebuggerJitWrapper\" \
	$(WRAPPER_CFLAGS) $(glue_cflags)

mono_debugger_mini_wrapper_SOURCES = \
	mini-main.c			\
	wrapper.c			\
	thread-manager.c		\
	icall.c				\
	mono-debugger-jit-wrapper.h

mono_debugger_mini_wrapper_LDADD = \
	$(WRAPPER_LIBS)

bin_SCRIPTS = mdb

one_SCRIPTS = mdb.exe mdb-server.exe mdb-server

MCS_FLAGS = -debug -define:DEBUG -nowarn:0169,0067

noinst_SCRIPTS = Mono.Debugger.dll Mono.Debugger.Remoting.dll

LANGUAGE_SPECIFIC_FILES = \
	$(top_builddir)/frontend/CSharpExpressionParser.cs	\
	$(top_srcdir)/frontend/CSharpTokenizer.cs

MDB_SRCLIST = \
	$(top_srcdir)/frontend/CL.cs			\
	$(top_srcdir)/frontend/Command.cs		\
	$(top_srcdir)/frontend/Completer.cs		\
	$(top_srcdir)/frontend/DebuggerTextWriter.cs	\
	$(top_srcdir)/frontend/Expression.cs		\
	$(top_srcdir)/frontend/GnuReadLine.cs		\
	$(top_srcdir)/frontend/IExpressionParser.cs	\
	$(top_srcdir)/frontend/Interpreter.cs		\
	$(top_srcdir)/frontend/Main.cs			\
	$(top_srcdir)/frontend/MyTextReader.cs		\
	$(top_srcdir)/frontend/ScriptingContext.cs	\
	$(top_srcdir)/frontend/Style.cs			\
	$(LANGUAGE_SPECIFIC_FILES)			\
	AssemblyInfo.cs

LIBRARY_SRCLIST = \
	$(top_srcdir)/arch/*.cs				\
	$(top_srcdir)/backends/*.cs			\
	$(top_srcdir)/backends/classes/*.cs		\
	$(top_srcdir)/backends/mono/*.cs			\
	$(top_srcdir)/backends/native/*.cs		\
	$(top_srcdir)/backends/ptrace/*.cs		\
	$(top_srcdir)/classes/*.cs			\
	$(top_srcdir)/interfaces/*.cs			\
	AssemblyInfo.cs

REMOTING_SRCLIST = \
	$(top_srcdir)/backends/remoting/DebuggerChannel.cs			\
	$(top_srcdir)/backends/remoting/DebuggerClientChannel.cs		\
	$(top_srcdir)/backends/remoting/DebuggerClientTransportSink.cs		\
	$(top_srcdir)/backends/remoting/DebuggerClientTransportSinkProvider.cs	\
	$(top_srcdir)/backends/remoting/DebuggerServerChannel.cs		\
	$(top_srcdir)/backends/remoting/DebuggerServerTransportSink.cs		\
	$(top_srcdir)/backends/remoting/DebuggerServerDispatchSink.cs		\
	$(top_srcdir)/backends/remoting/DebuggerServerDispatchSinkProvider.cs	\
	$(top_srcdir)/backends/remoting/DebuggerServerResponseSink.cs		\
	$(top_srcdir)/backends/remoting/DebuggerMessageIO.cs			\
	$(top_srcdir)/backends/remoting/DebuggerRemotingException.cs		\
	$(top_srcdir)/backends/remoting/DebuggerConnection.cs			\
	$(top_srcdir)/backends/remoting/DebuggerStream.cs			\
	$(top_srcdir)/backends/remoting/DebuggerClient.cs			\
	$(top_srcdir)/backends/remoting/DebuggerServer.cs

MDB_SERVER_SRCLIST = \
	$(top_srcdir)/backends/remoting/Server.cs

LIBRARY_DEPS = \
	-r:Mono.GetOptions	\
	-r:Mono.CompilerServices.SymbolWriter

MDB_DEPS = \
	-r:Mono.GetOptions	\
	-r:Mono.CompilerServices.SymbolWriter	\
	-r:System.Runtime.Remoting		\
	-r:./Mono.Debugger.dll			\
	-r:./Mono.Debugger.Remoting.dll

REMOTING_DEPS = \
	-r:Mono.GetOptions	\
	-r:./Mono.Debugger.dll			\
	-r:System.Runtime.Remoting

MDB_SERVER_DEPS = \
	-r:System.Runtime.Remoting		\
	-r:./Mono.Debugger.Remoting.dll		\
	-r:./Mono.Debugger.dll

Mono.Debugger.dll: $(LIBRARY_SRCLIST)
	$(MCS) -target:library -out:Mono.Debugger.dll $(MCS_FLAGS) -keyfile:$(top_srcdir)/wrapper/mono.snk $(LIBRARY_DEPS) $(LIBRARY_SRCLIST)

Mono.Debugger.Remoting.dll: $(REMOTING_SRCLIST) Mono.Debugger.dll
	$(MCS) -target:library -out:Mono.Debugger.Remoting.dll $(MCS_FLAGS) -keyfile:$(top_srcdir)/wrapper/mono.snk $(REMOTING_DEPS) $(REMOTING_SRCLIST)

mdb.exe: $(MDB_SRCLIST) Mono.Debugger.Remoting.dll
	$(MCS) -out:mdb.exe $(MDB_DEPS) $(MDB_SRCLIST) $(MCS_FLAGS)

mdb-server.exe: $(MDB_SERVER_SRCLIST) Mono.Debugger.Remoting.dll
	$(MCS) -out:mdb-server.exe $(MDB_SERVER_DEPS) $(MDB_SERVER_SRCLIST) $(MCS_FLAGS)

AssemblyInfo.cs: AssemblyInfo.cs.in Makefile
	sed -e 's^\@libdir\@^$(libdir)^g' \
		-e 's^\@prefix\@^$(prefix)^g' \
		< $(srcdir)/AssemblyInfo.cs.in > assinfo.tmp \
	&& mv assinfo.tmp AssemblyInfo.cs

noinst_DATA = Mono.Debugger.dll.config Mono.Debugger.Remoting.dll.config

ASSEMBLY_NAME = Mono.Debugger
ASSEMBLY = $(ASSEMBLY_NAME).dll

REMOTING_ASSEMBLY_NAME = Mono.Debugger.Remoting
REMOTING_ASSEMBLY = $(REMOTING_ASSEMBLY_NAME).dll


CLEANFILES = *.exe *.dll *.mdb mdb AssemblyInfo.cs Mono.Debugger.dll.config Mono.Debugger.Remoting.dll

EXTRA_DIST = \
	mdb.in mdb-wrapper.in mono.snk AssemblyInfo.cs.in \
	Mono.Debugger.dll.config.in Mono.Debugger.Remoting.dll.config.in

mdb: mdb.in Makefile
	sed -e 's^\@onedir\@^$(onedir)^g' < $(srcdir)/mdb.in > mdb.tmp \
	&& mv mdb.tmp mdb 

mdb-server: mdb-server.in Makefile
	sed -e 's^\@onedir\@^$(onedir)^g' \
		-e 's^\@prefix\@^$(prefix)^g' < $(srcdir)/mdb-server.in > mdb-server.tmp \
	&& mv mdb-server.tmp mdb-server

install-data-local:
	$(GACUTIL) /i $(ASSEMBLY) /f $(GACUTIL_FLAGS) || exit 1;
	$(GACUTIL) /i $(REMOTING_ASSEMBLY) /f $(GACUTIL_FLAGS) || exit 1;

uninstall-local:
	$(GACUTIL) /u $(ASSEMBLY_NAME) $(GACUTIL_FLAGS) || exit 1;
	$(GACUTIL) /u $(REMOTING_ASSEMBLY_NAME) $(GACUTIL_FLAGS) || exit 1;

