namespace MikeNakis.Wpf.D2DImage;

using Sys = System;
using Wpf = System.Windows;
using WpfMedia = System.Windows.Media;
using WpfInterop = System.Windows.Interop;
using D2d = Vortice.Direct2D1;
using D3d = Vortice.Direct3D;
using D3d9 = Vortice.Direct3D9;
using D3d11 = Vortice.Direct3D11;
using DCommon = Vortice.DCommon;
using Dxgi = Vortice.DXGI;
using static MikeNakis.Kit.GlobalStatics;
using MikeNakis.Kit;
using SysInterop = System.Runtime.InteropServices;

// PEARL: System.Windows.Media.ImageSource apparently has an internal constructor to prevent us from extending it.
//   So, we cannot implement an image source aggregating an instance of System.Windows.Interop.D3DImage; instead,
//   we have to extend the D3DImage.
//   I say "apparently" because nowhere in the documentation of ImageSource does it say that we are not allowed to
//   extend it, and the source of ImageSource code is secret.
// PEARL-ON-PEARL: When trying to extend a class with an inaccessible constructor, the C# compiler will not just tell
//   you that you cannot extend the class; instead, it will tell you that the base class does not have a constructor
//   accepting zero arguments. You try intellisense, it comes up with nothing. So you try one argument, it tells you it
//   does not have a constructor accepting one argument. You try two arguments, the same; and so on.
//   So, you are expected to GUESS that the base class secretly has no accessible constructor.
public class D2DImage : WpfInterop.D3DImage, Sys.IDisposable
{
	public sealed class NotInitializedException : Sys.Exception;
	public sealed class AlreadyInitializedException : Sys.Exception;

	readonly LifeGuard lifeGuard;
	readonly Dxgi.IDXGIFactory6 dxgiFactory = Dxgi.DXGI.CreateDXGIFactory2<Dxgi.IDXGIFactory6>( debug: DebugMode );
	readonly D2d.ID2D1Factory8 d2dFactory = D2d.D2D1.D2D1CreateFactory<D2d.ID2D1Factory8>( D2d.FactoryType.SingleThreaded, D2d.DebugLevel.Information );
	readonly D3d9.IDirect3D9Ex d3d9 = D3d9.D3D9.Direct3DCreate9Ex();
	readonly ushort adapterIndex;
	readonly D3d11.ID3D11Device d3d11Device;

	D3d9.IDirect3DDevice9Ex? d3d9Device;

	Wpf.FrameworkElement? parent;
	D3d11.ID3D11Texture2D? d3d11Texture2d;
	D3d9.IDirect3DTexture9? d3d9Texture;
	D3d9.IDirect3DSurface9? d3d9Surface;
	D2d.ID2D1RenderTarget? d2dRenderTarget;

	public nint D2DRenderTarget
	{
		get
		{
			Assert( isInitializedAssertion() );
			return d2dRenderTarget!.NativePointer;
		}
	}

	public D2DImage()
		: this( false )
	{
	}

