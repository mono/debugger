WARN_CFLAGS = @WARN_CFLAGS@
AM_CFLAGS = $(WARN_CFLAGS)

INCLUDES = -D_GNU_SOURCE -I$(srcdir)/../bfd/include

noinst_LTLIBRARIES = libiberty.la

libiberty_la_SOURCES = \
	concat.c hashtab.c hex.c objalloc.c safe-ctype.c \
	xexit.c xmalloc.c xstrerror.c
libiberty_la_LDFLAGS = -export-dynamic
