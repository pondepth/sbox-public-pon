using System.IO;

namespace Editor;

class FolderNode : TreeNode<LocalAssetBrowser.Location>
{
	public override string Name => Value.Name;
	public string Icon { get; set; }
	public bool TitleCase { get; set; } = false;

	DirectoryEntry.FolderMetadata Metadata;

	FileSystemWatcher watcher;

	public FolderNode( LocalAssetBrowser.Location location ) : base( location )
	{
		Icon = location.Icon;
		Metadata = DirectoryEntry.GetMetadata( Value.Path );

		if ( location is DiskLocation )
		{
			watcher = new FileSystemWatcher( location.Path );
			watcher.EnableRaisingEvents = true;
			watcher.Created += OnExternalChanges;
			watcher.Deleted += OnExternalChanges;
			watcher.Renamed += OnExternalChanges;
		}
	}

	~FolderNode()
	{
		watcher?.Dispose();
	}

	private void OnExternalChanges( object sender, FileSystemEventArgs e )
	{
		Dirty();
	}

	protected override void BuildChildren()
	{
		Clear();

		foreach ( var dir in Value.GetDirectories().OrderBy( x => x.Name ) )
		{
			AddItem( CreateChildFor( dir ) );
		}
	}

	protected virtual TreeNode CreateChildFor( LocalAssetBrowser.Location dir ) => new FolderNode( dir );

	public override void OnPaint( VirtualWidget item )
	{
		PaintSelection( item );

		var rect = item.Rect;

		Paint.SetPen( Theme.Yellow );
		var iconRect = Paint.DrawIcon( rect, Icon, 18, TextFlag.LeftCenter );

		if ( Value.ContentsIcon is not null )
		{
			Paint.SetPen( Theme.Yellow );
			Paint.DrawIcon( iconRect.Shrink( 0, 1, 0, 0 ), Value.ContentsIcon, 9, TextFlag.Center );
		}

		rect.Left += 24;

		Paint.SetPen( Theme.Text );
		Paint.SetDefaultFont();
		Paint.DrawText( rect, TitleCase ? Name.ToTitleCase() : Name, TextFlag.LeftCenter );
	}

	public System.Action OnContextMenuOpen;

	public override bool OnContextMenu()
	{
		if ( OnContextMenuOpen != null )
		{
			OnContextMenuOpen.Invoke();
			return true;
		}

		if ( Value is DiskLocation diskLocation )
		{
			var fcm = new FolderContextMenu
			{
				Target = new DirectoryInfo( diskLocation.Path ),
				ScreenPosition = Application.CursorPosition,
				Menu = new ContextMenu( null ),
				ThisFolder = false,
				Context = this
			};

			EditorEvent.Run( "folder.contextmenu", fcm );

			if ( !fcm.Menu.HasOptions )
				return true;

			fcm.Menu.OpenAt( fcm.ScreenPosition );
		}

		return true;
	}

	public override void OnDragHover( Widget.DragEvent ev )
	{

	}

	public override bool OnDragStart()
	{
		if ( Parent is null ) return false;

		var drag = new Drag( TreeView );
		drag.Data.Text = Value.Path;
		drag.Data.Url = new System.Uri( "file:///" + Value.Path );

		drag.Execute();

		return true;
	}

	public override DropAction OnDragDrop( BaseItemWidget.ItemDragEvent e )
	{
		var dropAction = e.HasCtrl ? DropAction.Move : DropAction.Copy;
		if ( !e.IsDrop ) return dropAction;

		foreach ( var file in e.Data.Files )
		{
			if ( file.ToLowerInvariant() == Value.Path.ToLowerInvariant() ) continue;
			var asset = AssetSystem.FindByPath( file );

			if ( asset is null )
			{
				if ( !System.IO.Path.Exists( file ) ) continue;

				// This isn't an asset so just copy the file in directly
				var destinationFile = System.IO.Path.Combine( Value.Path, System.IO.Path.GetFileName( file ) );

				if ( System.IO.Directory.Exists( file ) )
				{
					if ( dropAction == DropAction.Copy )
						DroppedAssetRegistration.CopyDirectory( file, destinationFile );
					else
					{
						EditorUtility.RenameDirectory( file, destinationFile );
						DirectoryEntry.RenameMetadata( file, destinationFile );
					}

					DroppedAssetRegistration.Register( destinationFile );
				}
				else
				{
					// Move File
					if ( System.IO.Path.GetFullPath( file ) == System.IO.Path.GetFullPath( destinationFile ) )
						continue;

					if ( dropAction == DropAction.Copy )
						System.IO.File.Copy( file, destinationFile );
					else
						System.IO.File.Move( file, destinationFile );

					DroppedAssetRegistration.Register( destinationFile );
				}
			}
			else
			{
				if ( asset.IsDeleted ) continue;
				if ( dropAction == DropAction.Copy )
					EditorUtility.CopyAssetToDirectory( asset, Value.Path );
				else
					EditorUtility.MoveAssetToDirectory( asset, Value.Path );
			}
		}

		var rootParent = Parent;
		rootParent.Dirty();

		return dropAction;
	}
}

static class DroppedAssetRegistration
{
	public static void Register( string path )
	{
		if ( System.IO.File.Exists( path ) )
		{
			RegisterFile( path );
			return;
		}

		if ( !System.IO.Directory.Exists( path ) )
			return;

		foreach ( var file in System.IO.Directory.EnumerateFiles( path, "*", SearchOption.AllDirectories ) )
		{
			RegisterFile( file );
		}
	}

	public static void CopyDirectory( string source, string destination )
	{
		if ( !System.IO.Directory.Exists( source ) )
			return;

		System.IO.Directory.CreateDirectory( destination );

		foreach ( var file in System.IO.Directory.EnumerateFiles( source, "*", SearchOption.TopDirectoryOnly ) )
		{
			System.IO.File.Copy( file, System.IO.Path.Combine( destination, System.IO.Path.GetFileName( file ) ) );
		}

		foreach ( var directory in System.IO.Directory.EnumerateDirectories( source, "*", SearchOption.TopDirectoryOnly ) )
		{
			CopyDirectory( directory, System.IO.Path.Combine( destination, System.IO.Path.GetFileName( directory ) ) );
		}
	}

	private static void RegisterFile( string path )
	{
		var extension = System.IO.Path.GetExtension( path );
		if ( string.IsNullOrWhiteSpace( extension ) )
			return;

		if ( extension.Equals( ".meta", StringComparison.OrdinalIgnoreCase ) )
			return;

		AssetSystem.RegisterFile( path );
	}
}


class PinnedFolderNode : FolderNode
{
	public PinnedFolderNode( LocalAssetBrowser.Location location ) : base( location )
	{
	}

	protected override void BuildChildren()
	{
	}
}
