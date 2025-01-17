﻿using System;

namespace Dbosoft.YaNco.Internal
{
    public class RfcServerHandle : IRfcServerHandle
    {
        internal RfcServerHandle(IntPtr ptr)
        {
            Ptr = ptr;
        }

        public IntPtr Ptr { get; private set; }

        public void Dispose()
        {
            if (Ptr == IntPtr.Zero) return;

            Interopt.RfcDestroyServer(Ptr, out _);
            Ptr = IntPtr.Zero;
        }
    }
}