	public D2DImage( bool collectStackTrace )
	{
		lifeGuard = LifeGuard.Create( collectStackTrace );
		D3d11.DeviceCreationFlags creationFlags = D3d11.DeviceCreationFlags.BgraSupport | (DebugMode ? D3d11.DeviceCreationFlags.Debug : 0);

		Log.Debug( $"Adapters according to DXGI:" );
		Log.Debug( $"    {"Index",-5} {"Description",-44} {"DeviceName",-15} {"Driver",-15} {"Width",6} {"Height",6} {"RefreshRate",11} {"Format",-8} {"DedicatedSystemMemory",-21} {"DedicatedVideoMemory",-20} {"SharedSystemMemory",-20} " );
		for( ushort adapterIndex = 0; dxgiFactory.EnumAdapterByGpuPreference( adapterIndex, Dxgi.GpuPreference.Unspecified, out Dxgi.IDXGIAdapter4? adapter ).Success; adapterIndex++ )
		{
			Assert( adapter != null );
			bool isReal = adapter.Description.DedicatedVideoMemory != 0;
			D3d9.AdapterIdentifier identifier = isReal ? getAdapterIdentifier( d3d9, adapterIndex ) : default;
			D3d9.DisplayMode displayMode = isReal ? getAdapterDisplayMode( d3d9, adapterIndex ) : default;
			Log.Debug( $"    {adapterIndex,-5} {adapter.Description.Description,-44} {identifier.DeviceName,-15} {identifier.Driver,-15} {displayMode.Width,6} {displayMode.Height,6} {displayMode.RefreshRate,11} {displayMode.Format,-8} {adapter.Description.DedicatedSystemMemory,-21} {adapter.Description.DedicatedVideoMemory,-20} {adapter.Description.SharedSystemMemory,-20}" );
			adapter.Release();
		}

		Log.Debug( $"Adapters according to D9D9:" );
		Log.Debug( $"    {"Index",-5} {"Description",-44} {"DeviceName",-15} {"Driver",-15} {"Width",6} {"Height",6} {"RefreshRate",11} {"Format",-8}" );
		uint adapterCount = d3d9.AdapterCount;
		for( ushort adapterIndex = 0; adapterIndex < adapterCount; adapterIndex++ )
		{
			D3d9.AdapterIdentifier identifier = d3d9.GetAdapterIdentifier( adapterIndex );
			D3d9.DisplayMode displayMode = d3d9.GetAdapterDisplayMode( adapterIndex );
			nint monitorHandle = d3d9.GetAdapterMonitor( adapterIndex );
			MONITORINFOEX monitorInfoEx = MONITORINFOEX.Create();
			bool ok = GetMonitorInfo( monitorHandle, ref monitorInfoEx );
			Assert( ok );
			Log.Debug( $"    {adapterIndex,-5} {identifier.Description,-44} {identifier.DeviceName,-15} {identifier.Driver,-15} {displayMode.Width,6} {displayMode.Height,6} {displayMode.RefreshRate,11} {displayMode.Format,-8}" );
		}

		Dxgi.IDXGIAdapter bestAdapter = findBestAdapter( dxgiFactory, d3d9, Dxgi.GpuPreference.HighPerformance );
		adapterIndex = getAdapterIndex( dxgiFactory, bestAdapter );
		Log.Debug( $"Best adapter: {bestAdapter.Description.Description}, index {adapterIndex}" );
		D3d11.D3D11.D3D11CreateDevice( bestAdapter, D3d.DriverType.Unknown, //when specifying the adapter explicitly, this must NOT be D3d.DriverType.Hardware.
			creationFlags, featureLevels: null!, //PEARL: Vortice declares the featureLevels argument as non-nullable, but internally it does handle it as nullable.
			out d3d11Device, out D3d11.ID3D11DeviceContext _ ).CheckError();
		bestAdapter.Release();
		IsFrontBufferAvailableChanged += onFrontBufferAvailableChanged;
	}

	// PEARL: Vortice offers only one version of D3D9.GetAdapterIdentifier() which will throw if the native function
	// returns an errorcode; however, the native function may return an error code under very normal circumstances,
	// such as when the adapter is a software adapter. 
	static D3d9.AdapterIdentifier getAdapterIdentifier( D3d9.IDirect3D9Ex d3d9, ushort adapterIndex )
	{
		try
		{
			return d3d9.GetAdapterIdentifier( adapterIndex );
		}
		catch( Sys.Exception )
		{
			return default;
		}
	}

	// PEARL: Vortice offers only one version of D3D9.GetAdapterDisplayMode() which will throw if the native function
	// returns an errorcode; however, the native function may return an error code under very normal circumstances,
	// such as when the adapter is a software adapter.
	static D3d9.DisplayMode getAdapterDisplayMode( D3d9.IDirect3D9Ex d3d9, ushort adapterIndex )
	{
		try
		{
			return d3d9.GetAdapterDisplayMode( adapterIndex );
		}
		catch( Sys.Exception )
		{
			return default;
		}
	}

