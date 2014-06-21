using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using EasyHook;


namespace D3DDetour
{
public class D3D11 : D3DHook
	{
		public struct SwapChainDescription
		{
			public D3D11.ModeDescription ModeDescription;
			public D3D11.SampleDescription SampleDescription;
			public int Usage;
			public int BufferCount;
			public IntPtr OutputHandle;
			[MarshalAs(UnmanagedType.Bool)]
			public bool IsWindowed;
			public int SwapEffect;
			public int Flags;
		}
		public struct Rational
		{
			public int Numerator;
			public int Denominator;
		}
		public struct ModeDescription
		{
			public int Width;
			public int Height;
			public D3D11.Rational RefreshRate;
			public int Format;
			public int ScanlineOrdering;
			public int Scaling;
		}
		public struct SampleDescription
		{
			public int Count;
			public int Quality;
		}
		private delegate void Delegate0(IntPtr intptr_0);
		private delegate int Delegate1(int int_0, int int_1, int int_2);
		private D3D11.Delegate1 delegate1_0;
		private LocalHook localHook_0;
		public IntPtr EndScenePointer = IntPtr.Zero;
		public IntPtr ResetPointer = IntPtr.Zero;
		public IntPtr ResetExPointer = IntPtr.Zero;
		private IntPtr intptr_0;
		private IntPtr intptr_1;
		private IntPtr intptr_2;
		public unsafe override void Initialize()
		{
			Form form = new Form();
			D3D11.SwapChainDescription swapChainDescription = new D3D11.SwapChainDescription
			{
				BufferCount = 1,
				ModeDescription = new D3D11.ModeDescription
				{
					Format = 28
				},
				Usage = 32,
				OutputHandle = form.Handle,
				SampleDescription = new D3D11.SampleDescription
				{
					Count = 1
				},
				IsWindowed = true
			};
			IntPtr zero = IntPtr.Zero;
			IntPtr zero2 = IntPtr.Zero;
			IntPtr zero3 = IntPtr.Zero;
		D3D11CreateDeviceAndSwapChain((void*)IntPtr.Zero, 1, (void*)IntPtr.Zero, 0, (void*)IntPtr.Zero, 0, 7, (void*)(&swapChainDescription), (void*)(&zero), (void*)(&zero2), (void*)IntPtr.Zero, (void*)(&zero3));
			this.intptr_0 = zero;
			this.intptr_1 = zero2;
			this.intptr_2 = zero3;
			IntPtr intPtr = Marshal.ReadIntPtr(Marshal.ReadIntPtr(this.intptr_0), 32);
			D3D11.Delegate0 @delegate = (D3D11.Delegate0)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(this.intptr_0), 8), typeof(D3D11.Delegate0));
			D3D11.Delegate0 delegate2 = (D3D11.Delegate0)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(this.intptr_1), 8), typeof(D3D11.Delegate0));
			D3D11.Delegate0 delegate3 = (D3D11.Delegate0)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(this.intptr_2), 8), typeof(D3D11.Delegate0));
			@delegate(this.intptr_0);
			delegate2(this.intptr_1);
			delegate3(this.intptr_2);
			this.delegate1_0 = (D3D11.Delegate1)Marshal.GetDelegateForFunctionPointer(intPtr, typeof(D3D11.Delegate1));
			this.localHook_0 = LocalHook.Create(intPtr, new D3D11.Delegate1(this.method_0), this);
			
			int[] exclusiveACL = new int[1];
			localHook_0.ThreadACL.SetExclusiveACL(exclusiveACL);
		}
		private int method_0(int int_0, int int_1, int int_2)
		{
			base.RaiseEvent();
			return this.delegate1_0(int_0, int_1, int_2);
		}
		public override void Remove()
		{
			this.localHook_0.Dispose();
		}
		[DllImport("d3d11.dll")]
		static extern unsafe int D3D11CreateDeviceAndSwapChain(void* pVoid_0, int int_0, void* pVoid_1, int int_1, void* pVoid_2, int int_2, int int_3, void* pVoid_3, void* pVoid_4, void* pVoid_5, void* pVoid_6, void* pVoid_7);
		[DllImport("kernel32.dll", CharSet = CharSet.Auto)]
		public static extern IntPtr GetModuleHandle(string lpModuleName);
		[DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
		public static extern IntPtr LoadLibraryA(IntPtr lpModuleName);
		public static void LoadLibrary(string libraryName)
		{
			if (D3D11.GetModuleHandle(libraryName) == IntPtr.Zero)
			{
				IntPtr intPtr = Marshal.StringToHGlobalAnsi(libraryName);
				try
				{
					D3D11.LoadLibraryA(intPtr);
				}
				finally
				{
					Marshal.FreeHGlobal(intPtr);
				}
			}
		}
	}
}
