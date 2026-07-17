using System;
using System.Runtime.InteropServices;

namespace CrossDeckHost.ProfileStore;

/// <summary>
/// <see cref="System.Drawing.Icon.ExtractAssociatedIcon"/> only ever returns the small
/// (32x32) shell-association icon — every extracted app icon looked blurry once scaled up
/// to our 144x144 tile size. This pulls the real 256x256 "jumbo" icon from the same system
/// image list Explorer's large-icon view uses, so extraction gets genuine detail instead of
/// upscaled mush.
/// </summary>
internal static class JumboIcon
{
    private const int SHIL_JUMBO = 0x4; // 256x256
    private const uint SHGFI_SYSICONINDEX = 0x4000;
    private const uint SHGFI_ICON = 0x100;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [ComImport]
    [Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageList
    {
        int Add(IntPtr hbmImage, IntPtr hbmMask, ref int pi);
        int ReplaceIcon(int i, IntPtr hicon, ref int pi);
        int SetOverlayImage(int iImage, int iOverlay);
        int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);
        int AddMasked(IntPtr hbmImage, int crMask, ref int pi);
        int Draw(IntPtr pimldp);
        int Remove(int i);
        int GetIcon(int i, int flags, ref IntPtr picon);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("shell32.dll", EntryPoint = "#727")]
    private static extern int SHGetImageList(int iImageList, ref Guid riid, out IImageList ppv);

    /// <summary>Returns a 256x256 icon for the given file, or null if the jumbo list lookup fails.</summary>
    public static System.Drawing.Icon? ExtractJumbo(string path)
    {
        var shfi = new SHFILEINFO();
        int sysIconIndexResult = SHGetFileInfo(path, 0, ref shfi, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_SYSICONINDEX | SHGFI_ICON);
        if (sysIconIndexResult == 0) return null;
        if (shfi.hIcon != IntPtr.Zero) NativeMethods.DestroyIcon(shfi.hIcon); // SHGFI_SYSICONINDEX still hands back a small hIcon we don't need

        var iidImageList = typeof(IImageList).GUID;
        if (SHGetImageList(SHIL_JUMBO, ref iidImageList, out var imageList) != 0) return null;

        IntPtr hJumboIcon = IntPtr.Zero;
        const int ILD_TRANSPARENT = 1;
        if (imageList.GetIcon(shfi.iIcon, ILD_TRANSPARENT, ref hJumboIcon) != 0 || hJumboIcon == IntPtr.Zero)
            return null;

        try
        {
            // FromHandle wraps the native HICON; the caller disposing the Icon does NOT free the
            // handle, so we still need an explicit DestroyIcon once we're done copying its bits.
            using var wrapped = System.Drawing.Icon.FromHandle(hJumboIcon);
            return (System.Drawing.Icon)wrapped.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(hJumboIcon);
        }
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);
    }
}
