namespace MikeNakis.Wpf.D2DImage;

using SysInterop = System.Runtime.InteropServices;

#pragma warning disable IDE1006 // Naming Styles
#pragma warning disable CA1021 // Avoid out parameters
#pragma warning disable CA1069 // Enums values should not be duplicated
#pragma warning disable CA1401 // P/Invokes should not be visible

// from https://pinvoke.net/default.aspx/user32/MONITORINFOEX.html

/// <summary>
/// The MONITORINFOEX structure contains information about a display monitor.
/// The MONITORINFOEX structure is a superset of the MONITORINFO structure, adding a string member to contain a name
/// for the display monitor.
/// </summary>
[SysInterop.StructLayout( SysInterop.LayoutKind.Sequential, CharSet = SysInterop.CharSet.Auto )]
public struct MONITORINFOEX
{
	/// <summary>
	/// The size, in bytes, of the structure. Set this member to sizeof(MONITORINFOEX) before calling the GetMonitorInfo function.
	/// Doing so lets the function determine the type of structure you are passing to it.
	/// </summary>
	public int Size;

	/// <summary>
	/// A RECT structure that specifies the display monitor rectangle, expressed in virtual-screen coordinates.
	/// Note that if the monitor is not the primary display monitor, some of the rectangle's coordinates may be negative values.
	/// </summary>
	public RECT Monitor;

	/// <summary>
	/// A RECT structure that specifies the work area rectangle of the display monitor that can be used by applications,
	/// expressed in virtual-screen coordinates. Windows uses this rectangle to maximize an application on the monitor.
	/// The rest of the area in rcMonitor contains system windows such as the task bar and side bars.
	/// Note that if the monitor is not the primary display monitor, some of the rectangle's coordinates may be negative values.
	/// </summary>
	public RECT WorkArea;

	/// <summary>
	/// The attributes of the display monitor.
	/// This member can be the following value:
	///   1 : MONITORINFOF_PRIMARY
	/// </summary>
	public uint Flags;

	/// <summary>
	/// A string that specifies the device name of the monitor being used. Most applications have no use for a display monitor name,
	/// and so can save some bytes by using a MONITORINFO structure.
	/// </summary>
	[SysInterop.MarshalAs( SysInterop.UnmanagedType.ByValTStr, SizeConst = 32 )]
	public string DeviceName;

	public static MONITORINFOEX Create()
	{
		MONITORINFOEX monitorInfoEx = new();
		monitorInfoEx.Size = SysInterop.Marshal.SizeOf<MONITORINFOEX>();
		return monitorInfoEx;
	}
}
