onedir = $(prefix)/lib/mono/1.0

bin_SCRIPTS = mdb

one_SCRIPTS = mdb.exe mdb-server.exe mdb-server

MCS_FLAGS = -debug -define:DEBUG -nowarn:0169,0067

noinst_SCRIPTS = Mono.Debugger.dll

EXTRA_DATA = Mono.Debugger.Cecil.dll

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
	$(top_srcdir)/languages/*.cs			\
	$(top_srcdir)/languages/mono/*.cs		\
	$(top_srcdir)/languages/native/*.cs		\
	$(top_srcdir)/backends/remoting/*.cs		\
	$(top_srcdir)/classes/*.cs			\
	$(top_srcdir)/interfaces/*.cs			\
	AssemblyInfo.cs

MDB_SERVER_SRCLIST = \
	$(top_srcdir)/wrapper/Server.cs

LIBRARY_DEPS = \
	-r:Mono.GetOptions				\
	-r:Mono.CompilerServices.SymbolWriter		\
	-r:System.Runtime.Remoting			\
	-r:System.Runtime.Serialization.Formatters.Soap	\
	-r:$(srcdir)/Mono.Debugger.Cecil.dll

MDB_DEPS = \
	-r:Mono.GetOptions				\
	-r:Mono.CompilerServices.SymbolWriter		\
	-r:System.Runtime.Remoting			\
	-r:./Mono.Debugger.dll

MDB_SERVER_DEPS = \
	-r:System.Runtime.Remoting		\
	-r:./Mono.Debugger.dll

Mono.Debugger.dll: Makefile $(srcdir)/Mono.Debugger.Cecil.dll $(LIBRARY_SRCLIST)
	$(MCS) -target:library -out:Mono.Debugger.dll $(MCS_FLAGS) -keyfile:$(top_srcdir)/wrapper/mono.snk $(LIBRARY_DEPS) $(LIBRARY_SRCLIST)

#Mono.Debugger.Cecil.dll: Makefile $(top_srcdir)/cecil/lib/Mono.Cecil.dll.sources
#	(dir=`pwd` && cd $(top_srcdir)/cecil/lib && $(MCS) -target:library -out:$$dir/Mono.Debugger.Cecil.dll $(MCS_FLAGS) @Mono.Cecil.dll.sources -keyfile:$(top_srcdir)/wrapper/mono.snk)

mdb.exe: Makefile $(MDB_SRCLIST) Mono.Debugger.dll
	$(MCS) -out:mdb.exe $(MDB_DEPS) $(MDB_SRCLIST) $(MCS_FLAGS)

mdb-server.exe: Makefile $(MDB_SERVER_SRCLIST) Mono.Debugger.dll
	$(MCS) -out:mdb-server.exe $(MDB_SERVER_DEPS) $(MDB_SERVER_SRCLIST) $(MCS_FLAGS)

AssemblyInfo.cs: AssemblyInfo.cs.in Makefile
	sed -e 's^\@libdir\@^$(libdir)^g' \
		-e 's^\@prefix\@^$(prefix)^g' \
		< $(srcdir)/AssemblyInfo.cs.in > assinfo.tmp \
	&& mv assinfo.tmp AssemblyInfo.cs

noinst_DATA = Mono.Debugger.dll.config

CLEANFILES = *.exe *.dll *.mdb mdb mdb-server AssemblyInfo.cs Mono.Debugger.dll.config

EXTRA_DIST = \
	mdb.in mono.snk AssemblyInfo.cs.in Mono.Debugger.dll.config.in \
	Mono.Debugger.Cecil.dll Server.cs mdb-server.in

mdb: mdb.in Makefile
	sed -e 's^\@onedir\@^$(onedir)^g' < $(srcdir)/mdb.in > mdb.tmp \
	&& mv mdb.tmp mdb 

mdb-server: mdb-server.in Makefile
	sed -e 's^\@onedir\@^$(onedir)^g' \
		-e 's^\@prefix\@^$(prefix)^g' < $(srcdir)/mdb-server.in > mdb-server.tmp \
	&& mv mdb-server.tmp mdb-server

install-data-local:
	$(GACUTIL) /i $(srcdir)/Mono.Debugger.Cecil.dll /f $(GACUTIL_FLAGS) || exit 1;
	$(GACUTIL) /i Mono.Debugger.dll /f $(GACUTIL_FLAGS) || exit 1;

uninstall-local:
	$(GACUTIL) /u Mono.Debugger $(GACUTIL_FLAGS) || exit 1;
	$(GACUTIL) /u Mono.Debugger.Cecil $(GACUTIL_FLAGS) || exit 1;
