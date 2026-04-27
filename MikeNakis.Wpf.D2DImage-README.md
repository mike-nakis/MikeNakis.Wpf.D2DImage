# Experiments: D2DImage

Once upon a time some Jeremiah Morrill guy from some "WPF Disciples" site created a subclass of
`System.Windows.Interop.D3DImage` which adds support for D3D 10/11 render targets (Texture2D).
The `D3DResourceTypeEx` enumeration also has an `ID3D11Texture2D` member.

Using it looks like this:

    TheD3dImageEx.SetBackBufferEx( D3DResourceTypeEx.ID3D10Texture2D, pRenderTarget );

The article can be found here:

https://web.archive.org/web/20210923054011/http://jmorrill.hjtcentral.com/Home/tabid/428/EntryId/437/Direct3D-10-11-Direct2D-in-WPF.aspx

The article contains an `Interop.D3DImageEx.zip` which can actually be downloaded from archive.org.

This is my attempt to compile it with a modern toolset and to get it to work.

First of all, I removed all the C++ code and re-wrote what I needed in C# using Vortice.

All the D3D10 code had to go, because Vortice does not support D3D10.

I also ended up ditching the `D3DImageEx` class and moving all of its functionality into `MainWindow`, which now uses a
plain `System.Windows.Interop.D3DImage`.

When I tried it, it refused to work, so I went to Stack Overflow: [Direct2D in WPF: what am I doing wrong?](https://stackoverflow.com/q/79931793/773113)

A guy called Simon Mourier left an answer with some suggestion modifications; I tried them, it still did not work.
