SUBDIRS = libiberty bfd opcodes

lib_LTLIBRARIES = libmonodebuggerbfdglue.la

libmonodebuggerbfdglue_la_SOURCES = \
	bfdglue.c		\
	bfdglue.h		\
	#

libmonodebuggerbfdglue_la_LIBADD = opcodes/libopcodes.la bfd/libbfd.la @BASE_DEPENDENCIES_LIBS@

noinst_PROGRAMS = testbfd

testbfd_SOURCES = testbfd.c
testbfd_LDADD = libmonodebuggerbfdglue.la

INCLUDES = -I$(srcdir)/opcodes @BASE_DEPENDENCIES_CFLAGS@

CLEANFILES = lib*.a lib*.dll

EXTRA_DIST = @srcdir@/*.cs