	public void Initialize( Wpf.FrameworkElement parent )
	{
		Assert( lifeGuard.IsAliveAssertion() );
		Assert( this.parent == null, () => throw new AlreadyInitializedException() );
		this.parent = parent;
		parent.SizeChanged += onParentSizeChanged;

		//PEARL: the documentation says "Creating a device requires an HWND. In general, you create a dummy HWND for
		//    Direct3D9 to use." They don't say a word about any non-general cases where you might want to supply a
		//    non-dummy HWND.
		//    (See "Microsoft Learn: WPF and Direct3D9 Interoperation", https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/wpf-and-direct3d9-interoperation)
		//    Then they show how to create such a dummy HWND, and they do it by registering a dummy window class and
		//    creating a dummy invisible window out of it.
		//    However, using the desktop window works just as fine, which begs the question:
		//    why THE FUCK do they require a HWND in the first place?
		nint deviceWindowHandle;
		if( False )
			deviceWindowHandle = GetDesktopWindow();
		else
			deviceWindowHandle = new WpfInterop.WindowInteropHelper( Wpf.Window.GetWindow( parent ) ).Handle;
		Assert( deviceWindowHandle != 0 );
		d3d9Device = createD3d9Device( d3d9, adapterIndex, deviceWindowHandle );

		allocateState();
	}

	public void Dispose()
	{
		lifeGuard.Dispose();
		if( parent != null )
		{
			parent.SizeChanged -= onParentSizeChanged;
			freeState();
		}
		d3d9Device?.Dispose();
		d3d9.Dispose();
		d2dFactory.Dispose();
		d3d11Device.Dispose();
		dxgiFactory.Dispose();
	}

	void onParentSizeChanged( object sender, Wpf.SizeChangedEventArgs e )
	{
		Assert( isInitializedAssertion() );
		freeState();
		allocateState();
	}

	void allocateState()
	{
		(uint deviceWidth, uint deviceHeight) = getDeviceSize( parent! );
		d3d11Texture2d = createSharedD3d11Texture2d( d3d11Device, deviceWidth, deviceHeight );
		nint sharedHandle = getSharedHandle( d3d11Texture2d );
		Assert( sharedHandle != 0 );
		d3d9Texture = createSharedD3d9Texture( d3d9Device!, deviceWidth, deviceHeight, sharedHandle );
		d3d9Surface = d3d9Texture.GetSurfaceLevel( 0 );
		base.Lock();
		base.SetBackBuffer( WpfInterop.D3DResourceType.IDirect3DSurface9, d3d9Surface.NativePointer );
		base.Unlock();
		d2dRenderTarget = createD2dRenderTarget( d2dFactory, d3d11Texture2d );
	}

	void freeState()
	{
		d2dRenderTarget?.Dispose();
		d3d9Surface?.Dispose();
		d3d9Texture?.Dispose();
		d3d11Texture2d?.Dispose();
	}

	void onFrontBufferAvailableChanged( object sender, Wpf.DependencyPropertyChangedEventArgs e )
	{
		if( IsFrontBufferAvailable )
		{
			base.Lock();
			base.SetBackBuffer( WpfInterop.D3DResourceType.IDirect3DSurface9, d3d9Surface!.NativePointer );
			base.Unlock();
		}
	}

	bool isInitializedAssertion()
	{
		Assert( lifeGuard.IsAliveAssertion() );
		Assert( d2dRenderTarget != null, () => throw new NotInitializedException() );
		return true;
	}

	public new void Lock()
	{
		Assert( isInitializedAssertion() );
		//Log.Debug( $"PixelWidth x PixelHeight = {PixelWidth} x {PixelHeight}" );
		base.Lock();
	}

	public new void Unlock()
	{
		Assert( isInitializedAssertion() );
		d3d11Device.ImmediateContext.Flush();
		base.Unlock();
	}

