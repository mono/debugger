## Process this file with automake to generate Makefile.in

INCDIR = $(srcdir)/include
CSEARCH = -I. -I$(srcdir) -I$(INCDIR)

noinst_LTLIBRARIES = libbfd.la

WARN_CFLAGS = @WARN_CFLAGS@
AM_CFLAGS = $(WARN_CFLAGS)

# These are defined by configure.in:
BFD_BACKENDS = elf.c elf-eh-frame.c elf-strtab.c elflink.c elf32.c elf32-i386.c
BFD_MACHINES = cpu-i386.c
BFD_COREFILE = trad-core.c
TDEFAULTS = -DSELECT_ARCHITECTURES='bfd_arch_i386' -DSELECT_VECS='&bfd_elf32_i386_vec' -DDEFAULT_VECTOR='bfd_elf32_i386_vec'

INCLUDES = -D_GNU_SOURCE $(CSEARCH) $(CSWITCHES)

SOURCE_FILES = \
	archive.c archures.c bfd.c cache.c coffgen.c corefile.c \
	format.c init.c libbfd.c opncls.c reloc.c \
	section.c syms.c targets.c hash.c linker.c \
	srec.c binary.c tekhex.c ihex.c stabs.c stab-syms.c \
	merge.c dwarf1.c dwarf2.c

libbfd_la_SOURCES = $(BFD_BACKENDS) $(BFD_MACHINES) $(BFD_COREFILE) $(SOURCE_FILES)
libbfd_la_LDFLAGS = -export-dynamic
libbfd_la_LIBADD = ../libiberty/libiberty.la

# This file holds an array associating configuration triplets and
# vector names.  It is built from config.bfd.  It is not compiled by
# itself, but is included by targets.c.
targmatch.h: config.bfd targmatch.sed
	rm -f targmatch.h
	sed -f $(srcdir)/targmatch.sed < $(srcdir)/config.bfd > targmatch.new
	mv -f targmatch.new targmatch.h

# When compiling archures.c and targets.c, supply the default target
# info from configure.

targets.lo: targets.c Makefile
	$(LIBTOOL) --mode=compile $(COMPILE) -c $(TDEFAULTS) $(srcdir)/targets.c

archures.lo: archures.c Makefile
	$(LIBTOOL) --mode=compile $(COMPILE) -c $(TDEFAULTS) $(srcdir)/archures.c

elf32-target.h : elfxx-target.h
	rm -f elf32-target.h
	sed -e s/NN/32/g < $(srcdir)/elfxx-target.h > elf32-target.new
	mv -f elf32-target.new elf32-target.h

peigen.c : peXXigen.c
	rm -f peigen.c
	sed -e s/XX/pe/g < $(srcdir)/peXXigen.c > peigen.new
	mv -f peigen.new peigen.c

pepigen.c : peXXigen.c
	rm -f pepigen.c
	sed -e s/XX/pep/g < $(srcdir)/peXXigen.c > pepigen.new
	mv -f pepigen.new pepigen.c


