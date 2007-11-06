#ifndef __BFD_GLUE_H__
#define __BFD_GLUE_H__

#include <glib.h>
#include <bfd.h>
#include <dis-asm.h>

G_BEGIN_DECLS

extern gchar *
bfd_glue_get_target_name (bfd *abfd);

extern gboolean
bfd_glue_check_format_object (bfd *abfd);

extern gboolean
bfd_glue_check_format_core (bfd *abfd);

extern int
bfd_glue_get_symbols (bfd *abfd, asymbol ***symbol_table);

extern gchar *
bfd_glue_get_symbol (bfd *abfd, asymbol **symbol_table, int idx, int *is_function, guint64 *address);

extern int
bfd_glue_get_dynamic_symbols (bfd *abfd, asymbol ***symbol_table);

typedef int (*BfdGlueReadMemoryHandler) (guint64 address, bfd_byte *buffer, int size);
typedef void (*BfdGlueOutputHandler) (const char *output);
typedef void (*BfdGluePrintAddressHandler) (guint64 address);

typedef struct {
	struct disassemble_info *info;
	BfdGlueReadMemoryHandler read_memory_cb;
	BfdGlueOutputHandler output_cb;
	BfdGluePrintAddressHandler print_address_cb;
	disassembler_ftype disassembler;
} BfdGlueDisassemblerInfo;

extern BfdGlueDisassemblerInfo *
bfd_glue_create_disassembler (gboolean is_x86_64,
			      BfdGlueReadMemoryHandler read_memory_cb,
			      BfdGlueOutputHandler output_cb,
			      BfdGluePrintAddressHandler print_address_cb);

extern void
bfd_glue_free_disassembler (BfdGlueDisassemblerInfo *handle);

extern int
bfd_glue_disassemble_insn (BfdGlueDisassemblerInfo *handle, guint64 address);

typedef enum {
	SECTION_FLAGS_LOAD	= 1,
	SECTION_FLAGS_ALLOC	= 2,
	SECTION_FLAGS_READONLY	= 4
} BfdGlueSectionFlags;

extern bfd *
bfd_glue_openr (const char *filename, const char *target);

extern gchar *
bfd_glue_get_errormsg (void);

extern gboolean
bfd_glue_get_section_contents (bfd *abfd, asection *section, gpointer data, guint32 size);

extern guint64
bfd_glue_get_section_vma (asection *p);

extern gchar *
bfd_glue_get_section_name (asection *p);

extern guint32
bfd_glue_get_section_size (asection *p);

extern BfdGlueSectionFlags
bfd_glue_get_section_flags (asection *p);

extern asection *
bfd_glue_get_first_section (bfd *abfd);

extern asection *
bfd_glue_get_next_section (asection *p);

extern gchar *
bfd_glue_core_file_failing_command (bfd *abfd);

extern guint64
bfd_glue_elfi386_locate_base (bfd *abfd, const guint8 *data, int size);

extern gboolean
bfd_glue_core_file_elfi386_get_registers (const guint8 *data, int size, guint32 *regs);

G_END_DECLS

#endif