	public new void SetBackBuffer( WpfInterop.D3DResourceType backBufferType, nint backBuffer ) => throw new Sys.NotImplementedException();

	public new void SetBackBuffer( WpfInterop.D3DResourceType backBufferType, nint backBuffer, bool enableSoftwareFallback ) => throw new Sys.NotImplementedException();

	static D3d9.IDirect3DTexture9 createSharedD3d9Texture( D3d9.IDirect3DDevice9Ex d3d9Device, uint deviceWidth, uint deviceHeight, nint sharedHandle )
	{
		return d3d9Device.CreateTexture( deviceWidth, deviceHeight, 1, D3d9.Usage.RenderTarget, D3d9.Format.A8R8G8B8, D3d9.Pool.Default, ref sharedHandle );
	}

	static nint getSharedHandle( D3d11.ID3D11Texture2D d3d11Texture2d )
	{
		Dxgi.IDXGIResource1 dxgiResource = d3d11Texture2d.QueryInterface<Dxgi.IDXGIResource1>();
		nint sharedHandle = dxgiResource.SharedHandle;
		dxgiResource.Release();
		return sharedHandle;
	}

	static (uint deviceWidth, uint deviceHeight) getDeviceSize( Wpf.FrameworkElement frameworkElement )
	{
		Wpf.DpiScale dpiScale = WpfMedia.VisualTreeHelper.GetDpi( frameworkElement );
		uint deviceWidth = (uint)(frameworkElement.ActualWidth * dpiScale.DpiScaleX);
		uint deviceHeight = (uint)(frameworkElement.ActualHeight * dpiScale.DpiScaleY);
		return (deviceWidth, deviceHeight);
	}

	static D2d.ID2D1RenderTarget createD2dRenderTarget( D2d.ID2D1Factory8 d2dFactory, D3d11.ID3D11Texture2D d3d11Texture2d )
	{
		Dxgi.IDXGISurface dxgiSurface = d3d11Texture2d.QueryInterface<Dxgi.IDXGISurface>();
		D2d.RenderTargetProperties renderTargetProperties = new()
		{
			Type = D2d.RenderTargetType.Hardware,
			PixelFormat = new( d3d11Texture2d.Description.Format, DCommon.AlphaMode.Premultiplied ),
			DpiX = 96.0f,
			DpiY = 96.0f,
			Usage = D2d.RenderTargetUsage.None,
			MinLevel = D2d.FeatureLevel.Default
		};
		D2d.ID2D1RenderTarget result = d2dFactory.CreateDxgiSurfaceRenderTarget( dxgiSurface, renderTargetProperties );
		dxgiSurface.Release();
		return result;
	}

	static D3d11.ID3D11Texture2D createSharedD3d11Texture2d( D3d11.ID3D11Device d3d11Device, uint deviceWidth, uint deviceHeight )
	{
		D3d11.Texture2DDescription textureDescription = new()
		{
			ArraySize = 1,
			BindFlags = D3d11.BindFlags.RenderTarget,
			CPUAccessFlags = D3d11.CpuAccessFlags.None,
			Format = Dxgi.Format.B8G8R8A8_UNorm,
			MipLevels = 1,
			MiscFlags = D3d11.ResourceOptionFlags.Shared,
			SampleDescription = Dxgi.SampleDescription.Default,
			Usage = D3d11.ResourceUsage.Default,
			Width = deviceWidth,
			Height = deviceHeight
		};
		D3d11.ID3D11Texture2D d3d11Texture2d = d3d11Device.CreateTexture2D( textureDescription );
		Assert( d3d11Texture2d.QueryInterface<Dxgi.IDXGIResource>().SharedHandle != 0 );
		return d3d11Texture2d;
	}

	[SysInterop.DllImport( "user32.dll", SetLastError = false ), SysInterop.DefaultDllImportSearchPaths( SysInterop.DllImportSearchPath.System32 )] static extern nint GetDesktopWindow();

