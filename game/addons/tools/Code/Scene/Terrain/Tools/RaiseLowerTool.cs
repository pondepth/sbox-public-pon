
namespace Editor.TerrainEditor;

/// <summary>
/// Click and drag to raise terrain.<br/> <br/>
/// <b>Ctrl</b> - lower terrain
/// </summary>
/// 
[Title( "Raise / Lower" )]
[Icon( "height" )]
[Alias( "raise_lower" )]
[Group( "1" )]
[Order( 0 )]
public class RaiseLowerTool : BaseBrushTool
{
	bool _randomBrushRotation;

	[Property, Title( "Random Brush Rotation" )]
	public bool RandomBrushRotation
	{
		get => _randomBrushRotation;
		set
		{
			if ( _randomBrushRotation == value )
				return;

			_randomBrushRotation = value;
			_brushRotation = _randomBrushRotation ? GetRandomBrushRotation() : 0.0f;
		}
	}

	public RaiseLowerTool( TerrainEditorTool terrainEditorTool ) : base( terrainEditorTool )
	{
		Mode = SculptMode.RaiseLower;
		AllowBrushInvert = true;
	}

	protected override float GetBrushRotation()
	{
		return RandomBrushRotation ? GetRandomBrushRotation() : 0.0f;
	}

	static float GetRandomBrushRotation()
	{
		return Random.Shared.NextSingle() * MathF.PI * 2.0f;
	}
}
