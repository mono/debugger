SUBDIRS = opcodes

lib_LTLIBRARIES = libmonodebuggerbfdglue.la

libmonodebuggerbfdglue_la_SOURCES = \
	bfdglue.c		\
	bfdglue.h		\
	#

libmonodebuggerbfdglue_la_LIBADD = opcodes/libopcodes.la @BASE_DEPENDENCIES_LIBS@ -lbfd 

INCLUDES = -I$(srcdir)/opcodes @BASE_DEPENDENCIES_CFLAGS@

CLEANFILES = lib*.a lib*.dll

EXTRA_DIST = @srcdir@/*.cs
