INCLUDES = @SERVER_DEPENDENCIES_CFLAGS@ @server_cflags@

lib_LTLIBRARIES = libmonodebuggerserver.la

EXTRA_libmonodebuggerserver_la_SOURCES = \
	i386-linux-ptrace.c		\
	i386-linux-ptrace.h		\
	i386-win32.c			\
	i386-win32.h			\
	i386-arch.c			\
	i386-arch.h			\
	i386-ptrace.c			\
	i386-ptrace.h

if MARTIN_HACKS
martin_hack_srclist = \
	remote-client.c		\
	Debugger-stubs.c	\
	Debugger-common.c

martin_hack_progs = debugger-remote-server

Debugger-stubs.c Debugger-common.c Debugger-skels.c Debugger.h : Debugger.idl
	orbit-idl-2 $(srcdir)/Debugger.idl
else
martin_hack_srclist =
martin_hack_progs =
endif

if PLATFORM_POWERPC
server_sources = powerpc-darwin.c
else
server_sources = i386-ptrace.c
endif

libexec_PROGRAMS = \
	$(martin_hack_progs)

debugger_remote_server_SOURCES = \
	remote-server.c		\
	debugger-srv.c		\
	Debugger-skels.c

libmonodebuggerserver_la_SOURCES = \
	server.h		\
	library.c		\
	$(martin_hack_srclist)	\
	$(server_sources)	\
	breakpoints.c		\
	breakpoints.h

debugger_remote_server_LDADD = @SERVER_DEPENDENCIES_LIBS@ libmonodebuggerserver.la
debugger_remote_server_LDFLAGS = -static

libmonodebuggerserver_la_LIBADD = @SERVER_DEPENDENCIES_LIBS@
libmonodebuggerserver_la_LDFLAGS = -no-undefined -module -export-dynamic -shared

CLEANFILES = lib*.a lib*.dll
