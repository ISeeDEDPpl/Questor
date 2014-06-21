using System;
using System.Runtime.InteropServices;


namespace D3DDetour
{
	public abstract class D3DHook
	{
		protected static readonly object _frameLock = new object();
		public static event EventHandler<EventArgs> OnFrame;

		public delegate void OnFrameDelegate();
		public static event OnFrameDelegate OnFrameOnce;

		public abstract void Initialize();
		public abstract void Remove();

		protected void RaiseEvent()
		{
			lock (_frameLock)
			{
				if (OnFrame != null)
					OnFrame(null, new EventArgs());

				if (OnFrameOnce != null)
				{
					OnFrameOnce();
					OnFrameOnce = null;
				}
			}
		}
	}

	public enum D3DVersion
	{
		Direct3D9,
		Direct3D11,
	}

}
