#include <bfdglue.h>
#include <signal.h>

gboolean
bfd_glue_check_format_object (bfd *abfd)
{
	return bfd_check_format (abfd, bfd_object);
}

int
bfd_glue_get_symbols (bfd *abfd, asymbol ***symbol_table)
{
	int storage_needed = bfd_get_symtab_upper_bound (abfd);

	if (storage_needed <= 0)
		return storage_needed;

	*symbol_table = g_malloc0 (storage_needed);

	return bfd_canonicalize_symtab (abfd, *symbol_table);
}

const char *
bfd_glue_get_symbol (bfd *abfd, asymbol **symbol_table, int index, guint64 *address)
{
	asymbol *symbol = symbol_table [index];

	if (symbol->flags == (BSF_OBJECT | BSF_GLOBAL))
		*address = symbol->section->vma + symbol->value;
	else if (symbol->flags == BSF_FUNCTION)
		*address = symbol->value;
	else
		return NULL;

	return symbol->name;
}

struct disassemble_info *
bfd_glue_init_disassembler (bfd *abfd)
{
	struct disassemble_info *info = g_new0 (struct disassemble_info, 1);

	INIT_DISASSEMBLE_INFO (*info, stderr, fprintf);
	info->flavour = bfd_get_flavour (abfd);
	info->arch = bfd_get_arch (abfd);
	info->mach = bfd_get_mach (abfd);
	info->octets_per_byte = bfd_octets_per_byte (abfd);

	if (bfd_big_endian (abfd))
		info->display_endian = info->endian = BFD_ENDIAN_BIG;
	else if (bfd_little_endian (abfd))
		info->display_endian = info->endian = BFD_ENDIAN_LITTLE;
	else
		g_assert_not_reached ();

	return info;
}

static int
read_memory_func (bfd_vma memaddr, bfd_byte *myaddr, unsigned int length, struct disassemble_info *info)
{
	BfdGlueDisassemblerInfo *data = info->application_data;

	return (* data->read_memory_cb) (memaddr, myaddr, length);
}

static int
fprintf_func (gpointer stream, const char *message, ...)
{
	BfdGlueDisassemblerInfo *data = stream;
	va_list args;
	gchar *output;
	int retval;

	va_start (args, message);
	output = g_strdup_vprintf (message, args);
	va_end (args);

	data->output_cb (output);
	retval = strlen (output);
	g_free (output);

	return retval;
}

void
bfd_glue_setup_disassembler (struct disassemble_info *info, BfdGlueReadMemoryHandler read_memory_cb,
			     BfdGlueOutputHandler output_cb)
{
	BfdGlueDisassemblerInfo *data = g_new0 (BfdGlueDisassemblerInfo, 1);

	data->read_memory_cb = read_memory_cb;
	data->output_cb = output_cb;

	info->application_data = data;
	info->read_memory_func = read_memory_func;
	info->fprintf_func = fprintf_func;
	info->stream = data;
}

void
bfd_glue_free_disassembler (struct disassemble_info *info)
{
	g_free (info->application_data);
	g_free (info);
}

int
bfd_glue_disassemble_insn (disassembler_ftype dis, struct disassemble_info *info, guint64 address)
{
	return dis (address, info);
}
