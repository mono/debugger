lib_LTLIBRARIES = libmonodebuggerbfdglue.la

libmonodebuggerbfdglue_la_SOURCES = \
	bfdglue.c		\
	bfdglue.h		\
	#

libmonodebuggerbfdglue_la_LIBADD = @BASE_DEPENDENCIES_LIBS@ -lbfd -lopcodes

INCLUDES = @BASE_DEPENDENCIES_CFLAGS@

CLEANFILES = lib*.a lib*.dll

EXTRA_DIST = *.cs
