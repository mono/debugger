using GLib;
//
// The register display code.
//
// Authors:
//   Martin Baulig (martin@gnome.org)
//   Miguel de Icaza (miguel@ximian.com)
//
// The register display will have to be implemented once per target
// so that we can achieve the best display layout for the registers, as
// a general purpose routine will not serve the purposes we need.
//
// Currently the only supported view is the x86 view.
//
// The actual widgets are laid out in Glade, and we use a notebook with various
// pages, and depending on the architecture, we select the proper page to show.
//
// Strategy:
//    I also want to widget.Hide () most of the useless registers and flags
//    except when the "advanced" mode is turned on, and then everything is shown.
//
// (C) 2002 Ximian, Inc.
//
using Gtk;
using System;
using System.IO;
using System.Runtime.InteropServices;
using Mono.Debugger;

namespace Mono.Debugger.GUI
{
	public class RegisterDisplay : DebuggerWidget
	{
		Glade.XML gxml;
		Gtk.Notebook notebook;
		IArchitecture arch;
		StackFrame current_frame;
		Gdk.Color color_change, color_stable;
		
		public RegisterDisplay (Glade.XML gxml, Gtk.Container window, Gtk.Notebook notebook)
			: base (window, notebook)
		{
			this.gxml = gxml;
			this.notebook = notebook;

			color_change.red = 0xffff;
			color_change.green = 0;
			color_change.blue = 0;

			color_stable.red = 0;
			color_stable.green = 0;
			color_stable.blue = 0;
		}

		public override void SetBackend (DebuggerBackend backend)
		{
			base.SetBackend (backend);

			//
			// This is really ugly, I would like to use a different handler for this
			//
			backend.InferiorStateNotify += new DebuggerBackend.InferiorState (InferiorRunning);
		}

		void InferiorRunning (bool state)
		{
			if (state){
				arch = backend.Architecture;
				
				if (arch is ArchitectureI386){
					notebook.Page = 1;
					SetupI386 ();
				} else
					notebook.Page = 0;
			} else {
				notebook.Page = 0;
				arch = null;
			}
		}
		
		//
		// The i386 registers
		//
		Gtk.Entry i386_eax, i386_ebx, i386_ecx, i386_edx;
		Gtk.Entry i386_esi, i386_edi, i386_ebp, i386_esp;
		Gtk.Entry i386_ecs, i386_eds, i386_ees, i386_ess;
		Gtk.Entry i386_eip, i386_efs, i386_egs;
		Gtk.ToggleButton i386_cf, i386_pf, i386_af, i386_zf;
		Gtk.ToggleButton i386_sf, i386_tf, i386_if, i386_df;
		Gtk.ToggleButton i386_of, i386_nt, i386_rf, i386_vm;
		Gtk.ToggleButton i386_ac, i386_vif, i386_id, i386_vip;

		Gtk.Entry GetEntry (string name, EventHandler ev)
		{
			Gtk.Entry entry = (Gtk.Entry) gxml [name];

			if (entry == null)
				Console.WriteLine ("COULD NOT FIND: " + name);

			return entry;
		}

		void i386_eax_changed (object sender, EventArgs e)
		{
		}
		
		void i386_ebx_changed (object sender, EventArgs e)
		{
		}
		
		void i386_ecx_changed (object sender, EventArgs e)
		{
		}
		
		void i386_edx_changed (object sender, EventArgs e)
		{
		}
		
		void i386_esi_changed (object sender, EventArgs e)
		{
		}
		
		void i386_edi_changed (object sender, EventArgs e)
		{
		}
		
		void i386_ebp_changed (object sender, EventArgs e)
		{
		}
		
		void i386_esp_changed (object sender, EventArgs e)
		{
		}
		
		void i386_ecs_changed (object sender, EventArgs e)
		{
		}

		void i386_eds_changed (object sender, EventArgs e)
		{
		}
		
		void i386_ees_changed (object sender, EventArgs e)
		{
		}

		void i386_ess_changed (object sender, EventArgs e)
		{
		}

		void i386_eip_changed (object sender, EventArgs e)
		{
		}
		
		void i386_efs_changed (object sender, EventArgs e)
		{
		}

		void i386_egs_changed (object sender, EventArgs e)
		{
		}
		
