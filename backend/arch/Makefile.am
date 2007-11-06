SUBDIRS = libiberty bfd opcodes

noinst_LTLIBRARIES = libmonodebuggerbfdglue.la

libmonodebuggerbfdglue_la_SOURCES = \
	bfdglue.c		\
	bfdglue.h

libmonodebuggerbfdglue_la_LIBADD = opcodes/libopcodes.la bfd/libbfd.la @BASE_DEPENDENCIES_LIBS@
libmonodebuggerbfdglue_la_LDFLAGS = -no-undefined -module -export-dynamic -shared

INCLUDES = -I$(srcdir)/bfd -I$(srcdir)/bfd/include -I$(srcdir)/opcodes @BASE_DEPENDENCIES_CFLAGS@

CLEANFILES = lib*.a lib*.dll

EXTRA_DIST = \
	Architecture.cs Architecture_I386.cs Architecture_X86_64.cs BfdContainer.cs Bfd.cs \
	BfdDisassembler.cs CoreFile.cs Disassembler.cs DwarfFrameReader.cs DwarfReader.cs \
	Instruction.cs Instruction_I386.cs Instruction_X86_64.cs Opcodes.cs Opcodes_I386.cs \
	Opcodes_X86_64.cs X86_Instruction.cs X86_Opcodes.cs
