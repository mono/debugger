using System;

namespace Mono.Debugger
{
	public delegate void AddressDomainInvalidHandler (AddressDomain domain);

	// <summary>
	//   This is the "domain" of a TargetAddress - you cannot compare addresses from
	//   different domains.  See TargetAddress.cs for details.
	//
	//   The debugger has the following address domains:
	//
	//   * the global address domain
	//
	//     This is used to do symbol lookups and to store addresses which are independent
	//     from the execution of a particular thread.
	//
	//   * the local address domain
	//
	//     Each thread has its own local address domain.  Technically, all threads share
	//     the same memory space, but this is to enforce the policy that you may not share
	//     any information about variables across thread boundaries.
	//
	//   * the frame address domain
	//
	//     Each stack frame has its own address domain (which is only constructed when it's
	//     actually used) which has the same lifetime than its corresponding frame.
	//
	//     This is normally used when reading an address from the stack or from a register,
	//     for instance the address of a variable which is stored on the stack.  Since the
	//     object such an address is pointing to becomes invalid when the target leaves the
	//     stack frame, the address will also become invalid.
	//
	// </summary>
	// <remarks>
	//   This is intentionally a class and not a struct - we must always pass this by
	//   reference and not by value.
	// </remarks>
	public sealed class AddressDomain : IDisposable
	{
		// <summary>
		//   `name' is just used in the error messages.
		// </summary>
		public AddressDomain (string name)
		{
			this.id = ++next_id;
			this.is_valid = true;
			this.name = name;
		}

		int id;
		bool is_valid;
		string name;

		static int next_id = 0;

		public int ID {
			get { return id; }
		}

		public string Name {
			get { return name; }
		}

		public bool IsValid {
			get { return is_valid; }
		}

		public event AddressDomainInvalidHandler InvalidEvent;

		public override string ToString ()
		{
			return String.Format ("AddressDomain ({0}:{1})", ID, Name);
		}

		//
		// IDisposable
		//

		private bool disposed = false;

		private void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("AddressDomain");
		}

		protected virtual void Dispose (bool disposing)
		{
			if (!this.disposed) {
				if (disposing) {
					if (InvalidEvent != null)
						InvalidEvent (this);

					is_valid = false;
				}

				this.disposed = true;
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~AddressDomain ()
		{
			Dispose (false);
		}
	}
}
