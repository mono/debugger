#ifndef __BFD_GLUE_H__
#define __BFD_GLUE_H__

#include <glib.h>
#include <bfd.h>
#include <dis-asm.h>

G_BEGIN_DECLS

extern gboolean
bfd_glue_check_format_object (bfd *abfd);

extern int
bfd_glue_get_symbols (bfd *abfd, asymbol ***symbol_table);

extern const char *
bfd_glue_get_symbol (bfd *abfd, asymbol **symbol_table, int idx, guint64 *address);

extern struct disassemble_info *
bfd_glue_init_disassembler (bfd *abfd);

typedef int (*BfdGlueReadMemoryHandler) (guint64 address, bfd_byte *buffer, int size);
typedef void (*BfdGlueOutputHandler) (const char *output);
typedef void (*BfdGluePrintAddressHandler) (guint64 address);

typedef struct {
	BfdGlueReadMemoryHandler read_memory_cb;
	BfdGlueOutputHandler output_cb;
	BfdGluePrintAddressHandler print_address_cb;
} BfdGlueDisassemblerInfo;

extern void
bfd_glue_setup_disassembler (struct disassemble_info *info, BfdGlueReadMemoryHandler read_memory_cb,
			     BfdGlueOutputHandler output_cb, BfdGluePrintAddressHandler print_address_cb);

extern void
bfd_glue_free_disassembler (struct disassemble_info *info);

extern int
bfd_glue_disassemble_insn (disassembler_ftype dis, struct disassemble_info *info, guint64 address);

extern gboolean
bfd_glue_get_section_contents (bfd *abfd, asection *section, guint64 offset,
			       gpointer *data, guint32 *size);

G_END_DECLS

#endif
