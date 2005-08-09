lib_LTLIBRARIES = libmonodebuggerremoting.la

libmonodebuggerremoting_la_SOURCES = \
	mono-debugger-remoting.c

libmonodebuggerremoting_la_LIBADD = @BASE_DEPENDENCIES_LIBS@ @READLINE_DEPLIBS@
libmonodebuggerremoting_la_LDFLAGS = -no-undefined -shared

INCLUDES = @BASE_DEPENDENCIES_CFLAGS@ @glue_cflags@

CLEANFILES = lib*.a lib*.dll

noinst_SCRIPTS = Mono.Debugger.Remoting.dll Test.exe Server.exe

MCS_FLAGS = -debug -define:DEBUG -nowarn:0169,0067

LIBRARY_SRCLIST = \
	$(srcdir)/DebuggerChannel.cs				\
	$(srcdir)/DebuggerClientChannel.cs			\
	$(srcdir)/DebuggerClientTransportSink.cs		\
	$(srcdir)/DebuggerClientTransportSinkProvider.cs	\
	$(srcdir)/DebuggerServerChannel.cs			\
	$(srcdir)/DebuggerServerTransportSink.cs		\
	$(srcdir)/DebuggerServerDispatchSink.cs			\
	$(srcdir)/DebuggerServerDispatchSinkProvider.cs		\
	$(srcdir)/DebuggerServerResponseSink.cs			\
	$(srcdir)/DebuggerMessageFormat.cs			\
	$(srcdir)/DebuggerRemotingException.cs			\
	$(srcdir)/DebuggerClientConnection.cs			\
	$(srcdir)/DebuggerServerConnection.cs			\
	$(srcdir)/DebuggerStream.cs

LIBRARY_DEPS = -r:System.Runtime.Remoting

FOO_SRCLIST = \
	$(srcdir)/Foo.cs
FOO_DEPS =

TEST_SRCLIST = \
	$(srcdir)/Test.cs

TEST_DEPS = -r:Mono.Debugger.Remoting -r:Foo

SERVER_SRCLIST = \
	$(srcdir)/Server.cs

SERVER_DEPS = -r:Mono.Debugger.Remoting -r:Foo

Mono.Debugger.Remoting.dll: $(LIBRARY_SRCLIST) libmonodebuggerremoting.la Makefile
	$(MCS) -target:library -out:Mono.Debugger.Remoting.dll $(LIBRARY_DEPS) $(LIBRARY_SRCLIST) $(MCS_FLAGS)

Foo.dll: $(FOO_SRCLIST) libmonodebuggerremoting.la Makefile
	$(MCS) -target:library -out:Foo.dll $(FOO_DEPS) $(FOO_SRCLIST) $(MCS_FLAGS)

Test.exe: $(TEST_SRCLIST) libmonodebuggerremoting.la Mono.Debugger.Remoting.dll Foo.dll
	$(MCS) -out:Test.exe $(TEST_DEPS) $(TEST_SRCLIST) $(MCS_FLAGS)

Server.exe: $(SERVER_SRCLIST) libmonodebuggerremoting.la Mono.Debugger.Remoting.dll Foo.dll
	$(MCS) -out:Server.exe $(SERVER_DEPS) $(SERVER_SRCLIST) $(MCS_FLAGS)

