using NativeEngine;
using SkiaSharp;

namespace Sandbox;

public partial class Bitmap
{
	public static bool IsHdrImagePath( string path )
	{
		var extension = System.IO.Path.GetExtension( path );

		return extension.Equals( ".exr", StringComparison.OrdinalIgnoreCase )
			|| extension.Equals( ".hdr", StringComparison.OrdinalIgnoreCase )
			|| extension.Equals( ".pfm", StringComparison.OrdinalIgnoreCase );
	}

	public static Bitmap CreateFromHdrFile( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
			return null;

		return CreateFromHdrFileInternal( path );
	}

	static Bitmap CreateFromHdrFileInternal( string path )
	{
		var fbm = FloatBitMap_t.Create();

		try
		{
			var extension = System.IO.Path.GetExtension( path );
			if ( extension.Equals( ".hdr", StringComparison.OrdinalIgnoreCase ) )
			{
				return CreateFromRadianceHdrBytes( System.IO.File.ReadAllBytes( path ) );
			}

			var success = extension.ToLowerInvariant() switch
			{
				".exr" => fbm.LoadFromEXR( path ),
				".pfm" => fbm.LoadFromPFM( path ),
				_ => fbm.LoadFromFile( path, FBMGammaType_t.FBM_GAMMA_LINEAR )
			};

			if ( !success || fbm.Width() <= 0 || fbm.Height() <= 0 )
				return null;

			var bitmap = new SKBitmap( fbm.Width(), fbm.Height(), SKColorType.RgbaF16, SKAlphaType.Unpremul );

			if ( !fbm.WriteToBuffer( bitmap.GetPixels(), bitmap.ByteCount, ImageFormat.RGBA16161616F, false, false, 0 ) )
			{
				bitmap.Dispose();
				return null;
			}

			return new Bitmap( bitmap );
		}
		finally
		{
			fbm.Delete();
		}
	}

	public unsafe bool SaveExr( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) || !IsValid )
			return false;

		var fbm = FloatBitMap_t.Create();

		try
		{
			fbm.LoadFromBuffer( (IntPtr)GetPointer(), ByteCount, IsFloatingPoint ? ImageFormat.RGBA16161616F : ImageFormat.RGBA8888, FBMGammaType_t.FBM_GAMMA_LINEAR );
			return fbm.WriteEXR( path, 0 );
		}
		finally
		{
			fbm.Delete();
		}
	}

	static Bitmap CreateFromRadianceHdrBytes( byte[] data )
	{
		if ( data is null || data.Length < 16 )
			return null;

		var offset = 0;
		var firstLine = ReadAsciiLine( data, ref offset );
		if ( firstLine != "#?RADIANCE" && firstLine != "#?RGBE" )
			return null;

		var format = "";
		for ( ;; )
		{
			var line = ReadAsciiLine( data, ref offset );
			if ( line is null ) return null;
			if ( line.Length == 0 ) break;
			if ( line.StartsWith( "FORMAT=", StringComparison.OrdinalIgnoreCase ) )
				format = line["FORMAT=".Length..];
		}

		if ( !format.Equals( "32-bit_rle_rgbe", StringComparison.OrdinalIgnoreCase ) )
			return null;

		var resolution = ReadAsciiLine( data, ref offset );
		if ( resolution is null ) return null;

		var parts = resolution.Split( ' ', StringSplitOptions.RemoveEmptyEntries );
		if ( parts.Length != 4 || parts[0] != "-Y" || parts[2] != "+X" )
			return null;

		if ( !int.TryParse( parts[1], out var height ) || !int.TryParse( parts[3], out var width ) )
			return null;

		if ( width <= 0 || height <= 0 )
			return null;

		var bitmap = new Bitmap( width, height, true );
		var pixels = bitmap.GetPixels();
		var scanline = new byte[width * 4];

		for ( var y = 0; y < height; y++ )
		{
			if ( !ReadRadianceScanline( data, ref offset, width, scanline ) )
			{
				bitmap.Dispose();
				return null;
			}

			for ( var x = 0; x < width; x++ )
			{
				var r = scanline[x * 4 + 0];
				var g = scanline[x * 4 + 1];
				var b = scanline[x * 4 + 2];
				var e = scanline[x * 4 + 3];

				pixels[y * width + x] = RgbeToColor( r, g, b, e );
			}
		}

		bitmap.SetPixels( pixels );
		return bitmap;
	}

	static bool ReadRadianceScanline( byte[] data, ref int offset, int width, byte[] output )
	{
		if ( offset + 4 > data.Length )
			return false;

		if ( width < 8 || width > 0x7fff || data[offset] != 2 || data[offset + 1] != 2 || (data[offset + 2] & 0x80) != 0 )
		{
			var byteCount = width * 4;
			if ( offset + byteCount > data.Length ) return false;
			Array.Copy( data, offset, output, 0, byteCount );
			offset += byteCount;
			return true;
		}

		var scanlineWidth = (data[offset + 2] << 8) | data[offset + 3];
		offset += 4;
		if ( scanlineWidth != width )
			return false;

		for ( var channel = 0; channel < 4; channel++ )
		{
			var x = 0;
			while ( x < width )
			{
				if ( offset >= data.Length ) return false;
				var count = data[offset++];
				if ( count > 128 )
				{
					count -= 128;
					if ( count == 0 || x + count > width || offset >= data.Length ) return false;
					var value = data[offset++];
					for ( var i = 0; i < count; i++ )
						output[(x++ * 4) + channel] = value;
				}
				else
				{
					if ( count == 0 || x + count > width || offset + count > data.Length ) return false;
					for ( var i = 0; i < count; i++ )
						output[(x++ * 4) + channel] = data[offset++];
				}
			}
		}

		return true;
	}

	static Color RgbeToColor( byte r, byte g, byte b, byte e )
	{
		if ( e == 0 )
			return Color.Black.WithAlpha( 1 );

		var scale = MathF.Pow( 2.0f, e - (128 + 8) );
		return new Color( (r + 0.5f) * scale, (g + 0.5f) * scale, (b + 0.5f) * scale, 1.0f );
	}

	static string ReadAsciiLine( byte[] data, ref int offset )
	{
		if ( offset >= data.Length )
			return null;

		var start = offset;
		while ( offset < data.Length && data[offset] != '\n' )
			offset++;

		if ( offset >= data.Length )
			return null;

		var length = offset - start;
		if ( length > 0 && data[start + length - 1] == '\r' )
			length--;

		offset++;
		return System.Text.Encoding.ASCII.GetString( data, start, length );
	}

}
