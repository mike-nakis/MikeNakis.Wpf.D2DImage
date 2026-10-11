# MikeNakis.Wpf.D2DImage

This is the equivalent of `System.Windows.Interop.D3DImage`, but for Direct2D instead of Direct3D. In other words,
it is a WPF `ImageSource` that can be used to display Direct2D graphics.

## Benefits

No air-space issues: You can draw WPF controls on top of the Direct2D surface, and WPF will properly compose them all
together on the screen.

## Drawbacks

If you use Direct2D, this means you want things done FAST. However, if you use this control, the surface you draw will
participate in the WPF composition process, thus negating, in part, the benefits of using Direct2D in the first place. 

## How to use it

You declare it the same way you declare `System.Windows.Interop.D3DImage`:

```xaml
	xmlns:d2dImage="clr-namespace:MikeNakis.Wpf.D2DImage;assembly=MikeNakis.Wpf.D2DImage"
	...
<Image Stretch="Fill">
	<Image.Source>
		<d2dImage:D2DImage x:Name="TheD2dImage"/>
	</Image.Source>
</Image>
```

Then in your main window...

```CSharp
D2d.ID2D1RenderTarget d2dRenderTarget;

(in the constructor)
	SourceInitialized += onSourceInitialized;

void onSourceInitialized( object? sender, System.EventArgs e )
{
	TheD2dImage.Initialize( this, onRealizeRenderTarget );
	System.Windows.Media.CompositionTarget.Rendering += onCompositionTargetRendering;
}

void onRealizeRenderTarget( nint renderTargetNativeHandle )
{
	d2dRenderTarget = new D2d.ID2D1RenderTarget( renderTargetNativeHandle );
}

void onCompositionTargetRendering( object? sender, System.EventArgs e )
{
	if( !TheD2dImage.IsFrontBufferAvailable )
		return;
	TheD2dImage.Lock();
	d2dRenderTarget.BeginDraw();
	...
	d2dRenderTarget.EndDraw().CheckError();
	TheD2dImage.AddDirtyRect( new Wpf.Int32Rect( 0, 0, TheD2dImage.PixelWidth, TheD2dImage.PixelHeight ) );
	TheD2dImage.Unlock();
}
```

## How it works

In one sentence: 

The D2D render target that you to draw in is created in such a way that it actually draws into a D3D11 texture. 
The texture is shared with D3D9, where it is cast as a D3D9 surface, and set as the back-buffer of the 
underlying `System.Windows.Interop.D3DImage`. 
So, when you draw in the D2D render target, you are actually drawing in the back-buffer of the D3DImage.

In more detail:

- First, it uses DXGI to find the best (most performant) adapter.

- Then, it creates a `ID3D11Device` on the best adapter.

- When you call the `Initialize()` method, it creates on the same adapter an `IDirect3DDevice9Ex`, associated with the 
window handle you provided.

- Then, it creates a shareable `ID3D11Texture2D` and obtains the share-handle.

- Then, it uses the share-handle to open the same D3D11 texture in D3D9, as a `IDirect3DTexture9`.

- Then, it casts the `IDirect3DTexture9` as a `IDirect3DSurface9`, and realizes it as the back-buffer of the underlying 
`System.Windows.Interop.D3DImage`.

- Finally, it casts the `ID3D11Texture2D` as a `IDXGISurface`, and calls `D2DFactory.CreateDxgiSurfaceRenderTarget()`
to create an `ID2D1RenderTarget` for you to draw in.

The [documentation of the ID3D11Device::OpenSharedResource() method](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11device-opensharedresource)
says that an alternative way of creating a shared a texture between D3D11 and D3D9 is to begin by creating the D3D9 
texture with a NULL shared-handle, in which case a new shared-handle will be generated, and then pass that share-handle 
to `ID3D11Device::OpenSharedResource()` to obtain the `ID3D11Texture2`. I have not tried that approach.

------------

### History

Once upon a time a guy called Jeremiah Morrill created a class called `D3DImageEx` which was a subclass of 
`System.Windows.Interop.D3DImage`, adding support for Direct3D 10/11 render targets. Using it looked like this:

    TheD3dImageEx.SetBackBufferEx( D3DResourceTypeEx.ID3D10Texture2D, pRenderTarget );

The `pRenderTarget` parameter needed to be an `nint` pointing to an `ID3D10Texture2D`, which you had to create.

He posted an article titled "Direct3D 10/11/Direct2D in WPF" on a web site called "WPF Disciples" describing how to use 
his `D3DImageEx` class. The article is not there anymore, but luckily it has been archived by the Wayback Machine:

<https://web.archive.org/web/20210923054011/http://jmorrill.hjtcentral.com/Home/tabid/428/EntryId/437/Direct3D-10-11-Direct2D-in-WPF.aspx>

The article came with source code in the form of an `Interop.D3DImageEx.zip` file, which can still be downloaded from archive.org.

Unfortunately, that source code is very old and does not compile with a modern toolset. Also, it contains code written 
in C++/CLI, which has fallen out of grace in recent years. Most importantly, I have no interest in Direct3D 10/11 
rendering; my interest lies in Direct2D rendering. Jeremiah's code looked promising because creating a Direct2D render
target from an `ID3D11Texture2D` is relatively easy: the `ID3D11Texture2D` can be cast to an  `IDXGISurface` by means
of `QueryInterface<>()`, and then the `CreateDxgiSurfaceRenderTarget()` method can be invoked to yield an
`ID2D1RenderTarget`.

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
