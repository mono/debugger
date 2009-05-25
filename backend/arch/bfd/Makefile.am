## Process this file with automake to generate Makefile.in

SUBDIRS = include hosts

INCDIR = $(srcdir)/include
CSEARCH = -I. -I$(srcdir) -I$(INCDIR)

noinst_LTLIBRARIES = libbfd.la

WARN_CFLAGS = @WARN_CFLAGS@
AM_CFLAGS = $(WARN_CFLAGS)

powerpc_backend = mach-o.c pef.c rs6000-core.c xsym.c cpu-rs6000.c cpu-powerpc.c
powerpc_vec_defines = -DHAVE_mach_o_be_vec -DHAVE_mach_o_le_vec -DHAVE_mach_o_fat_vec -DHAVE_pef_vec -DHAVE_pef_xlib_vec -DHAVE_sym_vec

i386_linux_backend = elf32-i386.c elf32.c elf.c elflink.c elf-strtab.c elf-eh-frame.c dwarf1.c i386linux.c aout32.c efi-app-ia32.c peigen.c cofflink.c elf32-gen.c cpu-i386.c pe-i386.c pei-i386.c trad-core.c
i386_linux_vec_defines = -DHAVE_bfd_elf32_i386_vec -DHAVE_i386linux_vec -DHAVE_bfd_efi_app_ia32_vec -DHAVE_bfd_elf32_little_generic_vec -DHAVE_bfd_elf32_big_generic_vec -DHAVE_i386pe_vec -DHAVE_i386pei_vec

x86_64_linux_backend = elf64-x86-64.c elf64.c elf32-i386.c elf32.c elf.c elflink.c elf-strtab.c elf-eh-frame.c dwarf1.c i386linux.c aout32.c efi-app-ia32.c peigen.c cofflink.c elf64-gen.c elf32-gen.c cpu-i386.c pe-i386.c pei-i386.c trad-core.c archive64.c
x86_64_linux_vec_defines = -DHAVE_bfd_elf64_x86_64_vec -DHAVE_bfd_elf32_i386_vec -DHAVE_i386linux_vec -DHAVE_bfd_efi_app_ia32_vec -DHAVE_bfd_elf64_little_generic_vec -DHAVE_bfd_elf64_big_generic_vec -DHAVE_bfd_elf32_little_generic_vec -DHAVE_bfd_elf32_big_generic_vec

i386_mach_backend = mach-o.c pef.c xsym.c cpu-i386.c
i386_mach_vec_defines = -DHAVE_mach_o_be_vec -DHAVE_mach_o_le_vec -DHAVE_mach_o_fat_vec -DHAVE_pef_vec -DHAVE_pef_xlib_vec -DHAVE_sym_vec

if BFD_TARGET_POWERPC
BFD_BACKEND = $(powerpc_backend) $(i386_linux_backend)
TDEFAULTS =  -DDEFAULT_VECTOR=mach_o_be_vec -DSELECT_VECS='&mach_o_be_vec,&mach_o_le_vec,&mach_o_fat_vec,&pef_vec,&pef_xlib_vec,&sym_vec,&bfd_elf32_i386_vec,&i386linux_vec,&bfd_efi_app_ia32_vec,&bfd_elf32_little_generic_vec,&bfd_elf32_big_generic_vec,&i386pe_vec,&i386pei_vec' -DSELECT_ARCHITECTURES='&bfd_powerpc_arch,&bfd_rs6000_arch,&bfd_i386_arch' $(powerpc_vec_defines) $(i386_linux_vec_defines)
bfd_cflags =
else
if BFD_TARGET_X86_64
BFD_BACKEND = $(x86_64_linux_backend)
TDEFAULTS =  -DDEFAULT_VECTOR=bfd_elf64_x86_64_vec -DSELECT_VECS='&bfd_elf64_x86_64_vec,&bfd_elf32_i386_vec,&i386linux_vec,&bfd_efi_app_ia32_vec,&bfd_elf64_little_generic_vec,&bfd_elf64_big_generic_vec,&bfd_elf32_little_generic_vec,&bfd_elf32_big_generic_vec' -DSELECT_ARCHITECTURES='&bfd_i386_arch' $(x86_64_linux_vec_defines)
bfd_cflags = -DTRAD_CORE -DBFD64
else
if BFD_TARGET_X86_MACH
BFD_BACKEND = $(i386_mach_backend)
TDEFAULTS =  -DDEFAULT_VECTOR=mach_o_be_vec -DSELECT_VECS='&mach_o_be_vec,&mach_o_le_vec,&mach_o_fat_vec,&pef_vec,&pef_xlib_vec,&sym_vec' -DSELECT_ARCHITECTURES='&bfd_i386_arch' $(i386_mach_vec_defines)
bfd_cflags =
else
BFD_BACKEND = $(i386_linux_backend)
TDEFAULTS =  -DDEFAULT_VECTOR=bfd_elf32_i386_vec -DSELECT_VECS='&bfd_elf32_i386_vec,&i386linux_vec,&bfd_efi_app_ia32_vec,&bfd_elf32_little_generic_vec,&bfd_elf32_big_generic_vec,&i386pe_vec,&i386pei_vec' -DSELECT_ARCHITECTURES='&bfd_i386_arch' $(i386_linux_vec_defines)
bfd_cflags = -DTRAD_CORE
endif
endif
endif

INCLUDES = -D_GNU_SOURCE $(bfd_cflags) $(CSEARCH) $(CSWITCHES)

SOURCE_FILES = \
	archive.c archures.c bfd.c cache.c coffgen.c corefile.c \
	format.c init.c libbfd.c opncls.c reloc.c \
	section.c syms.c targets.c hash.c linker.c \
	srec.c binary.c tekhex.c ihex.c stabs.c stab-syms.c \
	merge.c dwarf2.c bfdio.c simple.c

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
	targmatch.h version.h bfdver.h elf64-target.h
