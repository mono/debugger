noinst_LTLIBRARIES = libopcodes.la

INCLUDES = -D_GNU_SOURCE -DARCH_i386 -I$(srcdir)/../bfd -I$(srcdir)/../bfd/include

libopcodes_la_SOURCES =  dis-buf.c disassemble.c i386-dis.c
libopcodes_la_LDFLAGS = -export-dynamic

EXTRA_DIST = opintl.h
