twodir = $(prefix)/lib/mono/2.0

bin_SCRIPTS = mdb mdb-symbolreader

two_SCRIPTS = mdb.exe mdb-symbolreader.exe

MCS_FLAGS = -debug -define:DEBUG -define:DEBUGGER_SOURCE -nowarn:0169,0067

if MONO_TRUNK
MCS_FLAGS += -define:MONO_TRUNK
endif

if MARTIN_PRIVATE
# Enable some more stuff for me.
if ATTACHING_SUPPORTED
EXCLUDED_TESTS = NotWorking
else
EXCLUDED_TESTS = NotWorking,Attach
endif
else
# Exclude anything which may potentially break, only enable the
# 100% safe tests.
if ATTACHING_SUPPORTED
EXCLUDED_TESTS = NotWorking,Native,Threads,AppDomain,GUI
else
EXCLUDED_TESTS = NotWorking,Attach,Native,Threads,AppDomain,GUI
endif
endif

if HAVE_XSP
MCS_FLAGS += -define:HAVE_XSP
endif

noinst_PROGRAMS = \
	ulimit-check

noinst_SCRIPTS = \
	runtests					\
	Mono.Debugger.dll				\
	Mono.Debugger.Frontend.dll			\
	Mono.Debugger.Test.Framework.dll		\
	Mono.Debugger.Test.dll				\
	debugger-test-server.exe

LANGUAGE_SPECIFIC_FILES = \
	$(top_builddir)/frontend/CSharpExpressionParser.cs	\
	$(top_srcdir)/frontend/CSharpTokenizer.cs

MDB_SRCLIST = \
	$(top_srcdir)/frontend/CL.cs			\
	$(top_srcdir)/frontend/Command.cs		\
	$(top_srcdir)/frontend/Completer.cs		\
	$(top_srcdir)/frontend/DebuggerTextWriter.cs	\
	$(top_srcdir)/frontend/Expression.cs		\
	$(top_srcdir)/frontend/ExpressionParser.cs	\
	$(top_srcdir)/frontend/Interpreter.cs		\
	$(top_srcdir)/frontend/Main.cs			\
	$(top_srcdir)/frontend/MyTextReader.cs		\
	$(top_srcdir)/frontend/ScriptingContext.cs	\
	$(top_srcdir)/frontend/Style.cs			\
	$(top_srcdir)/frontend/ObjectFormatter.cs	\
	$(top_srcdir)/frontend/getline.cs		\
	$(LANGUAGE_SPECIFIC_FILES)			\
	AssemblyInfo.cs

