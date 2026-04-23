
namespace Editor;

/// <summary>
/// Move selected Gameobjects.<br/> <br/> 
/// <b>Ctrl</b> - toggle snap to grid<br/>
/// <b>Shift</b> - duplicate selection
/// </summary>
[Title( "Move/Position" )]
[Icon( "control_camera" )]
[Alias( "tools.position-tool" )]
[Group( "1" )]
[Order( 0 )]
public class PositionEditorTool : EditorTool
{
	readonly Dictionary<GameObject, Transform> startPoints = [];
	readonly HashSet<Rigidbody> bodies = [];

	Vector3 moveDelta;
	Vector3 handlePosition;
	BBox startBounds;
	bool hasStartBounds;

	IDisposable undoScope;

	public override void OnDisabled()
	{
		base.OnDisabled();

		ClearBodies();
	}

	private void ClearBodies()
	{
		foreach ( var body in bodies )
		{
			if ( !body.IsValid() )
				continue;

			body.SetTargetTransform( null );
		}

		bodies.Clear();
	}

	public override void OnUpdate()
	{
		var nonSceneGos = Selection.OfType<GameObject>().Where( go => go.GetType() != typeof( Sandbox.Scene ) );
		if ( nonSceneGos.Count() == 0 ) return;

		var bbox = BBox.FromPoints( nonSceneGos.Select( x => x.WorldPosition ) );
		var handleRotation = Gizmo.Settings.GlobalSpace ? Rotation.Identity : nonSceneGos.FirstOrDefault().WorldRotation;

		if ( !Gizmo.Pressed.Any && Gizmo.HasMouseFocus )
		{
			ClearBodies();

			startPoints.Clear();
			moveDelta = default;
			handlePosition = bbox.Center;
			hasStartBounds = false;
			undoScope?.Dispose();
			undoScope = null;
		}

		using ( Gizmo.Scope( "Tool", new Transform( bbox.Center ) ) )
		{
			Gizmo.Hitbox.DepthBias = 0.01f;

			if ( Gizmo.Control.Position( "position", Vector3.Zero, out var delta, handleRotation ) )
			{
				moveDelta += delta;

				StartDrag( nonSceneGos );

				var offset = GetMoveOffset( nonSceneGos, handleRotation );

				foreach ( var entry in startPoints )
				{
					OnMoveObject( entry.Key, entry.Value.Add( offset, true ) );
				}
			}
		}
	}

	private Vector3 GetMoveOffset( IEnumerable<GameObject> selectedGos, Rotation handleRotation )
	{
		if ( ShouldTrySurfaceSnap() && TryGetSurfaceSnapOffset( selectedGos, out var surfaceOffset ) )
		{
			return surfaceOffset;
		}

		if ( IsTemporarySurfaceSnap )
		{
			return moveDelta;
		}

		var offset = (moveDelta + handlePosition) * handleRotation.Inverse;
		offset = Gizmo.Snap( offset, moveDelta * handleRotation.Inverse );
		offset *= handleRotation;
		offset -= handlePosition;

		return offset;
	}

	private static bool IsTemporarySurfaceSnap => Gizmo.IsCtrlPressed && Gizmo.IsAltPressed;

	private static bool ShouldTrySurfaceSnap() => SurfaceSnapSettings.Enabled || IsTemporarySurfaceSnap;

	private bool TryGetSurfaceSnapOffset( IEnumerable<GameObject> selectedGos, out Vector3 offset )
	{
		var trace = Scene.Trace
			.Ray( Gizmo.CurrentRay, Gizmo.RayDepth )
			.UseRenderMeshes( true, EditorPreferences.BackfaceSelection )
			.UsePhysicsWorld( false )
			.WithoutTags( "trigger" );

		foreach ( var go in selectedGos )
		{
			trace = trace.IgnoreGameObjectHierarchy( go );
		}

		if ( !TryGetBestSurfaceHit( trace.Run(), selectedGos, out var hitPosition, out var hitNormal ) )
		{
			offset = default;
			return false;
		}

		offset = GetSurfaceSnapTarget( hitPosition, hitNormal ) - handlePosition;
		return true;
	}

	private bool TryGetBestSurfaceHit( SceneTraceResult traceResult, IEnumerable<GameObject> selectedGos, out Vector3 hitPosition, out Vector3 hitNormal )
	{
		var bestDistance = float.MaxValue;
		hitPosition = default;
		hitNormal = default;

		if ( traceResult.Hit )
		{
			bestDistance = traceResult.Distance;
			hitPosition = traceResult.HitPosition;
			hitNormal = traceResult.Normal;
		}

		var ignoredObjects = selectedGos.ToHashSet();
		foreach ( var terrain in Scene.GetAllComponents<Terrain>() )
		{
			if ( !terrain.IsValid() || terrain.Storage is null )
				continue;

			if ( ignoredObjects.Any( go => go.IsValid() && terrain.GameObject.IsAncestor( go ) ) )
				continue;

			if ( !terrain.RayIntersects( Gizmo.CurrentRay, Gizmo.RayDepth, out var localHitPosition ) )
				continue;

			var worldHitPosition = terrain.WorldTransform.PointToWorld( localHitPosition );
			var distance = Vector3.DistanceBetween( Gizmo.CurrentRay.Position, worldHitPosition );
			if ( distance >= bestDistance )
				continue;

			bestDistance = distance;
			hitPosition = worldHitPosition;
			hitNormal = GetTerrainNormal( terrain, localHitPosition );
		}

		return bestDistance < float.MaxValue;
	}

