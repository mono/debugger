## Process this file with automake to generate Makefile.in

SUBDIRS = include hosts

INCDIR = $(srcdir)/include
CSEARCH = -I. -I$(srcdir) -I$(INCDIR)

noinst_LTLIBRARIES = libbfd.la

WARN_CFLAGS = @WARN_CFLAGS@
AM_CFLAGS = $(WARN_CFLAGS)

if bfd_trad_core
trad_core = trad-core.c
bfd_cflags = -DTRAD_CORE
else
trad_core =
bfd_cflags =
endif

# These are defined by configure.in:
BFD_BACKEND = elf32-i386.c elf32.c elf.c elflink.c elf-strtab.c elf-eh-frame.c dwarf1.c i386linux.c aout32.c efi-app-ia32.c peigen.c cofflink.c elf32-gen.c cpu-i386.c pe-i386.c pei-i386.c $(trad_core)
TDEFAULTS =  -DDEFAULT_VECTOR=bfd_elf32_i386_vec -DSELECT_VECS='&bfd_elf32_i386_vec,&i386linux_vec,&bfd_efi_app_ia32_vec,&bfd_elf32_little_generic_vec,&bfd_elf32_big_generic_vec,&i386pe_vec,&i386pei_vec' -DSELECT_ARCHITECTURES='&bfd_i386_arch' -DHAVE_bfd_elf32_i386_vec -DHAVE_i386linux_vec -DHAVE_bfd_efi_app_ia32_vec -DHAVE_bfd_elf32_little_generic_vec -DHAVE_bfd_elf32_big_generic_vec -DHAVE_i386pe_vec -DHAVE_i386pei_vec

INCLUDES = -D_GNU_SOURCE $(bfd_cflags) $(CSEARCH) $(CSWITCHES)

SOURCE_FILES = \
	archive.c archures.c bfd.c cache.c coffgen.c corefile.c \
	format.c init.c libbfd.c opncls.c reloc.c \
	section.c syms.c targets.c hash.c linker.c \
	srec.c binary.c tekhex.c ihex.c stabs.c stab-syms.c \
	merge.c dwarf2.c

libbfd_la_SOURCES = $(BFD_BACKEND) $(SOURCE_FILES)
libbfd_la_LDFLAGS = -export-dynamic
libbfd_la_LIBADD = ../libiberty/libiberty.la

# When compiling archures.c and targets.c, supply the default target
# info from configure.

targets.lo: targets.c Makefile
	$(LIBTOOL) --mode=compile $(COMPILE) -c $(TDEFAULTS) $(srcdir)/targets.c

archures.lo: archures.c Makefile
	$(LIBTOOL) --mode=compile $(COMPILE) -c $(TDEFAULTS) $(srcdir)/archures.c

EXTRA_DIST = coff-i386.c peigen.c aout-target.h aoutx.h bfd.h bfd-in2.h bfd-in.h coffcode.h \
	coffswap.h elf32-target.h elf-bfd.h elfcode.h elfcore.h elflink.h genlink.h libaout.h \
	libbfd.h libbfd-in.h libcoff.h libcoff-in.h libecoff.h libpei.h peicode.h sysdep.h \
	targmatch.h version.h
