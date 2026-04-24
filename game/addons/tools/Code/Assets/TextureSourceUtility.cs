namespace Editor;

internal static class TextureSourceUtility
{
	public static bool IsHdrImageAsset( Asset asset )
	{
		if ( asset?.AssetType != AssetType.ImageFile )
			return false;

		var extension = System.IO.Path.GetExtension( asset.AbsolutePath );

		return extension.Equals( ".exr", StringComparison.OrdinalIgnoreCase )
			|| extension.Equals( ".hdr", StringComparison.OrdinalIgnoreCase )
			|| extension.Equals( ".pfm", StringComparison.OrdinalIgnoreCase );
	}

	public static Asset GetOrCreateTextureAssetForImage( Asset asset )
	{
		if ( !IsHdrImageAsset( asset ) )
			return asset;

		var outputPath = NormalizeImageAsset( asset );
		if ( string.IsNullOrWhiteSpace( outputPath ) )
			return asset;

		var existing = AssetSystem.FindByPath( outputPath );

		if ( existing?.AssetType == AssetType.ImageFile )
			return existing;

		return AssetSystem.RegisterFile( outputPath ) ?? asset;
	}

	static string NormalizeImageAsset( Asset asset )
	{
		var sourcePath = asset.AbsolutePath;
		var extension = System.IO.Path.GetExtension( sourcePath );

		if ( extension.Equals( ".hdr", StringComparison.OrdinalIgnoreCase ) )
		{
			using var bitmap = Bitmap.CreateFromHdrFile( sourcePath );
			if ( bitmap is null )
			{
				return null;
			}

			var exrPath = System.IO.Path.ChangeExtension( sourcePath, ".exr" );
			var tempPath = exrPath + ".tmp";
			if ( !SaveBitmapExr( bitmap, tempPath ) )
				return null;

			System.IO.File.Move( tempPath, exrPath, true );
			System.IO.File.Delete( sourcePath );
			return exrPath;
		}

		if ( !extension.Equals( ".exr", StringComparison.OrdinalIgnoreCase ) )
			return null;

		using ( var bitmap = Bitmap.CreateFromHdrFile( sourcePath ) )
		{
			if ( bitmap is null )
				return asset.AbsolutePath;

			var tempPath = sourcePath + ".tmp";
			if ( SaveBitmapExr( bitmap, tempPath ) )
				System.IO.File.Move( tempPath, sourcePath, true );
		}

		return sourcePath;
	}

	static bool SaveBitmapExr( Bitmap bitmap, string path )
	{
		var method = typeof( Bitmap ).GetMethod( "SaveExr", [typeof( string )] );
		if ( method is null )
			return false;

		return method.Invoke( bitmap, [path] ) is true;
	}
}
