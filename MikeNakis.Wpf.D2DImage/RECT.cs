namespace MikeNakis.Wpf.D2DImage;

using Sys = System;
using SysInterop = System.Runtime.InteropServices;
using SysDrawing = System.Drawing;

#pragma warning disable IDE1006 // Naming Styles
#pragma warning disable CA1021 // Avoid out parameters
#pragma warning disable CA1069 // Enums values should not be duplicated
#pragma warning disable CA1401 // P/Invokes should not be visible

//Originally from https://pinvoke.net/default.aspx/Structures/RECT.html

/// <summary>The RECT structure defines the coordinates of the upper-left and lower-right corners of a rectangle.</summary>
/// <remarks>
/// By convention, the right and bottom edges of the rectangle are normally considered exclusive.
/// In other words, the pixel whose coordinates are ( right, bottom ) lies immediately outside of the the rectangle.
/// For example, when RECT is passed to the FillRect function, the rectangle is filled up to, but not including,
/// the right column and bottom row of pixels. This structure is identical to the RECTL structure.
/// </remarks>
[SysInterop.StructLayout( SysInterop.LayoutKind.Sequential )]
public struct RECT : Sys.IEquatable<RECT>
{
	/// <summary>The x-coordinate of the upper-left corner of the rectangle.</summary>
	public int Left;

	/// <summary>The y-coordinate of the upper-left corner of the rectangle.</summary>
	public int Top;

	/// <summary>The x-coordinate of the lower-right corner of the rectangle.</summary>
	public int Right;

	/// <summary>The y-coordinate of the lower-right corner of the rectangle.</summary>
	public int Bottom;

	public RECT( int left, int top, int right, int bottom )
	{
		Left = left;
		Top = top;
		Right = right;
		Bottom = bottom;
	}

	public RECT( SysDrawing.Rectangle r ) : this( r.Left, r.Top, r.Right, r.Bottom ) { }

	public int X
	{
		readonly get => Left;
		set
		{
			Right -= Left - value;
			Left = value;
		}
	}

	public int Y
	{
		readonly get => Top;
		set
		{
			Bottom -= Top - value;
			Top = value;
		}
	}

	public int Height
	{
		readonly get => Bottom - Top; set => Bottom = value + Top;
	}

	public int Width
	{
		readonly get => Right - Left; set => Right = value + Left;
	}

	public SysDrawing.Point Location
	{
		readonly get => new SysDrawing.Point( Left, Top );
		set
		{
			X = value.X;
			Y = value.Y;
		}
	}

	public SysDrawing.Size Size
	{
		readonly get => new SysDrawing.Size( Width, Height );
		set
		{
			Width = value.Width;
			Height = value.Height;
		}
	}

	public static implicit operator SysDrawing.Rectangle( RECT r ) => new SysDrawing.Rectangle( r.Left, r.Top, r.Width, r.Height );

	public static implicit operator RECT( SysDrawing.Rectangle r ) => new RECT( r );

	public static bool operator ==( RECT r1, RECT r2 ) => r1.Equals( r2 );

	public static bool operator !=( RECT r1, RECT r2 ) => !r1.Equals( r2 );

	public readonly bool Equals( RECT r )
	{
		return r.Left == Left && r.Top == Top && r.Right == Right && r.Bottom == Bottom;
	}

	public readonly override bool Equals( object? obj )
	{
		if( obj is RECT kin )
			return Equals( kin );
		else if( obj is SysDrawing.Rectangle rectangle )
			return Equals( new RECT( rectangle ) );
		return false;
	}

	public readonly override int GetHashCode()
	{
		return ((SysDrawing.Rectangle)this).GetHashCode();
	}

	public readonly override string ToString()
	{
		return $"{{Left={Left},Top={Top},Right={Right},Bottom={Bottom}}}";
	}
}