	private Vector3 GetSurfaceSnapTarget( Vector3 hitPosition, Vector3 hitNormal )
	{
		if ( !hasStartBounds )
			return hitPosition;

		var minAlongNormal = startBounds.Corners.Min( corner => Vector3.Dot( corner - handlePosition, hitNormal ) );
		var surfaceDistance = MathF.Max( 0.0f, -minAlongNormal );

		return hitPosition + hitNormal * surfaceDistance;
	}

	private static Vector3 GetTerrainNormal( Terrain terrain, Vector3 localHitPosition )
	{
		var storage = terrain.Storage;
		var resolution = storage.Resolution;
		var sizeScale = storage.TerrainSize / resolution;

		var x = (int)MathF.Floor( localHitPosition.x / storage.TerrainSize * resolution );
		var y = (int)MathF.Floor( localHitPosition.y / storage.TerrainSize * resolution );

		float SampleHeight( int sampleX, int sampleY )
		{
			sampleX = Math.Clamp( sampleX, 0, resolution - 1 );
			sampleY = Math.Clamp( sampleY, 0, resolution - 1 );
			return storage.HeightMap[sampleX + sampleY * resolution] / (float)ushort.MaxValue * storage.TerrainHeight;
		}

		var heightLeft = SampleHeight( x - 1, y );
		var heightRight = SampleHeight( x + 1, y );
		var heightDown = SampleHeight( x, y - 1 );
		var heightUp = SampleHeight( x, y + 1 );

		var localNormal = new Vector3(
			-(heightRight - heightLeft) / (sizeScale * 2.0f),
			-(heightUp - heightDown) / (sizeScale * 2.0f),
			1.0f ).Normal;

		return terrain.WorldTransform.NormalToWorld( localNormal );
	}

	private void StartDrag( IEnumerable<GameObject> selectedGos )
	{
		if ( startPoints.Count != 0 )
			return;

		if ( Gizmo.IsShiftPressed )
		{
			undoScope ??= SceneEditorSession.Active.UndoScope( "Duplicate Object(s)" ).WithGameObjectCreations().Push();

			DuplicateSelection();
		}
		else
		{
			undoScope ??= SceneEditorSession.Active.UndoScope( "Transform Object(s)" ).WithGameObjectChanges( selectedGos, GameObjectUndoFlags.Properties ).Push();

			selectedGos.DispatchPreEdited( nameof( GameObject.LocalPosition ) );
		}

		foreach ( var entry in selectedGos )
		{
			startPoints[entry] = entry.WorldTransform;
		}

		hasStartBounds = TryGetSelectionBounds( startPoints.Keys, out startBounds );
	}

	private static bool TryGetSelectionBounds( IEnumerable<GameObject> gameObjects, out BBox bounds )
	{
		bool hasBounds = false;
		bounds = default;

		foreach ( var go in gameObjects )
		{
			if ( !go.IsValid() )
				continue;

			var goBounds = go.GetBounds();
			if ( !hasBounds )
			{
				bounds = goBounds;
				hasBounds = true;
			}
			else
			{
				bounds = bounds.AddBBox( goBounds );
			}
		}

		return hasBounds;
	}

	private void OnMoveObject( GameObject gameObject, Transform transform )
	{
		if ( !gameObject.IsValid() )
			return;

		if ( !Scene.IsEditor )
		{
			var rb = gameObject.GetComponent<Rigidbody>();

			if ( rb.IsValid() && rb.MotionEnabled )
			{
				bodies.Add( rb );
				rb.SetTargetTransform( transform );

				return;
			}
		}

		gameObject.BreakProceduralBone();
		gameObject.WorldTransform = transform;

		gameObject.DispatchEdited( nameof( GameObject.LocalPosition ) );
	}

	[Shortcut( "tools.position-tool", "w", typeof( SceneViewWidget ) )]
	public static void ActivateSubTool()
	{
		if ( !(EditorToolManager.CurrentModeName == nameof( ObjectEditorTool ) || EditorToolManager.CurrentModeName == "object") ) return;
		EditorToolManager.SetSubTool( nameof( PositionEditorTool ) );
	}
}

internal static class SurfaceSnapSettings
{
	public static bool Enabled
	{
		get => EditorCookie.Get( "SceneView.SurfaceSnap", false );
		set => EditorCookie.Set( "SceneView.SurfaceSnap", value );
	}
}
