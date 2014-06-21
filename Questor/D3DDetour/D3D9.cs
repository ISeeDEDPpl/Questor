using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Collections.Generic;
using EasyHook;

namespace D3DDetour
{
	
	public class D3D9 : D3DHook
	{
		[DllImport("d3d9.dll")]
		static extern IntPtr Direct3DCreate9(uint uint_0);
		
		private delegate int Delegate2(IntPtr intptr_0);
		private delegate void Delegate3(IntPtr intptr_0);
		private delegate int Delegate4(IntPtr instance, uint adapter, uint deviceType, IntPtr focusWindow, uint behaviorFlags, [In] ref D3D9.Struct0 presentationParameters, out IntPtr returnedDeviceInterface);
		private struct Struct0
		{
			#pragma warning disable
			public readonly uint uint_0;
			public readonly uint uint_1;
			public uint uint_2;
			public readonly uint uint_3;
			public readonly uint uint_4;
			public readonly uint uint_5;
			public uint uint_6;
			public readonly IntPtr intptr_0;
			[MarshalAs(UnmanagedType.Bool)]
			public bool bool_0;
			[MarshalAs(UnmanagedType.Bool)]
			public readonly bool bool_1;
			public readonly uint uint_7;
			public readonly uint uint_8;
			public readonly uint uint_9;
			public readonly uint uint_10;
			#pragma warning restore
		}
		private D3D9.Delegate2 delegate2_0;
		private LocalHook localHook_0;
		public IntPtr EndScenePointer = IntPtr.Zero;
		public IntPtr ResetPointer = IntPtr.Zero;
		public IntPtr ResetExPointer = IntPtr.Zero;
		public override void Initialize()
		{
			Form form = new Form();
			IntPtr intPtr = Direct3DCreate9(32u);
			if (intPtr == IntPtr.Zero)
			{
				throw new Exception("Failed to create D3D.");
			}
			D3D9.Struct0 @struct = new D3D9.Struct0
			{
				bool_0 = true,
				uint_6 = 1u,
				uint_2 = 0u
			};
			D3D9.Delegate4 @delegate = (D3D9.Delegate4)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(intPtr), 64), typeof(D3D9.Delegate4));
			IntPtr intPtr2;
			if (@delegate(intPtr, 0u, 1u, form.Handle, 32u, ref @struct, out intPtr2) < 0)
			{
				throw new Exception("Failed to create device.");
			}
			this.EndScenePointer = Marshal.ReadIntPtr(Marshal.ReadIntPtr(intPtr2), 168);
			D3D9.Delegate3 delegate2 = (D3D9.Delegate3)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(intPtr2), 8), typeof(D3D9.Delegate3));
			D3D9.Delegate3 delegate3 = (D3D9.Delegate3)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(intPtr), 8), typeof(D3D9.Delegate3));
			delegate2(intPtr2);
			delegate3(intPtr);
			form.Dispose();
			this.delegate2_0 = (D3D9.Delegate2)Marshal.GetDelegateForFunctionPointer(this.EndScenePointer, typeof(D3D9.Delegate2));
			this.localHook_0 = LocalHook.Create(this.EndScenePointer, new D3D9.Delegate2(this.method_0), this);
			
			int[] exclusiveACL = new int[1];
			localHook_0.ThreadACL.SetExclusiveACL(exclusiveACL);
		}
		private int method_0(IntPtr intptr_0)
		{
			base.RaiseEvent();
			return this.delegate2_0(intptr_0);
		}
		public override void Remove()
		{
			this.localHook_0.Dispose();
		}
	}
}