	static D3d9.IDirect3DDevice9Ex createD3d9Device( D3d9.IDirect3D9Ex d3d9, ushort adapterIndex, nint deviceWindowHandle )
	{
		//D3d9.DisplayMode displayMode = d3d9.GetAdapterDisplayMode( adapterIndex );
		D3d9.PresentParameters presentParameters = new()
		{
			Windowed = true,
			SwapEffect = D3d9.SwapEffect.Discard,
			DeviceWindowHandle = deviceWindowHandle,
			PresentationInterval = D3d9.PresentInterval.Immediate,
		};
		//NOTE:
		// The HardwareVertexProcessing seems to be necessary no matter what.
		// The Multithreaded flag must be added according to rumours, because WPF does multi-threading, even if we don't.
		// The FpuPreserve flag should be added, also according to rumours.
		D3d9.CreateFlags creationFlags = D3d9.CreateFlags.HardwareVertexProcessing | D3d9.CreateFlags.Multithreaded | D3d9.CreateFlags.FpuPreserve;
		return d3d9.CreateDeviceEx( adapterIndex, D3d9.DeviceType.Hardware, deviceWindowHandle, creationFlags, presentParameters );
	}

	static Dxgi.IDXGIAdapter findBestAdapter( Dxgi.IDXGIFactory6 dxgiFactory, D3d9.IDirect3D9Ex d3d9, Dxgi.GpuPreference gpuPreference )
	{
		for( ushort bestAdapterIndex = 0; dxgiFactory.EnumAdapterByGpuPreference( bestAdapterIndex, gpuPreference, out Dxgi.IDXGIAdapter4? adapter ).Success; bestAdapterIndex++ )
		{
			Assert( adapter != null );
			ushort adapterIndex = getAdapterIndex( dxgiFactory, adapter );

			if( !d3d9.CheckDeviceType( adapterIndex, D3d9.DeviceType.Hardware, D3d9.Format.X8R8G8B8, D3d9.Format.X8R8G8B8, windowed: true ).Success )
			{
				Log.Debug( "Adapter \"" + adapter.Description.Description + "\" does not support the required format, skipping it." );
			}
			else
			{
				try
				{
					d3d9.GetAdapterIdentifier( adapterIndex );
					return adapter;
				}
				catch( Sys.Exception )
				{
					Log.Debug( "Adapter \"" + adapter.Description.Description + "\" is erroring, skipping it." );
				}
			}
			adapter.Release();
		}
		throw new AssertionFailureException();
	}

	static ushort getAdapterIndex( Dxgi.IDXGIFactory dxgiFactory, Dxgi.IDXGIAdapter adapterToFind )
	{
		for( ushort adapterIndex = 0; dxgiFactory.EnumAdapters( adapterIndex, out Dxgi.IDXGIAdapter adapter ).Success; adapterIndex++ )
		{
			bool found = adapter.Description.Description == adapterToFind.Description.Description;
			adapter.Release();
			if( found )
				return adapterIndex;
		}
		throw new AssertionFailureException();
	}

	/// <summary>The GetMonitorInfo function retrieves information about a display monitor.</summary>
	/// <param name="hMonitor">A handle to the display monitor of interest.</param>
	/// <param name="monitorInfoEx"><see cref="MONITORINFOEX"/> structure that receives information about the specified
	/// display monitor. You must set the <b>cbSize</b> member of the structure to sizeof(MONITORINFOEX) before
	/// calling the <b>GetMonitorInfo</b> function. Doing so lets the function determine the type of structure you are
	/// passing to it.</param>
	/// <returns>If the function succeeds, the return value is nonzero. If the function fails, the return value is zero.</returns>
	[SysInterop.DllImport( "USER32.dll", ExactSpelling = true, EntryPoint = "GetMonitorInfoW" )]
	[SysInterop.DefaultDllImportSearchPaths( SysInterop.DllImportSearchPath.System32 )]
	static extern bool GetMonitorInfo( nint hMonitor, ref MONITORINFOEX monitorInfoEx );
}
