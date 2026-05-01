# MikeNakis.Wpf.D2DImage

This is the equivalent of `System.Windows.Interop.D3DImage`, but for Direct2D instead of Direct3D. In other words,
it  is an `ImageSource` implementation for WPF which can be used to display Direct2D graphics.

The class offers an `Initialize` method which accepts a `FrameworkElement` as a parameter. At the moment this method
is invoked, the framework element must already be associated with a window handle. So, typically, a main window invokes
this method from its `onSourceInitialized` event handler, passing itself as the framework element, as follows:

```CSharp
public MainWindow()
{
	InitializeComponent();
	SourceInitialized += onSourceInitialized;
}

void onSourceInitialized( object? sender, System.EventArgs e )
{
	TheD2dImage.Initialize( this );
}
```

Once you have initialized the `D2DImage`, you can obtain a Direct2D render target from its `D2DRenderTarget` property. 
This is a `nint`, so you can wrap it in a Vortice `ID2D1RenderTarget` as follows:

```CSharp
Vortice.Direct2D1.ID2D1RenderTarget d2dRenderTarget;
d2dRenderTarget = new Vortice.Direct2D1.ID2D1RenderTarget( TheD2dImage.D2DRenderTarget );
```

From that moment on, you can render Direct2D content as follows:

```CSharp
	System.Windows.Media.CompositionTarget.Rendering += onCompositionTargetRendering;

void onCompositionTargetRendering( object? sender, System.EventArgs e )
{
	if( !TheD2dImage.IsFrontBufferAvailable )
		return;
	TheD2dImage.Lock();
	d2dRenderTarget.BeginDraw();
	...
	d2dRenderTarget.EndDraw().CheckError();
	TheD2dImage.AddDirtyRect( new System.Windows.Int32Rect( 0, 0, TheD2dImage.PixelWidth, TheD2dImage.PixelHeight ) );
	TheD2dImage.Unlock();
}
```

------------

### History

Once upon a time a guy called Jeremiah Morrill created a class called `D3DImageEx` which was a subclass of 
`System.Windows.Interop.D3DImage`, adding support for Direct3D 10/11 render targets. Using it looked like this:

    TheD3dImageEx.SetBackBufferEx( D3DResourceTypeEx.ID3D10Texture2D, pRenderTarget );

The `pRenderTarget` parameter needed to be a pointer to an `ID3D10Texture2D`, which you had to somehow create.

He posted an article titled "Direct3D 10/11/Direct2D in WPF" on a web site called "WPF Disciples" describing how to use 
his `D3DImageEx` class. The article is not there anymore, but luckily it has been archived by the Wayback Machine:

<https://web.archive.org/web/20210923054011/http://jmorrill.hjtcentral.com/Home/tabid/428/EntryId/437/Direct3D-10-11-Direct2D-in-WPF.aspx>

The article came with source code in the form of an `Interop.D3DImageEx.zip` file, which can still be downloaded from archive.org.

Unfortunately, that source code is very old and does not compile with a modern toolset. Also, it contains code written 
in C++/CLI, which has fallen out of grace in recent years. Most importantly, I have no interest in Direct3D 10/11 
rendering; my interest lies in Direct2D rendering. Jeremiah's code looked promising because creating a Direct2D render
target from an `ID3D11Texture2D` is relatively easy: the `ID3D11Texture2D` can be query-interfaced into an 
`IDXGISurface`, and then the `CreateDxgiSurfaceRenderTarget()` method can be invoked to yield an `ID2D1RenderTarget`.

So, I decided to revamp Jeremiah's code for Direct2D rendering, and to get it to work with a modern toolset.

First of all, I removed all the C++ code and re-wrote what I needed in C# using the [Vortice library](https://github.com/amerkoleci/Vortice.Windows).

Then, everything that dealt with Direct3D 10 had to go, because Vortice does not support Direct3D 10.

And then, I drastically changed the way the class works, so that the lifetime and the resizing of the 2D texture is 
managed internally by the class itself, and not by the user of the class.

Needless to say, by the time I was done, the class was completely different from the original `D3DImageEx` class. So, I
called it `D2DImage`, to reflect the fact that it is now a Direct2D image source, and not a Direct3D image source.

Of course, when I got it to compile, it refused to work, so I had to do a lot of research and troubleshooting, including 
asking questions on Stack Overflow, like [Direct2D in WPF: what am I doing wrong?](https://stackoverflow.com/q/79931793/773113)
and [How come my single-adapter windowed Direct3D application renders on either monitor on a dual GPU system?](https://stackoverflow.com/q/79933905/773113)

I think I may have finally gotten it to work, but I cannot be sure because DirectX appears to be extremely sensitive to
the hardware configuration, and I only have a single system to try it on. DirectX even appears to be sensitive to
different monitor configurations on the same system, so, all bets are off. (You would not believe the pains I had to go
through in order to get this thing to work with my dual-adapter laptop both when an external monitor was connected and 
when it was not.)
