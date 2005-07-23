noinst_LTLIBRARIES = libopcodes.la

if BFD_TARGET_POWERPC
arch_cflags = -DARCH_powerpc -DARCH_i386
arch_sources = i386-dis.c ppc-dis.c ppc-opc.c
else
if BFD_TARGET_X86_64
arch_cflags = -DARCH_i386 -DBFD64
arch_sources = i386-dis.c
else
arch_cflags = -DARCH_i386
arch_sources = i386-dis.c
endif
endif

INCLUDES = -D_GNU_SOURCE $(arch_cflags) -I$(srcdir)/../bfd -I$(srcdir)/../bfd/include

libopcodes_la_SOURCES =  dis-buf.c disassemble.c $(arch_sources)
libopcodes_la_LDFLAGS = -export-dynamic

EXTRA_DIST = opintl.h sysdep.h
