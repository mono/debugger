INCLUDES = -g -O0 @SERVER_DEPENDENCIES_CFLAGS@ @server_cflags@

EXTRA_DIST = LibGTop.cs

twodir = $(prefix)/lib/mono/2.0

two_LTLIBRARIES = libmonodebuggertest_support.la

libmonodebuggertest_support_la_SOURCES = \
	libgtop-glue.h		\
	libgtop-glue.c

libmonodebuggertest_support_la_LIBADD = \
	@SERVER_DEPENDENCIES_LIBS@
libmonodebuggertest_support_la_LDFLAGS = \
	-no-undefined -export-dynamic -shared

CLEANFILES = lib*.a lib*.dll

install:
	/bin/true
