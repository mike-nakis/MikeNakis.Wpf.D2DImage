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
using MikeNakis.Kit.Extensions;
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
	readonly Dxgi.IDXGIFactory7 dxgiFactory = Dxgi.DXGI.CreateDXGIFactory2<Dxgi.IDXGIFactory7>( debug: DebugMode );
	readonly D2d.ID2D1Factory8 d2dFactory = D2d.D2D1.D2D1CreateFactory<D2d.ID2D1Factory8>( D2d.FactoryType.SingleThreaded, D2d.DebugLevel.Information );
	readonly D3d9.IDirect3D9Ex d3d9 = D3d9.D3D9.Direct3DCreate9Ex();
	readonly ushort adapterIndex;
	readonly D3d11.ID3D11Device d3d11Device;
	readonly D3d9.IDirect3DDevice9Ex d3d9Device;

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
		Dxgi.IDXGIAdapter adapter = findBestAdapter( dxgiFactory, Dxgi.GpuPreference.HighPerformance );
		D3d11.D3D11.D3D11CreateDevice( adapter, D3d.DriverType.Unknown, //when specifying the adapter explicitly, this must not be D3d.DriverType.Hardware
			creationFlags, featureLevels: null!, //PEARL: Vortice declares the featureLevels argument as non-nullable, but internally it does handle it as nullable.
			out d3d11Device, out D3d11.ID3D11DeviceContext _ ).CheckError();
		adapter.Dispose();
		adapterIndex = getAdapterIndex( dxgiFactory, d3d11Device );
		d3d9Device = createD3d9Device( d3d9, adapterIndex );
		IsFrontBufferAvailableChanged += onFrontBufferAvailableChanged;
	}

	public void Initialize( Wpf.FrameworkElement parent )
	{
		Assert( lifeGuard.IsAliveAssertion() );
		Assert( this.parent == null, () => throw new AlreadyInitializedException() );
		this.parent = parent;
		//Wpf.Window window = Wpf.Window.GetWindow( parent );
		//nint deviceWindowHandle = new WpfInterop.WindowInteropHelper( window ).Handle;
		parent.SizeChanged += onParentSizeChanged;
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
		Assert( parent != null );
		freeState();
		allocateState();
	}

	void allocateState()
	{
		(uint deviceWidth, uint deviceHeight) = getDeviceSize( parent! );
		d3d11Texture2d = createSharedD3d11Texture2d( d3d11Device, deviceWidth, deviceHeight );
		d3d9Texture = createSharedD3d9Texture( d3d9Device!, d3d11Texture2d );
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

	static D3d9.IDirect3DTexture9 createSharedD3d9Texture( D3d9.IDirect3DDevice9Ex d3d9Device, D3d11.ID3D11Texture2D d3d11Texture2d )
	{
		nint sharedHandle = getSharedHandle( d3d11Texture2d );
		Assert( sharedHandle != 0 );
		D3d11.Texture2DDescription textureDescription = d3d11Texture2d.Description;
		uint width = textureDescription.Width;
		uint height = textureDescription.Height;
		Assert( textureDescription.Format == Dxgi.Format.B8G8R8A8_UNorm );
		return d3d9Device.CreateTexture( width, height, 1, D3d9.Usage.RenderTarget, D3d9.Format.A8R8G8B8, D3d9.Pool.Default, ref sharedHandle );
	}

	static nint getSharedHandle( D3d11.ID3D11Texture2D d3d11Texture2d )
	{
		using( Dxgi.IDXGIResource dxgiResource = d3d11Texture2d.QueryInterface<Dxgi.IDXGIResource>() )
			return dxgiResource.SharedHandle;
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
		Assert( d3d11Texture2d.Description.Format == Dxgi.Format.B8G8R8A8_UNorm );
		using( Dxgi.IDXGISurface dxgiSurface = d3d11Texture2d.QueryInterface<Dxgi.IDXGISurface>() )
		{
			D2d.RenderTargetProperties renderTargetProperties = new()
			{
				Type = D2d.RenderTargetType.Hardware,
				PixelFormat = new( Dxgi.Format.B8G8R8A8_UNorm, DCommon.AlphaMode.Premultiplied ),
				DpiX = 96.0f,
				DpiY = 96.0f,
				Usage = D2d.RenderTargetUsage.None,
				MinLevel = D2d.FeatureLevel.Default
			};
			return d2dFactory.CreateDxgiSurfaceRenderTarget( dxgiSurface, renderTargetProperties );
		}
	}

	static D3d11.ID3D11Texture2D createSharedD3d11Texture2d( D3d11.ID3D11Device d3d11Device, uint deviceWidth, uint deviceHeight )
	{
		D3d11.Texture2DDescription textureDescription = new()
		{
			ArraySize = 1,
			BindFlags = D3d11.BindFlags.RenderTarget,
			Format = Dxgi.Format.B8G8R8A8_UNorm,
			Width = deviceWidth,
			Height = deviceHeight,
			MipLevels = 1,
			SampleDescription = Dxgi.SampleDescription.Default,
			MiscFlags = D3d11.ResourceOptionFlags.Shared
		};
		D3d11.ID3D11Texture2D d3d11Texture2d = d3d11Device.CreateTexture2D( textureDescription );
		Assert( d3d11Texture2d.QueryInterface<Dxgi.IDXGIResource>().SharedHandle != 0 );
		return d3d11Texture2d;
	}

	[SysInterop.DllImport( "user32.dll", SetLastError = false ), SysInterop.DefaultDllImportSearchPaths( SysInterop.DllImportSearchPath.System32 )] static extern nint GetDesktopWindow();

	static D3d9.IDirect3DDevice9Ex createD3d9Device( D3d9.IDirect3D9Ex d3d9, ushort adapterIndex )
	{
		//PEARL: the documentation says "Creating a device requires an HWND. In general, you create a dummy HWND for
		// Direct3D9 to use." They don't say a word about any non-general case where you must supply a non-dummy HWND.
		//(See "Microsoft Learn: WPF and Direct3D9 Interoperation", https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/wpf-and-direct3d9-interoperation)
		//Then they show how to create such a dummy HWND, and they do it by registering a dummy window class and
		//creating a dummy invisible window out of it.
		//However, using the desktop window works just as fine, which begs the question:
		//why THE FUCK do they require a HWND in the first place?
		nint deviceWindowHandle = GetDesktopWindow();

		D3d9.PresentParameters presentParameters = new()
		{
			Windowed = true,
			SwapEffect = D3d9.SwapEffect.Discard,
			DeviceWindowHandle = deviceWindowHandle,
			PresentationInterval = D3d9.PresentInterval.Immediate
		};
		//NOTE: Rumor has it that the Multithreaded flag must be added, despite the fact that we are not doing any
		// multi-threading, because WPF is doing multi-threadfing.
		D3d9.CreateFlags creationFlags = D3d9.CreateFlags.HardwareVertexProcessing | D3d9.CreateFlags.Multithreaded | D3d9.CreateFlags.FpuPreserve;
		return d3d9.CreateDeviceEx( adapterIndex /*0 = D3DADAPTER_DEFAULT*/, D3d9.DeviceType.Hardware, deviceWindowHandle, creationFlags, presentParameters );
	}

	static Dxgi.IDXGIAdapter findBestAdapter( Dxgi.IDXGIFactory7 dxgiFactory, Dxgi.GpuPreference gpuPreference )
	{
		dxgiFactory.EnumAdapterByGpuPreference( 0, gpuPreference, out Dxgi.IDXGIAdapter? dxgiAdapter ).CheckError();
		return dxgiAdapter.OrThrow();
	}

	ushort getAdapterIndex( Dxgi.IDXGIFactory dxgiFactory, D3d11.ID3D11Device d3d11Device )
	{
		using( Dxgi.IDXGIDevice dxgiDevice = d3d11Device.QueryInterface<Dxgi.IDXGIDevice>() )
		using( Dxgi.IDXGIAdapter adapter = dxgiDevice.GetAdapter() )
			return getAdapterIndex( dxgiFactory, adapter );
	}

	ushort getAdapterIndex( Dxgi.IDXGIFactory dxgiFactory, Dxgi.IDXGIAdapter adapter )
	{
		Log.Debug( $"Adapter: {adapter.Description.Description}" );
		for( ushort adapterIndex = 0; dxgiFactory.EnumAdapters( adapterIndex, out Dxgi.IDXGIAdapter thisAdapter ).Success; adapterIndex++ )
		{
			Log.Debug( $"   {adapterIndex}: {thisAdapter.Description.Description}" );
			if( thisAdapter.Description.Description == adapter.Description.Description )
				return adapterIndex;
			thisAdapter.Dispose();
		}
		throw new AssertionFailureException();
	}
}