		void SetupI386 ()
		{
			i386_eax = GetEntry ("386-eax-entry", new EventHandler (i386_eax_changed));
			i386_ebx = GetEntry ("386-ebx-entry", new EventHandler (i386_ebx_changed));
			i386_ecx = GetEntry ("386-ecx-entry", new EventHandler (i386_ecx_changed));
			i386_edx = GetEntry ("386-edx-entry", new EventHandler (i386_edx_changed));
			i386_esi = GetEntry ("386-esi-entry", new EventHandler (i386_esi_changed));
			i386_edi = GetEntry ("386-edi-entry", new EventHandler (i386_edi_changed));
			i386_ebp = GetEntry ("386-ebp-entry", new EventHandler (i386_ebp_changed));
			i386_esp = GetEntry ("386-esp-entry", new EventHandler (i386_esp_changed));
			i386_ecs = GetEntry ("386-ecs-entry", new EventHandler (i386_ecs_changed));
			i386_eds = GetEntry ("386-eds-entry", new EventHandler (i386_eds_changed));
			i386_ees = GetEntry ("386-ees-entry", new EventHandler (i386_ees_changed));
			i386_ess = GetEntry ("386-ess-entry", new EventHandler (i386_ess_changed));
			i386_eip = GetEntry ("386-eip-entry", new EventHandler (i386_eip_changed));
			i386_efs = GetEntry ("386-efs-entry", new EventHandler (i386_efs_changed));
			i386_egs = GetEntry ("386-egs-entry", new EventHandler (i386_egs_changed));


			i386_cf = (Gtk.ToggleButton) gxml ["386-carry-flag"];
			i386_pf = (Gtk.ToggleButton) gxml ["386-parity-flag"];
			i386_af = (Gtk.ToggleButton) gxml ["386-auxiliary-carry-flag"];
			i386_zf = (Gtk.ToggleButton) gxml ["386-zero-flag"];
			i386_sf = (Gtk.ToggleButton) gxml ["386-sign-flag"];
			i386_tf = (Gtk.ToggleButton) gxml ["386-trap-flag"];
			i386_if = (Gtk.ToggleButton) gxml ["386-interrupt-enable-flag"];
			i386_df = (Gtk.ToggleButton) gxml ["386-direction-flag"];
			i386_of = (Gtk.ToggleButton) gxml ["386-overflow-flag"];
			i386_nt = (Gtk.ToggleButton) gxml ["386-nested-task-flag"];
			i386_rf = (Gtk.ToggleButton) gxml ["386-resume-flag"];
			i386_vm = (Gtk.ToggleButton) gxml ["386-vm-flag"];
			i386_ac = (Gtk.ToggleButton) gxml ["386-align-check-flag"];
			i386_vif = (Gtk.ToggleButton) gxml ["386-vi-flag"];
			i386_id = (Gtk.ToggleButton) gxml ["386-id-flag"];
			i386_vip = (Gtk.ToggleButton) gxml ["386-vip-flag"];

			backend.FrameChangedEvent += new StackFrameHandler (I386_FrameChangedEvent);
			backend.FramesInvalidEvent += new StackFrameInvalidHandler (I386_FramesInvalidEvent);
		}
		
		long [] last_regs, regs;

		void SetText (Gtk.Entry entry, int idx)
		{
			
			if (last_regs != null){
				if (regs [idx] != last_regs [idx]){
					entry.Text = String.Format ("{0:X8}", regs [idx]);
					entry.ModifyText (Gtk.StateType.Normal, color_change);
				} else {
					entry.ModifyText (Gtk.StateType.Normal, color_stable);
				}
			} else {
				entry.Text = String.Format ("{0:X8}", regs [idx]);
				entry.ModifyText (Gtk.StateType.Normal, color_stable);
			}
		}

		public void UpdateDisplay ()
		{
			if (!IsVisible)
				return;
			
			if (current_frame == null)
				return;
			
			try {
				regs = backend.GetRegisters (arch.AllRegisterIndices);

				SetText (i386_eax, (int) I386Register.EAX);
				SetText (i386_ebx, (int) I386Register.EBX);
				SetText (i386_ecx, (int) I386Register.ECX);
				SetText (i386_edx, (int) I386Register.EDX);
				SetText (i386_esi, (int) I386Register.ESI);
				SetText (i386_edi, (int) I386Register.EDI);
				SetText (i386_ebp, (int) I386Register.EBP);
				SetText (i386_esp, (int) I386Register.ESP);
				SetText (i386_ecs, (int) I386Register.XCS);
				SetText (i386_eds, (int) I386Register.XDS);
				SetText (i386_ees, (int) I386Register.XES);
				SetText (i386_ess, (int) I386Register.XSS);
				SetText (i386_eip, (int) I386Register.EIP);
				SetText (i386_efs, (int) I386Register.XFS);
				SetText (i386_egs, (int) I386Register.XGS);

				long f = regs [(int)I386Register.EFL];
				i386_cf.Active =  ((f & (1 << 0)) != 0);
				i386_pf.Active =  ((f & (1 << 2)) != 0);
				i386_af.Active =  ((f & (1 << 4)) != 0);
				i386_zf.Active =  ((f & (1 << 6)) != 0);
				i386_sf.Active =  ((f & (1 << 7)) != 0);
				i386_tf.Active =  ((f & (1 << 8)) != 0);
				i386_if.Active =  ((f & (1 << 9)) != 0);
				i386_df.Active =  ((f & (1 << 10)) != 0);
				i386_of.Active =  ((f & (1 << 11)) != 0);
				i386_nt.Active =  ((f & (1 << 14)) != 0);
				i386_rf.Active =  ((f & (1 << 16)) != 0);
				i386_vm.Active =  ((f & (1 << 17)) != 0);
				i386_ac.Active =  ((f & (1 << 18)) != 0);
				i386_vif.Active = ((f & (1 << 19)) != 0);
				i386_id.Active =  ((f & (1 << 21)) != 0);
				i386_vip.Active = ((f & (1 << 20)) != 0);

				last_regs = regs;
			} catch {
				Console.WriteLine ("Register loading threw an exception here");
				last_regs = null;
			}
		}
		
		void I386_FrameChangedEvent (StackFrame frame)
		{
			current_frame = frame;

			if (!backend.HasTarget)
				return;

			UpdateDisplay ();
		}

		void I386_FramesInvalidEvent ()
		{
			current_frame = null;
			notebook.Page = 0;
		}
	}
}
