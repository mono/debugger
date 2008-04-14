INCLUDES = -g -O0 @SERVER_DEPENDENCIES_CFLAGS@ @LIBGTOP_CFLAGS@ @server_cflags@

twodir = $(prefix)/lib/mono/2.0

if HAVE_LIBGTOP
libgtop_libraries = libmonodebuggertest_support.la
else
libgtop_libraries =
endif

two_LTLIBRARIES = $(libgtop_libraries)

libmonodebuggertest_support_la_SOURCES = \
	libgtop-glue.h		\
	libgtop-glue.c

libmonodebuggertest_support_la_LIBADD = \
	@SERVER_DEPENDENCIES_LIBS@ @LIBGTOP_LIBS@
libmonodebuggertest_support_la_LDFLAGS = \
	-no-undefined -export-dynamic -shared

CLEANFILES = lib*.a lib*.dll

install:
	/bin/true
