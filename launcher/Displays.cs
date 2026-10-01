using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace DarkReign.Launcher
{
	public sealed record Resolution(int Width, int Height)
	{
		public override string ToString() => $"{Width} × {Height}";
	}

	public sealed class Display
	{
		// The game's Graphics.VideoDisplay: SDL's display index.
		public int Index;
		public string DeviceName;
		public string Name;
		public bool Primary;
		public Resolution Native;
		public double Scale;
		public List<Resolution> Modes = [];

		public override string ToString() => $"{Name}  ·  {Native}" + (Primary ? "  (main)" : "");
	}

	public static class Displays
	{
		// SDL 2 numbers displays as Windows enumerates them, the primary moved first
		// (WIN_InitModes makes one pass for the primary, then one for the rest).
		public static List<Display> Enumerate()
		{
			var names = FriendlyNames();
			var found = new List<Display>();
			var device = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
			for (uint i = 0; EnumDisplayDevices(null, i, ref device, 0); i++, device.cb = Marshal.SizeOf<DISPLAY_DEVICE>())
			{
				if ((device.StateFlags & DisplayDeviceAttachedToDesktop) == 0)
					continue;

				var mode = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
				if (!EnumDisplaySettings(device.DeviceName, EnumCurrentSettings, ref mode))
					continue;

				var display = new Display
				{
					DeviceName = device.DeviceName,
					Primary = (device.StateFlags & DisplayDevicePrimaryDevice) != 0,
					Native = new Resolution(mode.dmPelsWidth, mode.dmPelsHeight),
					Scale = MonitorScale(mode.dmPositionX + mode.dmPelsWidth / 2, mode.dmPositionY + mode.dmPelsHeight / 2),
					Name = names.TryGetValue(device.DeviceName, out var name) ? name : null,
				};

				var all = new HashSet<Resolution>();
				var m = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
				for (var n = 0; EnumDisplaySettings(device.DeviceName, n, ref m); n++)
					if (m.dmBitsPerPel >= 32)
						all.Add(new Resolution(m.dmPelsWidth, m.dmPelsHeight));

				all.Add(display.Native);
				display.Modes = all.OrderByDescending(r => r.Width * r.Height).ThenByDescending(r => r.Width).ToList();
				found.Add(display);
			}

			var ordered = found.Where(d => d.Primary).Concat(found.Where(d => !d.Primary)).ToList();
			for (var i = 0; i < ordered.Count; i++)
			{
				ordered[i].Index = i;
				ordered[i].Name ??= ordered.Count == 1 ? "Display" : $"Display {i + 1}";
			}

			return ordered;
		}

		static double MonitorScale(int x, int y)
		{
			try
			{
				var monitor = MonitorFromPoint(new POINT { X = x, Y = y }, MonitorDefaultToNearest);
				if (GetDpiForMonitor(monitor, MdtEffectiveDpi, out var dpiX, out _) == 0 && dpiX > 0)
					return dpiX / 96.0;
			}
			catch (Exception)
			{
				// Shcore is missing before Windows 8.1.
			}

			return 1;
		}

		// Monitor model names ("DELL U3423WE") by GDI device name (\\.\DISPLAY1), from the display configuration.
		static Dictionary<string, string> FriendlyNames()
		{
			var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			try
			{
				if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var pathCount, out var modeCount) != 0)
					return names;

				var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
				var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
				if (QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0)
					return names;

				foreach (var path in paths.Take((int)pathCount))
				{
					var source = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
					source.header.type = DisplayConfigGetSourceName;
					source.header.size = Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>();
					source.header.adapterId = path.sourceAdapterId;
					source.header.id = path.sourceId;
					if (DisplayConfigGetDeviceInfo(ref source) != 0)
						continue;

					var target = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
					target.header.type = DisplayConfigGetTargetName;
					target.header.size = Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>();
					target.header.adapterId = path.targetAdapterId;
					target.header.id = path.targetId;
					if (DisplayConfigGetDeviceInfo(ref target) != 0)
						continue;

					var name = target.monitorFriendlyDeviceName?.Trim();
					if (string.IsNullOrEmpty(name) && target.outputTechnology == OutputTechnologyInternal)
						name = "Built-in display";

					if (!string.IsNullOrEmpty(name))
						names.TryAdd(source.viewGdiDeviceName, name);
				}
			}
			catch (Exception)
			{
				// Names are cosmetic; the numbered fallback is fine.
			}

			return names;
		}

		const int EnumCurrentSettings = -1;
		const int DisplayDeviceAttachedToDesktop = 0x1;
		const int DisplayDevicePrimaryDevice = 0x4;
		const uint MonitorDefaultToNearest = 2;
		const int MdtEffectiveDpi = 0;
		const uint QdcOnlyActivePaths = 2;
		const int DisplayConfigGetSourceName = 1;
		const int DisplayConfigGetTargetName = 2;
		const uint OutputTechnologyInternal = 0x80000000;

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		struct DISPLAY_DEVICE
		{
			public int cb;
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
			public int StateFlags;
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
		}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		struct DEVMODE
		{
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
			public short dmSpecVersion;
			public short dmDriverVersion;
			public short dmSize;
			public short dmDriverExtra;
			public int dmFields;
			public int dmPositionX;
			public int dmPositionY;
			public int dmDisplayOrientation;
			public int dmDisplayFixedOutput;
			public short dmColor;
			public short dmDuplex;
			public short dmYResolution;
			public short dmTTOption;
			public short dmCollate;
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
			public short dmLogPixels;
			public int dmBitsPerPel;
			public int dmPelsWidth;
			public int dmPelsHeight;
			public int dmDisplayFlags;
			public int dmDisplayFrequency;
			public int dmICMMethod;
			public int dmICMIntent;
			public int dmMediaType;
			public int dmDitherType;
			public int dmReserved1;
			public int dmReserved2;
			public int dmPanningWidth;
			public int dmPanningHeight;
		}

		[StructLayout(LayoutKind.Sequential)]
		struct POINT { public int X, Y; }

		[StructLayout(LayoutKind.Sequential)]
		struct LUID { public uint LowPart; public int HighPart; }

		// DISPLAYCONFIG_PATH_INFO: a 20-byte source info, a 48-byte target info, then flags.
		[StructLayout(LayoutKind.Explicit, Size = 72)]
		struct DISPLAYCONFIG_PATH_INFO
		{
			[FieldOffset(0)] public LUID sourceAdapterId;
			[FieldOffset(8)] public uint sourceId;
			[FieldOffset(20)] public LUID targetAdapterId;
			[FieldOffset(28)] public uint targetId;
		}

		// Only passed through; its contents are not read.
		[StructLayout(LayoutKind.Sequential, Size = 64)]
		struct DISPLAYCONFIG_MODE_INFO { public uint infoType; }

		[StructLayout(LayoutKind.Sequential)]
		struct DISPLAYCONFIG_DEVICE_INFO_HEADER
		{
			public int type;
			public int size;
			public LUID adapterId;
			public uint id;
		}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
		{
			public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
		}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		struct DISPLAYCONFIG_TARGET_DEVICE_NAME
		{
			public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
			public uint flags;
			public uint outputTechnology;
			public ushort edidManufactureId;
			public ushort edidProductCodeId;
			public uint connectorInstance;
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
		}

		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

		[DllImport("user32.dll")]
		static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

		[DllImport("shcore.dll")]
		static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

		[DllImport("user32.dll")]
		static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

		[DllImport("user32.dll")]
		static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, [Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
			ref uint numModeInfoArrayElements, [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, IntPtr currentTopologyId);

		[DllImport("user32.dll")]
		static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket);

		[DllImport("user32.dll")]
		static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);
	}
}