DEBUGGER_SRCLIST = \
	$(top_srcdir)/interface/*.cs			\
	$(top_srcdir)/backend/arch/*.cs			\
	$(top_srcdir)/backend/os/*.cs			\
	$(top_srcdir)/backend/mono/*.cs			\
	$(top_srcdir)/backend/*.cs			\
	$(top_srcdir)/languages/*.cs			\
	$(top_srcdir)/languages/mono/*.cs		\
	$(top_srcdir)/languages/native/*.cs		\
	$(top_srcdir)/classes/*.cs			\
	AssemblyInfo.cs

SYMBOL_WRITER_SRCLIST = \
	$(top_srcdir)/symbolwriter/MonoSymbolFile.cs		\
	$(top_srcdir)/symbolwriter/MonoSymbolTable.cs		\
	AssemblyInfo.cs

SYMBOL_READER_SRCLIST = \
	$(top_srcdir)/symbolwriter/ndesk-options.cs		\
	$(top_srcdir)/symbolwriter/MonoSymbolFile.cs		\
	$(top_srcdir)/symbolwriter/MonoSymbolTable.cs		\
	$(top_srcdir)/symbolwriter/MdbSymbolReader.cs		\
	AssemblyInfo.cs

TEST_FRAMEWORK_SRCLIST = \
	$(top_srcdir)/test/framework/DebuggerTestFixture.cs		\
	$(top_srcdir)/test/framework/DebuggerTestFixtureAttribute.cs	\
	$(top_srcdir)/test/framework/DebuggerTestHost.cs		\
	$(top_srcdir)/test/framework/RemotingContract.cs		\
	$(top_srcdir)/test/framework/ProxyListener.cs			\
	$(top_srcdir)/test/framework/LibGTop.cs				\
	AssemblyInfo.cs

TEST_FRAMEWORK_DEPS = \
	-r:./Mono.Debugger.dll				\
	-r:./Mono.Debugger.Frontend.dll			\
	-r:System.Runtime.Remoting			\
	-r:nunit.framework				\
	-r:nunit.core					\
	-r:nunit.core.interfaces

TEST_SRCLIST = \
	$(top_srcdir)/test/testsuite/*.cs		\
	AssemblyInfo.cs

TEST_CASE_SRCLIST = \
	$(top_srcdir)/test/src/*.cs

TEST_CASE_EXELIST = \
	$(top_builddir)/test/src/*.exe			\
	$(top_builddir)/test/src/*.dll

SYMBOL_WRITER_DEPS = \
	-r:./Mono.Cecil.dll

SYMBOL_READER_DEPS = \
	-r:./Mono.Cecil.dll

DEBUGGER_DEPS = \
	-r:Mono.Debugger.SymbolWriter			\
	-r:System.Runtime.Serialization.Formatters.Soap	\
	-r:./Mono.Cecil.dll

MDB_DEPS = \
	-r:./Mono.Debugger.dll

TEST_DEPS = \
	-r:./Mono.Debugger.dll				\
	-r:./Mono.Debugger.Frontend.dll			\
	-r:./Mono.Debugger.Test.Framework.dll		\
	-r:nunit.framework				\
	-r:nunit.core					\
	-r:nunit.core.interfaces

DEBUGGER_TEST_SERVER_SRCLIST = \
	$(top_srcdir)/test/framework/DebuggerTestServer.cs

DEBUGGER_TEST_SERVER_DEPS = \
	-r:Mono.Debugger.Test.Framework.dll		\
	-r:System.Runtime.Remoting			\
	-r:nunit.framework				\
	-r:nunit.core					\
	-r:nunit.core.interfaces			\
	-r:nunit.util

if MARTIN_PRIVATE
MCS_FLAGS += -define:MARTIN_PRIVATE
endif

Mono.Debugger.SymbolWriter.dll: Makefile Mono.Cecil.dll $(SYMBOL_WRITER_SRCLIST)
	$(MCS) -target:library -out:Mono.Debugger.SymbolWriter.dll -d:CECIL $(MCS_FLAGS) -keyfile:$(top_srcdir)/build/mono.snk $(SYMBOL_WRITER_DEPS) $(SYMBOL_WRITER_SRCLIST)

Mono.Debugger.dll: Makefile Mono.Debugger.SymbolWriter.dll $(DEBUGGER_SRCLIST)
	$(MCS) -target:library -out:Mono.Debugger.dll $(MCS_FLAGS) -keyfile:$(top_srcdir)/build/mono.snk -resource:$(top_srcdir)/classes/DebuggerConfiguration.xsd,DebuggerConfiguration $(DEBUGGER_DEPS) $(DEBUGGER_SRCLIST)

Mono.Debugger.Frontend.dll: Makefile $(MDB_SRCLIST) Mono.Debugger.dll
	$(MCS) -target:library -out:Mono.Debugger.Frontend.dll $(MCS_FLAGS) -keyfile:$(top_srcdir)/build/mono.snk $(MDB_DEPS) $(MDB_SRCLIST)

Mono.Debugger.Test.Framework.dll: Makefile $(TEST_FRAMEWORK_SRCLIST) Mono.Debugger.dll Mono.Debugger.Frontend.dll
	$(MCS) -target:library -out:Mono.Debugger.Test.Framework.dll $(MCS_FLAGS) -keyfile:$(top_srcdir)/build/mono.snk $(TEST_FRAMEWORK_DEPS) $(TEST_FRAMEWORK_SRCLIST)

Mono.Debugger.Test.dll: Makefile $(TEST_SRCLIST) Mono.Debugger.dll Mono.Debugger.Frontend.dll Mono.Debugger.Test.Framework.dll
	$(MCS) -target:library -out:Mono.Debugger.Test.dll $(MCS_FLAGS) -keyfile:$(top_srcdir)/build/mono.snk $(TEST_DEPS) $(TEST_SRCLIST)

Mono.Cecil.dll:
	cp $(CECIL_ASM) $(top_builddir)/build

mdb.exe: Makefile $(MDB_SRCLIST) Mono.Debugger.dll
	$(MCS) -out:mdb.exe $(MDB_DEPS) $(MDB_SRCLIST) $(MCS_FLAGS)

mdb-symbolreader.exe: Makefile $(SYMBOL_READER_SRCLIST) Mono.Debugger.SymbolWriter.dll
	$(MCS) -out:mdb-symbolreader.exe $(SYMBOL_READER_DEPS) $(SYMBOL_READER_SRCLIST) $(MCS_FLAGS) -d:CECIL -d:LINQ -d:NDESK_OPTIONS

debugger-test-server.exe: Makefile $(DEBUGGER_TEST_SERVER_SRCLIST)
	$(MCS) -out:debugger-test-server.exe $(DEBUGGER_TEST_SERVER_SRCLIST) $(DEBUGGER_TEST_SERVER_DEPS) $(MCS_FLAGS) -d:LINQ

AssemblyInfo.cs: AssemblyInfo.cs.in Makefile
	builddir=$(top_builddir) ; the_builddir=`cd $$builddir && pwd` ; \
	srcdir=$(top_srcdir) ; the_srcdir=`cd $$srcdir && pwd` ; \
	sed -e 's^\@libdir\@^$(libdir)^g' \
		-e 's^\@prefix\@^$(prefix)^g' \
		-e "s^\@srcdir\@^$$the_srcdir^g" \
		-e "s^\@builddir\@^$$the_builddir^g" \
		-e 's^\@mono\@^$(TARGET_MONO)^g' \
		-e 's^\@mcs\@^$(TARGET_MCS)^g' \
		-e 's^\@monodis\@^$(TARGET_MONODIS)^g' \
		-e 's^\@xsp\@^$(XSP_EXE)^g' \
		< $(srcdir)/AssemblyInfo.cs.in > assinfo.tmp \
	&& mv assinfo.tmp AssemblyInfo.cs

noinst_DATA = Mono.Debugger.dll.config

CLEANFILES = *.exe *.mdb mdb AssemblyInfo.cs Mono.Debugger.dll.config \
	TestResult.* Mono.Debugger.dll Mono.Debugger.Frontend.dll \
	Mono.Debugger.Test.dll Mono.Debugger.SymbolWriter.dll \
	Mono.Cecil.dll runtests

EXTRA_DIST = \
	mdb.in mdb-symbolreader.in runtests.in mono.snk AssemblyInfo.cs.in \
	Mono.Debugger.dll.config.in

mdb: mdb.in Makefile
	sed -e 's^\@twodir\@^$(twodir)^g' \
	    -e 's^\@libdir\@^$(libdir)^g' \
	    -e 's^\@mono\@^$(MONO)^g' \
		< $(srcdir)/mdb.in > mdb.tmp \
	&& mv mdb.tmp mdb 

mdb-symbolreader: mdb-symbolreader.in Makefile
	sed -e 's^\@twodir\@^$(twodir)^g' \
	    -e 's^\@libdir\@^$(libdir)^g' \
	    -e 's^\@mono\@^$(MONO)^g' \
		< $(srcdir)/mdb-symbolreader.in > mdb-symbolreader.tmp \
	&& mv mdb-symbolreader.tmp mdb-symbolreader

NUNIT_CONSOLE_EXE = $(NUNIT_PREFIX)/lib/mono/2.0/nunit-console.exe

runtests: runtests.in Makefile ulimit-check
	builddir=$(top_builddir) ; the_builddir=`cd $$builddir && pwd` ; \
	sed -e 's^\@NUNIT_CONSOLE_EXE\@^$(NUNIT_CONSOLE_EXE)^g' \
		-e 's^\@MONO\@^$(TARGET_MONO)^g' \
		-e 's^\@NUNIT_CONSOLE_FLAGS\@^$(NUNIT_CONSOLE_FLAGS)^g' \
		-e "s^\@top_builddir\@^$$the_builddir^g" \
		< $(srcdir)/runtests.in > runtests.tmp \
	&& mv runtests.tmp runtests && chmod 755 runtests

install-data-local:
	$(GACUTIL) /i Mono.Debugger.dll /f $(GACUTIL_FLAGS) || exit 1;
	$(GACUTIL) /i Mono.Debugger.Frontend.dll /f $(GACUTIL_FLAGS) || exit 1;
	$(GACUTIL) /i Mono.Debugger.SymbolWriter.dll /f $(GACUTIL_FLAGS) || exit 1;

uninstall-local:
	$(GACUTIL) /u Mono.Debugger $(GACUTIL_FLAGS) || exit 1;
	$(GACUTIL) /u Mono.Debugger.Frontend $(GACUTIL_FLAGS) || exit 1;
	$(GACUTIL) /u Mono.Debugger.SymbolWriter $(GACUTIL_FLAGS) || exit 1;

STANDARD_NUNIT_CONSOLE_FLAGS = -noshadow -labels -exclude:$(EXCLUDED_TESTS)
NUNIT_CONSOLE_FLAGS = -noshadow

verify-local: mdb-symbolreader.exe $(TEST_CASE_SRCLIST)
	$(MONO) --debug mdb-symbolreader.exe $(TEST_CASE_EXELIST)

check-local: Mono.Debugger.Test.dll runtests $(TEST_CASE_SRCLIST) verify-local
#	$(builddir)/runtests /output:TestResult.log /exclude:$(EXCLUDED_TESTS) /xml:TestResult.xml || ok=false; \
#	(echo ''; cat TestResult.log) | sed '1,/^Tests run: /d'; \
#	$$ok
	$(builddir)/runtests $(STANDARD_NUNIT_CONSOLE_FLAGS) || ok=false; \
	$$ok

