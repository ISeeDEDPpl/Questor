using System;
using EasyHook;

namespace D3DDetour
{
    public static class Pulse
    {
    	//private static LocalHook _hook;
        //public static Magic Magic = new Magic();
       	public static D3DHook Hook = null;

        public static void Initialize(D3DVersion ver)
        {
            switch (ver)
            {
                case D3DVersion.Direct3D9:
                    Hook = new D3D9();
                    break;
#if FALSE
                case D3DVersion.Direct3D11:
                    Hook = new D3D11();
                    break;
#endif
            }

           // if (Hook == null)
           //     throw new Exception("Hook = null!");

            Hook.Initialize();
        }

        public static void Shutdown()
        {
            Hook.Remove();
        }
    }
}
