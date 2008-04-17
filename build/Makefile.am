twodir = $(prefix)/lib/mono/2.0

bin_SCRIPTS = mdb

two_SCRIPTS = mdb.exe

MCS_FLAGS = -debug -define:DEBUG -nowarn:0169,0067

noinst_SCRIPTS = \
	runtests					\
	Mono.Debugger.dll				\
	Mono.Debugger.Backend.dll			\
	Mono.Debugger.Frontend.dll			\
	Mono.Debugger.Test.dll

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
	$(top_srcdir)/frontend/Interpreter.cs		\
	$(top_srcdir)/frontend/Main.cs			\
	$(top_srcdir)/frontend/MyTextReader.cs		\
	$(top_srcdir)/frontend/ScriptingContext.cs	\
	$(top_srcdir)/frontend/Style.cs			\
	$(top_srcdir)/frontend/ObjectFormatter.cs	\
	$(LANGUAGE_SPECIFIC_FILES)			\
	AssemblyInfo.cs

INTERFACE_SRCLIST = \
	$(top_srcdir)/interface/*.cs			\
	AssemblyInfo.cs

BACKEND_SRCLIST = \
	$(top_srcdir)/backend/arch/*.cs			\
	$(top_srcdir)/backend/*.cs			\
	$(top_srcdir)/languages/*.cs			\
	$(top_srcdir)/languages/mono/*.cs		\
	$(top_srcdir)/languages/native/*.cs		\
	$(top_srcdir)/classes/*.cs			\
	AssemblyInfo.cs

TEST_SRCLIST = \
	$(top_srcdir)/test/testsuite/*.cs		\
	$(top_srcdir)/test/misc/*.cs			\
	AssemblyInfo.cs

TEST_CASE_SRCLIST = \
	$(top_srcdir)/test/src/*.cs

INTERFACE_DEPS = \
	-r:Mono.GetOptions

BACKEND_DEPS = \
	-r:Mono.GetOptions				\
	-r:Mono.CompilerServices.SymbolWriter		\
	-r:System.Runtime.Serialization.Formatters.Soap	\
	-r:$(srcdir)/Mono.Debugger.Cecil.dll		\
	-r:./Mono.Debugger.dll

MDB_DEPS = \
	-r:Mono.GetOptions				\
	-r:Mono.CompilerServices.SymbolWriter		\
	-r:./Mono.Debugger.Backend.dll			\
	-r:./Mono.Debugger.dll

TEST_DEPS = \
	-r:Mono.GetOptions				\
	-r:./Mono.Debugger.dll				\
	-r:./Mono.Debugger.Backend.dll			\
	-r:./Mono.Debugger.Frontend.dll			\
	-r:nunit.framework				\
	-r:nunit.core

Mono.Debugger.Backend.dll: Makefile $(srcdir)/Mono.Debugger.Cecil.dll $(BACKEND_SRCLIST) Mono.Debugger.dll
	$(MCS) -target:library -out:Mono.Debugger.Backend.dll $(MCS_FLAGS) -keyfile:$(top_srcdir)/build/mono.snk -resource:$(top_srcdir)/classes/DebuggerConfiguration.xsd,DebuggerConfiguration $(BACKEND_DEPS) $(BACKEND_SRCLIST)

Mono.Debugger.dll: Makefile $(INTERFACE_SRCLIST)
	$(MCS) -target:library -out:Mono.Debugger.dll $(MCS_FLAGS) -keyfile:$(top_srcdir)/build/mono.snk $(INTERFACE_DEPS) $(INTERFACE_SRCLIST)

Mono.Debugger.Frontend.dll: Makefile $(MDB_SRCLIST) Mono.Debugger.Backend.dll
	$(MCS) -target:library -out:Mono.Debugger.Frontend.dll $(MCS_FLAGS) -keyfile:$(top_srcdir)/build/mono.snk $(MDB_DEPS) $(MDB_SRCLIST)

Mono.Debugger.Test.dll: Makefile $(TEST_SRCLIST) Mono.Debugger.dll Mono.Debugger.Frontend.dll
	$(MCS) -target:library -out:Mono.Debugger.Test.dll $(MCS_FLAGS) -keyfile:$(top_srcdir)/build/mono.snk $(TEST_DEPS) $(TEST_SRCLIST)

#Mono.Debugger.Cecil.dll: Makefile $(top_srcdir)/cecil/lib/Mono.Cecil.dll.sources
#	(dir=`pwd` && cd $(top_srcdir)/cecil/lib && $(MCS) -target:library -out:$$dir/Mono.Debugger.Cecil.dll $(MCS_FLAGS) @Mono.Cecil.dll.sources -keyfile:$(top_srcdir)/build/mono.snk)

mdb.exe: Makefile $(MDB_SRCLIST) Mono.Debugger.Backend.dll
	$(MCS) -out:mdb.exe $(MDB_DEPS) $(MDB_SRCLIST) $(MCS_FLAGS)

AssemblyInfo.cs: AssemblyInfo.cs.in Makefile
	sed -e 's^\@libdir\@^$(libdir)^g' \
		-e 's^\@prefix\@^$(prefix)^g' \
		-e 's^\@srcdir\@^$(srcdir)^g' \
		-e 's^\@builddir\@^$(builddir)^g' \
		-e 's^\@mono\@^$(MONO)^g' \
		-e 's^\@mcs\@^$(MCS)^g' \
		< $(srcdir)/AssemblyInfo.cs.in > assinfo.tmp \
	&& mv assinfo.tmp AssemblyInfo.cs

noinst_DATA = Mono.Debugger.dll.config Mono.Debugger.Backend.dll.config

CLEANFILES = *.exe *.mdb mdb AssemblyInfo.cs Mono.Debugger.dll.config \
	Mono.Debugger.Backend.dll.config TestResult.* \
	Mono.Debugger.Backend.dll Mono.Debugger.dll Mono.Debugger.Frontend.dll \
	Mono.Debugger.Test.dll

EXTRA_DIST = \
	mdb.in runtests.in mono.snk AssemblyInfo.cs.in Mono.Debugger.dll.config.in \
	Mono.Debugger.Cecil.dll

mdb: mdb.in Makefile
	sed -e 's^\@twodir\@^$(twodir)^g' < $(srcdir)/mdb.in > mdb.tmp \
	&& mv mdb.tmp mdb 

runtests: runtests.in Makefile
	builddir=$(top_builddir) ; the_builddir=`cd $$builddir && pwd` ; \
	sed -e 's^\@NUNIT_CONSOLE_EXE\@^$(twodir)/nunit-console.exe^g' \
		-e 's^\@MONO\@^$(MONO)^g' \
		-e 's^\@NUNIT_CONSOLE_FLAGS\@^$(NUNIT_CONSOLE_FLAGS)^g' \
		-e "s^\@top_builddir\@^$$the_builddir^g" \
		< $(srcdir)/runtests.in > runtests.tmp \
	&& mv runtests.tmp runtests && chmod 755 runtests

install-data-local:
	$(GACUTIL) /i $(srcdir)/Mono.Debugger.Cecil.dll /f $(GACUTIL_FLAGS) || exit 1;
	$(GACUTIL) /i Mono.Debugger.Backend.dll /f $(GACUTIL_FLAGS) || exit 1;
	$(GACUTIL) /i Mono.Debugger.dll /f $(GACUTIL_FLAGS) || exit 1;

uninstall-local:
	$(GACUTIL) /u Mono.Debugger $(GACUTIL_FLAGS) || exit 1;
	$(GACUTIL) /u Mono.Debugger.Backend $(GACUTIL_FLAGS) || exit 1;
	$(GACUTIL) /u Mono.Debugger.Cecil $(GACUTIL_FLAGS) || exit 1;

NUNIT_CONSOLE_FLAGS =

check-local: Mono.Debugger.Test.dll runtests $(TEST_CASE_SRCLIST)
	$(builddir)/runtests /output:TestResult.log /exclude:NotWorking /xml:TestResult.xml || ok=false; \
	(echo ''; cat TestResult.log) | sed '1,/^Tests run: /d'; \
	$$ok

