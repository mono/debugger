SUBDIRS = bfd

noinst_LTLIBRARIES = libmonodebuggerbfdglue.la

libmonodebuggerbfdglue_la_SOURCES = \
	bfdglue.c		\
	bfdglue.h

libmonodebuggerbfdglue_la_LIBADD = bfd/opcodes/libopcodes.la bfd/libbfd.la @BASE_DEPENDENCIES_LIBS@
libmonodebuggerbfdglue_la_LDFLAGS = -no-undefined -module -export-dynamic -shared

INCLUDES = -I$(srcdir)/bfd -I$(srcdir)/bfd/include -I$(srcdir)/bfd/opcodes @BASE_DEPENDENCIES_CFLAGS@

CLEANFILES = lib*.a lib*.dll

EXTRA_DIST = $(srcdir)/*.cs
