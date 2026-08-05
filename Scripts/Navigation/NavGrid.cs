using System;
using System.Collections.Generic;
using Godot;

public partial class NavGrid : Node
{
	[Signal] public delegate void NavigationReadyEventHandler();

	[ExportGroup("Terrain")]
	[Export(PropertyHint.Range, "0,90,1")]
	private int _maxWalkableSlopeDegrees = 25;
	public bool IsNavReady { get; private set; }
	private ushort[] _slopeMap;
	private byte[] _navigationPacket;
	private readonly HashSet<int> _pendingNavPeers = new();


	[ExportGroup("Debug")]
	[Export] private bool _drawNavGrid;
	[Export] public Color GridColor { get; set; } = new(1.0f, 1.0f, 1.0f, 0.35f);
	[Export] public Color SectorColor { get; set; } = new(1.0f, 0.55f, 0.1f, 0.9f);
	[Export] public Color BlockedColor { get; set; } = new(1.0f, 0.05f, 0.05f, 0.9f);
	[Export] public Color FlowColor { get; set; } = new(0.1f, 0.75f, 1.0f, 0.9f);
	[Export] public Color PortalColor { get; set; } = new(0.2f, 1.0f, 0.35f, 0.95f);
	[Export] public Color TargetColor { get; set; } = new(1.0f, 0.9f, 0.1f, 1.0f);
	[Export(PropertyHint.Layers3DPhysics)] private uint _terrainCollisionMask = 1u << 1;
	[Export] private float _terrainRaycastHeight = 1000.0f;
	private int _unitSpacingInCells = 3;
	private int _width { get; set; }
	private int _height { get; set; }
	private int _sectorSize = 15;
	private int _sectorWidth;
	private int _sectorHeight;
	private Vector2I _gridOrigin;
	private NavCell[,] _cells;
	private NavSector[,] _sectors;
	private float[,] _heightMap;
	private Dictionary<int, NavFlowField> _flowFields = new();
	private int _nextFlowFieldID = 1;
	private int CellSize = 2;
	private int _diagonalCost = 14;
	private int _straightCost = 10;
	private readonly NavGridDebugRenderer _debugRenderer = new();
	private LocalResources _localResources;
	private readonly Vector2I[] _offsets =
	[
		new Vector2I(0, -1),  // N
		new Vector2I(0, 1),   // S
		new Vector2I(1, 0),   // E
		new Vector2I(-1, 0),  // W

		new Vector2I(1, -1),  // NE
		new Vector2I(-1, -1), // NW
		new Vector2I(1, 1),   // SE
		new Vector2I(-1, 1)   // SW
	];

	public override void _Ready()
	{
		_localResources = GetTree().CurrentScene as LocalResources;
		if (_localResources == null)
		{
			GD.PrintErr("[NavGrid.Ready()] Could not find LocalResources");
		}
		else
		{
			_width = _localResources.MapSize.X / CellSize;
			_height = _localResources.MapSize.Y / CellSize;

			_sectorWidth = Mathf.CeilToInt(_width / (float)_sectorSize);
			_sectorHeight = Mathf.CeilToInt(_height / (float)_sectorSize);

			_gridOrigin = new Vector2I(-_localResources.MapSize.X / 2, -_localResources.MapSize.Y / 2);
		}

		InitGrid();
		InitSectors();
		// LoadObstacles();
		// BuildSectorPortals();
		// CallDeferred(nameof(BuildHeightMap));
		CallDeferred(nameof(InitNavigation));

		_debugRenderer.SetSectorSize(_sectorSize);
		_debugRenderer.SetColors(GridColor, BlockedColor, FlowColor, TargetColor, SectorColor, PortalColor);

		if (_drawNavGrid)
		{
			_debugRenderer.DrawGridLines(this, _gridOrigin, _width, _height, CellSize);
			_debugRenderer.DrawBlockedCells(this, _gridOrigin, CellSize, _cells);
			_debugRenderer.DrawPortalCells(this, _gridOrigin, _width, _height, CellSize, _sectors);
		}
	}

	public override void _Process(double delta)
	{
		if (_drawNavGrid)
		{
			_debugRenderer.SetSectorSize(_sectorSize);
			_debugRenderer.SetColors(GridColor, BlockedColor, FlowColor, TargetColor, SectorColor, PortalColor);
		}
	}

	public int BuildField(
		List<Vector2I> startPositions,
		Vector2I targetPos,
		Rect2I targetSectorRegion)
	{
		const int maxExpansionRadius = 2;

		HashSet<Vector2I> baseAllowedSectors = BuildAllowedSectorSet(
			startPositions,
			targetPos,
			targetSectorRegion
		);
		HashSet<Vector2I> resolvedAllowedSectors = null;

		for (int radius = 0; radius <= maxExpansionRadius; radius++)
		{
			HashSet<Vector2I> allowedSectors = ExpandAllowedSectors(baseAllowedSectors, radius);

			bool isIntegrationFieldBuilt = BuildIntegrationField(targetSectorRegion, allowedSectors);
			if (!isIntegrationFieldBuilt)
				continue;

			if (!AreStartCellsReachable(startPositions))
				continue;

			resolvedAllowedSectors = allowedSectors;
			break;
		}

		if (resolvedAllowedSectors == null)
		{
			HashSet<Vector2I> allSectors = BuildAllSectorSet();

			bool isIntegrationFieldBuilt = BuildIntegrationField(targetSectorRegion, allSectors);
			if (!isIntegrationFieldBuilt)
				return -1;

			resolvedAllowedSectors = allSectors;
		}

		NavFlowField flowField = BuildFlowField(
			resolvedAllowedSectors,
			targetPos,
			targetSectorRegion
		);
		int flowFieldID = _nextFlowFieldID++;
		_flowFields[flowFieldID] = flowField;

		if (_drawNavGrid)
		{
			_debugRenderer.SetColors(GridColor, BlockedColor, FlowColor, TargetColor, SectorColor, PortalColor);
			_debugRenderer.DrawFlowArrows(this, _gridOrigin, CellSize, _cells);
			_debugRenderer.DrawPortalCells(this, _gridOrigin, _width, _height, CellSize, _sectors);
			_debugRenderer.DrawTarget(this, _gridOrigin, CellSize, targetPos);
		}

		return flowFieldID;
	}

	public List<Vector2I> FindDestinationCells(
		Vector2I centerCell,
		int destCount)
	{
		var destinations = new List<Vector2I>(destCount);
		if (destCount <= 0)
			return destinations;

		int spacing = Math.Max(1, _unitSpacingInCells);
		long minDistSquared = (long)spacing * spacing;

		void TryAddDestination(Vector2I cellPos)
		{
			if (destinations.Count >= destCount)
				return;

			NavCell cell = GetCell(cellPos);
			if (cell == null || !cell.Walkable)
				return;

			foreach (Vector2I dest in destinations)
			{
				Vector2I diff = cellPos - dest;
				long distSquared =
					(long)diff.X * diff.X +
					(long)diff.Y * diff.Y;

				if (distSquared < minDistSquared)
					return;
			}

			destinations.Add(cellPos);
		}

		int maxSearchRadius = Math.Max(_width, _height);

		for (int radius = 0; radius <= maxSearchRadius && destinations.Count < destCount; radius++)
		{
			if (radius == 0)
			{
				TryAddDestination(centerCell);
				continue;
			}

			int minOffset = -radius;
			int maxOffset = radius;

			for (int x = minOffset; x <= maxOffset; x++)
				TryAddDestination(centerCell + new Vector2I(x, minOffset));

			for (int y = minOffset + 1; y <= maxOffset; y++)
				TryAddDestination(centerCell + new Vector2I(maxOffset, y));

			for (int x = maxOffset - 1; x >= minOffset; x--)
				TryAddDestination(centerCell + new Vector2I(x, maxOffset));

			for (int y = maxOffset - 1; y > minOffset; y--)
				TryAddDestination(centerCell + new Vector2I(minOffset, y));
		}

		return destinations;
	}

	public Vector2I GetDirection(Vector2I cellPos)
	{
		NavCell cell = GetCell(cellPos);
		if (cell == null)
			return Vector2I.Zero;

		return cell.Direction;
	}

	public Vector2I GetDirection(int flowFieldID, Vector2I cellPos)
	{
		if (!_flowFields.TryGetValue(flowFieldID, out NavFlowField flowField))
			return Vector2I.Zero;

		Vector2I direction = flowField.GetDirection(cellPos);
		if (direction != Vector2I.Zero)
			return direction;

		NavSector sector = GetSectorForCell(cellPos);
		if (sector == null || flowField.IsSectorCalculated(sector.Position))
			return Vector2I.Zero;

		return TryExpandFlowFieldToSector(flowField, sector)
			? flowField.GetDirection(cellPos)
			: Vector2I.Zero;
	}

	public bool TryGetNearestFlowCell(
		int flowFieldID,
		Vector2I cellPos,
		int maxSearchRadius,
		out Vector2I flowCell)
	{
		flowCell = default;
		if (!_flowFields.TryGetValue(flowFieldID, out NavFlowField flowField))
			return false;

		return flowField.TryGetNearestDirectedCell(cellPos, maxSearchRadius, out flowCell);
	}

	public bool TryFindPath(
		Vector2I startCellPos,
		Vector2I targetCellPos,
		out List<Vector2I> path)
	{
		path = new List<Vector2I>();
		NavCell startCell = GetCell(startCellPos);
		NavCell targetCell = GetCell(targetCellPos);

		if (startCell == null || targetCell == null || !targetCell.Walkable)
			return false;

		if (startCellPos == targetCellPos)
			return true;

		var frontier = new PriorityQueue<Vector2I, (int Cost, int Y, int X)>();
		var costSoFar = new Dictionary<Vector2I, int>();
		var cameFrom = new Dictionary<Vector2I, Vector2I>();

		frontier.Enqueue(startCellPos, (0, startCellPos.Y, startCellPos.X));
		costSoFar[startCellPos] = 0;

		while (frontier.TryDequeue(out Vector2I currentCellPos, out _))
		{
			if (currentCellPos == targetCellPos)
				break;

			int currentCost = costSoFar[currentCellPos];
			foreach (NavCell neighbor in GetNeighbors(currentCellPos))
			{
				int newCost =
					currentCost + GetMoveCost(currentCellPos, neighbor.Position) * neighbor.Cost;

				if (costSoFar.TryGetValue(neighbor.Position, out int existingCost) &&
					existingCost <= newCost)
				{
					continue;
				}

				costSoFar[neighbor.Position] = newCost;
				cameFrom[neighbor.Position] = currentCellPos;

				Vector2I targetOffset = targetCellPos - neighbor.Position;
				int diagonalSteps = Math.Min(Math.Abs(targetOffset.X), Math.Abs(targetOffset.Y));
				int straightSteps = Math.Max(Math.Abs(targetOffset.X), Math.Abs(targetOffset.Y)) - diagonalSteps;
				int estimatedCost =
					diagonalSteps * _diagonalCost + straightSteps * _straightCost;

				frontier.Enqueue(
					neighbor.Position,
					(newCost + estimatedCost, neighbor.Position.Y, neighbor.Position.X)
				);
			}
		}

		if (!cameFrom.ContainsKey(targetCellPos))
			return false;

		Vector2I pathCellPos = targetCellPos;
		while (pathCellPos != startCellPos)
		{
			path.Add(pathCellPos);
			pathCellPos = cameFrom[pathCellPos];
		}

		path.Reverse();
		return true;
	}

	public void SetWalkable(Vector2I cellPos, bool walkable)
	{
		NavCell cell = GetCell(cellPos);
		if (cell == null)
			return;

		cell.Walkable = walkable;
	}

	public Vector2I WorldToCell(Vector3 worldPos)
	{
		int x = Mathf.FloorToInt((worldPos.X - _gridOrigin.X) / CellSize);
		int z = Mathf.FloorToInt((worldPos.Z - _gridOrigin.Y) / CellSize);

		return new Vector2I(x, z);
	}

	public Vector3 CellToWorld(Vector2I cellPos)
	{
		float x = _gridOrigin.X + cellPos.X * CellSize + CellSize / 2.0f;
		float z = _gridOrigin.Y + cellPos.Y * CellSize + CellSize / 2.0f;
		float y = GetTerrainHeight(new Vector2(x, z));

		return new Vector3(x, y, z);
	}

	public bool AreCellsInSameSector(Vector2I firstCell, Vector2I secondCell)
	{
		NavSector firstSector = GetSectorForCell(firstCell);
		NavSector secondSector = GetSectorForCell(secondCell);

		return
			firstSector != null &&
			secondSector != null &&
			firstSector.Position == secondSector.Position;
	}

	public Rect2I GetSectorRegion(IReadOnlyList<Vector2I> cellPositions)
	{
		if (cellPositions.Count == 0)
			return default;

		Vector2I minSector = new(int.MaxValue, int.MaxValue);
		Vector2I maxSector = new(int.MinValue, int.MinValue);

		foreach (Vector2I cellPos in cellPositions)
		{
			NavSector sector = GetSectorForCell(cellPos);
			if (sector == null)
				continue;

			minSector = new Vector2I(
				Math.Min(minSector.X, sector.Position.X),
				Math.Min(minSector.Y, sector.Position.Y)
			);
			maxSector = new Vector2I(
				Math.Max(maxSector.X, sector.Position.X),
				Math.Max(maxSector.Y, sector.Position.Y)
			);
		}

		if (minSector.X == int.MaxValue)
			return default;

		return new Rect2I(minSector, maxSector - minSector + Vector2I.One);
	}

	public bool IsCellInSectorRegion(Vector2I cellPos, Rect2I sectorRegion)
	{
		NavSector sector = GetSectorForCell(cellPos);
		if (sector == null)
			return false;

		Vector2I regionEnd = sectorRegion.Position + sectorRegion.Size;
		return
			sector.Position.X >= sectorRegion.Position.X &&
			sector.Position.Y >= sectorRegion.Position.Y &&
			sector.Position.X < regionEnd.X &&
			sector.Position.Y < regionEnd.Y;
	}

	public float GetTerrainHeight(Vector2 worldPos)
	{
		if (_heightMap == null)
			return GetFallbackGroundHeight();

		Vector2I cell = WorldToCell(new Vector3(worldPos.X, 0.0f, worldPos.Y));

		if (cell.X < 0 || cell.Y < 0 || cell.X >= _width || cell.Y >= _height)
			return GetFallbackGroundHeight();

		return _heightMap[cell.X, cell.Y];
	}

	public bool RebuildHeightMap(Vector2I sectorPosition)
	{
		return RebuildHeightMap(new Rect2I(sectorPosition, Vector2I.One));
	}

	public bool RebuildHeightMap(Rect2I sectorRegion)
	{
		if (!TryGetCellRegion(sectorRegion, out Rect2I cellRegion))
			return false;

		_heightMap ??= new float[_width, _height];
		SampleHeightMapCells(cellRegion);
		return true;
	}

	public bool RebuildNavData(Vector2I sectorPosition)
	{
		return RebuildNavData(new Rect2I(sectorPosition, Vector2I.One));
	}

	public bool RebuildNavData(Rect2I sectorRegion)
	{
		if (!Multiplayer.IsServer())
		{
			GD.PushError("[NavGrid.RebuildNavData] Only the server may rebuild navigation data.");
			return false;
		}

		if (!IsNavReady || !TryGetCellRegion(sectorRegion, out Rect2I cellRegion))
			return false;

		ResetWalkability(cellRegion);
		SampleNavDataCells(cellRegion);
		LoadObstacles();

		_flowFields.Clear();
		_navigationPacket = null;
		BuildSectorPortals();
		DrawNavigationState();
		return true;
	}

	public bool TryProjectToTerrain(Vector3 rayOrigin, Vector3 rayDirection, out Vector3 terrainPoint)
	{
		terrainPoint = default;

		if (rayDirection == Vector3.Zero)
			return false;

		Vector3 rayEnd = rayOrigin + rayDirection.Normalized() * _terrainRaycastHeight * 2.0f;
		return TryRaycastTerrain(rayOrigin, rayEnd, out terrainPoint);
	}

	public static List<Vector2I> AssignDestinationCells(
		List<Unit> units,
		List<Vector2I> destCells,
		NavGrid navGrid)
	{
		var availableCells = new List<Vector2I>(destCells);
		var assignments = new List<Vector2I>(units.Count);
		var unitCells = new List<Vector2I>(units.Count);
		var unitOrder = new List<int>(units.Count);
		long unitCellTotalX = 0;
		long unitCellTotalY = 0;
		long destinationCellTotalX = 0;
		long destinationCellTotalY = 0;

		for (int i = 0; i < units.Count; i++)
		{
			Vector2I unitCell = navGrid.WorldToCell(units[i].GetSimWorldPosition());
			unitCells.Add(unitCell);
			unitOrder.Add(i);
			assignments.Add(default);
			unitCellTotalX += unitCell.X;
			unitCellTotalY += unitCell.Y;
		}

		foreach (Vector2I destinationCell in destCells)
		{
			destinationCellTotalX += destinationCell.X;
			destinationCellTotalY += destinationCell.Y;
		}

		unitOrder.Sort((leftIndex, rightIndex) =>
		{
			Vector2I leftCell = unitCells[leftIndex];
			Vector2I rightCell = unitCells[rightIndex];
			long leftOffsetX = (long)leftCell.X * units.Count - unitCellTotalX;
			long leftOffsetY = (long)leftCell.Y * units.Count - unitCellTotalY;
			long rightOffsetX = (long)rightCell.X * units.Count - unitCellTotalX;
			long rightOffsetY = (long)rightCell.Y * units.Count - unitCellTotalY;
			long leftDistanceSquared = leftOffsetX * leftOffsetX + leftOffsetY * leftOffsetY;
			long rightDistanceSquared = rightOffsetX * rightOffsetX + rightOffsetY * rightOffsetY;
			int distanceComparison = rightDistanceSquared.CompareTo(leftDistanceSquared);
			if (distanceComparison != 0)
				return distanceComparison;

			int rowComparison = leftCell.Y.CompareTo(rightCell.Y);
			if (rowComparison != 0)
				return rowComparison;

			int columnComparison = leftCell.X.CompareTo(rightCell.X);
			return columnComparison != 0
				? columnComparison
				: units[leftIndex].UnitID.CompareTo(units[rightIndex].UnitID);
		});

		foreach (int unitIndex in unitOrder)
		{
			Vector2I unitCell = unitCells[unitIndex];
			long unitOffsetX = (long)unitCell.X * units.Count - unitCellTotalX;
			long unitOffsetY = (long)unitCell.Y * units.Count - unitCellTotalY;

			int bestIdx = -1;
			long bestShapeError = long.MaxValue;

			for (int i = 0; i < availableCells.Count; i++)
			{
				Vector2I candidate = availableCells[i];
				long candidateOffsetX =
					(long)candidate.X * destCells.Count - destinationCellTotalX;
				long candidateOffsetY =
					(long)candidate.Y * destCells.Count - destinationCellTotalY;
				long offsetErrorX = candidateOffsetX - unitOffsetX;
				long offsetErrorY = candidateOffsetY - unitOffsetY;
				long shapeError =
					offsetErrorX * offsetErrorX +
					offsetErrorY * offsetErrorY;

				bool winsTie =
					bestIdx < 0 ||
					candidate.Y < availableCells[bestIdx].Y ||
					(candidate.Y == availableCells[bestIdx].Y &&
					 candidate.X < availableCells[bestIdx].X);

				if (shapeError < bestShapeError ||
					(shapeError == bestShapeError && winsTie))
				{
					bestIdx = i;
					bestShapeError = shapeError;
				}
			}

			assignments[unitIndex] = availableCells[bestIdx];
			availableCells.RemoveAt(bestIdx);
		}

		return assignments;
	}

	private void InitNavigation()
	{
		if (Multiplayer.IsServer())
			BuildHostNavData();
		else
		{
			BuildHeightMap(); // Visual Y only
			RpcId(1, nameof(RequestNavData));
		}
	}

	private void BuildHostNavData()
	{
		_heightMap = new float[_width, _height];
		_slopeMap = new ushort[_width * _height];

		LoadObstacles();
		SampleNavDataCells(new Rect2I(Vector2I.Zero, new Vector2I(_width, _height)));

		BuildSectorPortals();
		IsNavReady = true;
		EmitSignal(SignalName.NavigationReady);
		DrawNavigationState();
	}

	private void SampleNavDataCells(Rect2I cellRegion)
	{
		_heightMap ??= new float[_width, _height];
		_slopeMap ??= new ushort[_width * _height];

		int maxSlopeTenths = _maxWalkableSlopeDegrees * 10;
		float fallbackHeight = GetFallbackGroundHeight();
		const int samplesPerAxis = 3;
		float sampleStep = CellSize / (float)samplesPerAxis;
		Vector2I cellRegionEnd = cellRegion.Position + cellRegion.Size;

		for (int x = cellRegion.Position.X; x < cellRegionEnd.X; x++)
		{
			for (int y = cellRegion.Position.Y; y < cellRegionEnd.Y; y++)
			{
				int index = y * _width + x;
				NavCell cell = _cells[x, y];
				float cellMinX = _gridOrigin.X + x * CellSize;
				float cellMinZ = _gridOrigin.Y + y * CellSize;
				ushort maximumCellSlope = 0;
				bool missingTerrain = false;

				_heightMap[x, y] = fallbackHeight;

				for (int sampleX = 0; sampleX < samplesPerAxis; sampleX++)
				{
					for (int sampleY = 0; sampleY < samplesPerAxis; sampleY++)
					{
						float worldX = cellMinX + (sampleX + 0.5f) * sampleStep;
						float worldZ = cellMinZ + (sampleY + 0.5f) * sampleStep;

						if (!TryGetTerrainPoint(
							new Vector2(worldX, worldZ),
							out Vector3 terrainPoint,
							out Vector3 terrainNormal))
						{
							missingTerrain = true;
							continue;
						}

						if (sampleX == samplesPerAxis / 2 && sampleY == samplesPerAxis / 2)
							_heightMap[x, y] = terrainPoint.Y;

						float alignment = Mathf.Clamp(
							Mathf.Abs(terrainNormal.Dot(Vector3.Up)),
							0.0f,
							1.0f
						);
						ushort sampleSlope = (ushort)Mathf.Clamp(
							Mathf.RoundToInt(Mathf.RadToDeg(Mathf.Acos(alignment)) * 10.0f),
							0,
							900
						);

						if (sampleSlope > maximumCellSlope)
							maximumCellSlope = sampleSlope;
					}
				}

				if (missingTerrain)
					maximumCellSlope = 900;

				_slopeMap[index] = maximumCellSlope;
				cell.SlopeTenths = maximumCellSlope;

				if (maximumCellSlope > maxSlopeTenths)
					cell.Walkable = false;
			}
		}
	}

	private Vector2I CellToSectorPosition(Vector2I cellPos)
	{
		int sectorX = cellPos.X / _sectorSize;
		int sectorY = cellPos.Y / _sectorSize;

		return new Vector2I(sectorX, sectorY);
	}

	private NavCell GetCell(Vector2I cellPos)
	{
		bool isInBounds = IsInBounds(cellPos);
		if (!isInBounds)
			return null;

		return _cells[cellPos.X, cellPos.Y];
	}

	private NavSector GetSector(Vector2I sectorPos)
	{
		if (sectorPos.X < 0 || sectorPos.Y < 0)
			return null;

		if (sectorPos.X >= _sectorWidth || sectorPos.Y >= _sectorHeight)
			return null;

		return _sectors[sectorPos.X, sectorPos.Y];
	}

	private NavSector GetSectorForCell(Vector2I cellPos)
	{
		Vector2I sectorPos = CellToSectorPosition(cellPos);
		return GetSector(sectorPos);
	}

	private void InitGrid()
	{
		_cells = new NavCell[_width, _height];

		for (int x = 0; x < _width; x++)
		{
			for (int y = 0; y < _height; y++)
			{
				Vector2I pos = new Vector2I(x, y);
				NavCell cell = new NavCell(pos);
				_cells[x, y] = cell;
			}
		}
	}

	private void InitSectors()
	{
		_sectors = new NavSector[_sectorWidth, _sectorHeight];

		for (int x = 0; x < _sectorWidth; x++)
		{
			for (int y = 0; y < _sectorHeight; y++)
			{
				Vector2I sectorPos = new Vector2I(x, y);
				_sectors[x, y] = CreateSector(sectorPos);
			}
		}
	}

	private void BuildSectorPortals()
	{
		void AddPortalPair(NavSector fromSector, NavSector toSector, Vector2I fromCellPos, Vector2I toCellPos)
		{
			fromSector.Portals.Add(new NavPortal
			{
				FromSector = fromSector.Position,
				ToSector = toSector.Position,
				FromCell = fromCellPos,
				ToCell = toCellPos
			});

			toSector.Portals.Add(new NavPortal
			{
				FromSector = toSector.Position,
				ToSector = fromSector.Position,
				FromCell = toCellPos,
				ToCell = fromCellPos
			});
		}

		foreach (NavSector sector in _sectors)
			sector.Portals.Clear();

		for (int sectorX = 0; sectorX < _sectorWidth; sectorX++)
		{
			for (int sectorY = 0; sectorY < _sectorHeight; sectorY++)
			{
				NavSector sector = _sectors[sectorX, sectorY];

				// EAST
				NavSector sector_E = GetSector(sector.Position + new Vector2I(1, 0));
				if (sector_E != null)
				{
					int leftX = sector.MaxCell.X;
					int rightX = sector_E.MinCell.X;

					int startY = Math.Max(sector.MinCell.Y, sector_E.MinCell.Y);
					int endY = Math.Min(sector.MaxCell.Y, sector_E.MaxCell.Y);

					for (int borderY = startY; borderY <= endY; borderY++)
					{
						Vector2I fromCellPos = new Vector2I(leftX, borderY);
						Vector2I toCellPos = new Vector2I(rightX, borderY);

						if (AreCellsWalkable(fromCellPos, toCellPos))
							AddPortalPair(sector, sector_E, fromCellPos, toCellPos);
					}
				}

				// SOUTH
				NavSector sector_S = GetSector(sector.Position + new Vector2I(0, 1));
				if (sector_S != null)
				{
					int topY = sector.MaxCell.Y;
					int bottomY = sector_S.MinCell.Y;

					int startX = Math.Max(sector.MinCell.X, sector_S.MinCell.X);
					int endX = Math.Min(sector.MaxCell.X, sector_S.MaxCell.X);

					for (int borderX = startX; borderX <= endX; borderX++)
					{
						Vector2I fromCellPos = new Vector2I(borderX, topY);
						Vector2I toCellPos = new Vector2I(borderX, bottomY);

						if (AreCellsWalkable(fromCellPos, toCellPos))
							AddPortalPair(sector, sector_S, fromCellPos, toCellPos);
					}
				}

				// SOUTHEAST
				NavSector sector_SE = GetSector(sector.Position + new Vector2I(1, 1));
				if (sector_SE != null)
				{
					Vector2I fromCellPos = new Vector2I(sector.MaxCell.X, sector.MaxCell.Y);
					Vector2I toCellPos = new Vector2I(sector_SE.MinCell.X, sector_SE.MinCell.Y);

					Vector2I sideCellA = new Vector2I(toCellPos.X, fromCellPos.Y);
					Vector2I sideCellB = new Vector2I(fromCellPos.X, toCellPos.Y);

					if (AreCellsWalkable(fromCellPos, toCellPos, sideCellA, sideCellB))
						AddPortalPair(sector, sector_SE, fromCellPos, toCellPos);
				}

				// SOUTHWEST
				NavSector sector_SW = GetSector(sector.Position + new Vector2I(-1, 1));
				if (sector_SW != null)
				{
					Vector2I fromCellPos = new Vector2I(sector.MinCell.X, sector.MaxCell.Y);
					Vector2I toCellPos = new Vector2I(sector_SW.MaxCell.X, sector_SW.MinCell.Y);

					Vector2I sideCellA = new Vector2I(toCellPos.X, fromCellPos.Y);
					Vector2I sideCellB = new Vector2I(fromCellPos.X, toCellPos.Y);

					if (AreCellsWalkable(fromCellPos, toCellPos, sideCellA, sideCellB))
						AddPortalPair(sector, sector_SW, fromCellPos, toCellPos);
				}
			}
		}
	}

	private void BuildHeightMap()
	{
		_heightMap = new float[_width, _height];
		SampleHeightMapCells(new Rect2I(Vector2I.Zero, new Vector2I(_width, _height)));
	}

	private void SampleHeightMapCells(Rect2I cellRegion)
	{
		Vector2I cellRegionEnd = cellRegion.Position + cellRegion.Size;

		for (int x = cellRegion.Position.X; x < cellRegionEnd.X; x++)
		{
			for (int y = cellRegion.Position.Y; y < cellRegionEnd.Y; y++)
			{
				float worldX = _gridOrigin.X + x * CellSize + CellSize / 2.0f;
				float worldZ = _gridOrigin.Y + y * CellSize + CellSize / 2.0f;

				_heightMap[x, y] = GetTerrainHeightByRayCast(new Vector2(worldX, worldZ));
			}
		}
	}

	private float GetTerrainHeightByRayCast(Vector2 worldPos)
	{
		if (TryGetTerrainPoint(worldPos, out Vector3 terrainPoint))
			return terrainPoint.Y;

		return GetFallbackGroundHeight();
	}

	private int GetPortalMoveCost(NavPortal portal)
	{
		Vector2I delta = portal.ToSector - portal.FromSector;

		bool isDiagonal = delta.X != 0 && delta.Y != 0;
		return isDiagonal ? _diagonalCost : _straightCost;
	}

	private bool AreCellsWalkable(params Vector2I[] cellPositions)
	{
		foreach (Vector2I cellPos in cellPositions)
		{
			NavCell cell = GetCell(cellPos);

			if (cell == null || !cell.Walkable)
				return false;
		}

		return true;
	}

	private HashSet<Vector2I> BuildAllowedSectorSet(
		List<Vector2I> startCellPositions,
		Vector2I targetCellPos,
		Rect2I targetSectorRegion)
	{
		var allowedSectors = new HashSet<Vector2I>();
		var representativeStartCellBySector = new Dictionary<Vector2I, Vector2I>();

		Vector2I targetRegionEnd = targetSectorRegion.Position + targetSectorRegion.Size;
		for (int sectorX = targetSectorRegion.Position.X; sectorX < targetRegionEnd.X; sectorX++)
		{
			for (int sectorY = targetSectorRegion.Position.Y; sectorY < targetRegionEnd.Y; sectorY++)
			{
				NavSector targetSector = GetSector(new Vector2I(sectorX, sectorY));
				if (targetSector != null)
					allowedSectors.Add(targetSector.Position);
			}
		}

		foreach (Vector2I startCellPos in startCellPositions)
		{
			NavSector startSector = GetSectorForCell(startCellPos);
			if (startSector == null)
				continue;

			allowedSectors.Add(startSector.Position);

			if (!representativeStartCellBySector.ContainsKey(startSector.Position))
				representativeStartCellBySector[startSector.Position] = startCellPos;
		}

		foreach (Vector2I representativeStartCell in representativeStartCellBySector.Values)
		{
			List<NavPortal> sectorPath = FindSectorPortalPath(representativeStartCell, targetCellPos);

			foreach (NavPortal portal in sectorPath)
			{
				allowedSectors.Add(portal.FromSector);
				allowedSectors.Add(portal.ToSector);

				Vector2I sectorDelta = portal.ToSector - portal.FromSector;
				if (sectorDelta.X != 0 && sectorDelta.Y != 0)
				{
					Vector2I horizontalBridge =
						portal.FromSector + new Vector2I(sectorDelta.X, 0);
					Vector2I verticalBridge =
						portal.FromSector + new Vector2I(0, sectorDelta.Y);

					if (GetSector(horizontalBridge) != null)
						allowedSectors.Add(horizontalBridge);

					if (GetSector(verticalBridge) != null)
						allowedSectors.Add(verticalBridge);
				}
			}
		}

		return allowedSectors;
	}

	private List<NavPortal> FindSectorPortalPath(Vector2I startCellPos, Vector2I targetCellPos)
	{
		NavSector startSector = GetSectorForCell(startCellPos);
		NavSector targetSector = GetSectorForCell(targetCellPos);

		if (startSector == null || targetSector == null)
			return new List<NavPortal>();

		if (startSector.Position == targetSector.Position)
			return new List<NavPortal>();

		var frontier = new PriorityQueue<NavSector, int>();
		var costSoFar = new Dictionary<Vector2I, int>();
		var cameFromPortal = new Dictionary<Vector2I, NavPortal>();

		frontier.Enqueue(startSector, 0);
		costSoFar[startSector.Position] = 0;

		while (frontier.Count > 0)
		{
			NavSector currentSector = frontier.Dequeue();

			if (currentSector.Position == targetSector.Position)
				break;

			int currentCost = costSoFar[currentSector.Position];

			foreach (NavPortal portal in currentSector.Portals)
			{
				NavSector nextSector = GetSector(portal.ToSector);
				if (nextSector == null)
					continue;

				int newCost = currentCost + GetPortalMoveCost(portal);

				if (costSoFar.TryGetValue(nextSector.Position, out int existingCost) && existingCost <= newCost)
					continue;

				costSoFar[nextSector.Position] = newCost;
				cameFromPortal[nextSector.Position] = portal;
				frontier.Enqueue(nextSector, newCost);
			}
		}

		if (!costSoFar.ContainsKey(targetSector.Position))
			return new List<NavPortal>();

		var path = new List<NavPortal>();
		Vector2I current = targetSector.Position;

		while (current != startSector.Position)
		{
			NavPortal portal = cameFromPortal[current];
			path.Add(portal);
			current = portal.FromSector;
		}

		path.Reverse();
		return path;
	}

	private NavFlowField BuildFlowField(
		HashSet<Vector2I> allowedSectors,
		Vector2I targetPos,
		Rect2I targetSectorRegion)
	{
		Vector2I[,] directions = new Vector2I[_width, _height];
		int[,] integrationCosts = new int[_width, _height];

		foreach (NavCell cell in _cells)
		{
			Vector2I direction = Vector2I.Zero;

			NavSector sector = GetSectorForCell(cell.Position);
			if (sector != null && allowedSectors.Contains(sector.Position) && cell.Walkable)
			{
				NavCell[] neighbors = GetNeighbors(cell.Position);

				NavCell bestNeighbor = null;
				int bestCost = cell.IntegrationCost;

				foreach (NavCell neighbor in neighbors)
				{
					if (neighbor.IntegrationCost < bestCost)
					{
						bestNeighbor = neighbor;
						bestCost = neighbor.IntegrationCost;
					}
				}

				if (bestNeighbor != null)
					direction = bestNeighbor.Position - cell.Position;
			}

			directions[cell.Position.X, cell.Position.Y] = direction;
			integrationCosts[cell.Position.X, cell.Position.Y] = cell.IntegrationCost;

			Vector2I debugDirection = direction;
			if (debugDirection == Vector2I.Zero &&
				cell.Walkable &&
				sector != null &&
				targetSectorRegion.HasPoint(sector.Position) &&
				cell.Position != targetPos)
			{
				Vector2I targetOffset = targetPos - cell.Position;
				debugDirection = new Vector2I(
					Math.Sign(targetOffset.X),
					Math.Sign(targetOffset.Y)
				);
			}

			// Temporary: keep the debug overlay showing the most recently built field.
			cell.Direction = debugDirection;
		}

		return new NavFlowField(targetPos, directions, integrationCosts, allowedSectors);
	}

	private bool TryExpandFlowFieldToSector(NavFlowField flowField, NavSector sector)
	{
		var frontier = new PriorityQueue<NavCell, int>();
		bool hasBoundarySeed = false;

		for (int x = sector.MinCell.X; x <= sector.MaxCell.X; x++)
		{
			for (int y = sector.MinCell.Y; y <= sector.MaxCell.Y; y++)
			{
				NavCell cell = GetCell(new Vector2I(x, y));
				if (cell == null || !cell.Walkable)
					continue;

				int bestCost = NavCell.MaxIntegrationCost;
				foreach (NavCell neighbor in GetNeighbors(cell.Position))
				{
					NavSector neighborSector = GetSectorForCell(neighbor.Position);
					if (neighborSector == null ||
						neighborSector.Position == sector.Position ||
						!flowField.IsSectorCalculated(neighborSector.Position))
					{
						continue;
					}

					int neighborCost = flowField.GetIntegrationCost(neighbor.Position);
					if (neighborCost >= NavCell.MaxIntegrationCost)
						continue;

					int candidateCost =
						neighborCost + GetMoveCost(neighbor.Position, cell.Position) * cell.Cost;
					bestCost = Math.Min(bestCost, candidateCost);
				}

				if (bestCost >= NavCell.MaxIntegrationCost)
					continue;

				flowField.SetIntegrationCost(cell.Position, bestCost);
				frontier.Enqueue(cell, bestCost);
				hasBoundarySeed = true;
			}
		}

		if (!hasBoundarySeed)
			return false;

		while (frontier.TryDequeue(out NavCell currentCell, out int queuedCost))
		{
			int currentCost = flowField.GetIntegrationCost(currentCell.Position);
			if (queuedCost != currentCost)
				continue;

			foreach (NavCell neighbor in GetNeighbors(currentCell.Position))
			{
				NavSector neighborSector = GetSectorForCell(neighbor.Position);
				if (neighborSector == null || neighborSector.Position != sector.Position)
					continue;

				int candidateCost =
					currentCost + GetMoveCost(currentCell.Position, neighbor.Position) * neighbor.Cost;
				if (candidateCost >= flowField.GetIntegrationCost(neighbor.Position))
					continue;

				flowField.SetIntegrationCost(neighbor.Position, candidateCost);
				frontier.Enqueue(neighbor, candidateCost);
			}
		}

		flowField.AddCalculatedSector(sector.Position);

		for (int x = sector.MinCell.X; x <= sector.MaxCell.X; x++)
		{
			for (int y = sector.MinCell.Y; y <= sector.MaxCell.Y; y++)
			{
				NavCell cell = GetCell(new Vector2I(x, y));
				if (cell == null || !cell.Walkable)
					continue;

				int bestCost = flowField.GetIntegrationCost(cell.Position);
				Vector2I direction = Vector2I.Zero;

				foreach (NavCell neighbor in GetNeighbors(cell.Position))
				{
					NavSector neighborSector = GetSectorForCell(neighbor.Position);
					if (neighborSector == null || !flowField.IsSectorCalculated(neighborSector.Position))
						continue;

					int neighborCost = flowField.GetIntegrationCost(neighbor.Position);
					if (neighborCost < bestCost)
					{
						bestCost = neighborCost;
						direction = neighbor.Position - cell.Position;
					}
				}

				flowField.SetDirection(cell.Position, direction);
			}
		}

		return true;
	}

	private bool BuildIntegrationField(Rect2I targetSectorRegion, HashSet<Vector2I> allowedSectors)
	{
		ResetIntegrationCosts();

		var queue = new Queue<NavCell>();
		Vector2I targetRegionEnd = targetSectorRegion.Position + targetSectorRegion.Size;

		for (int sectorX = targetSectorRegion.Position.X; sectorX < targetRegionEnd.X; sectorX++)
		{
			for (int sectorY = targetSectorRegion.Position.Y; sectorY < targetRegionEnd.Y; sectorY++)
			{
				NavSector targetSector = GetSector(new Vector2I(sectorX, sectorY));
				if (targetSector == null || !allowedSectors.Contains(targetSector.Position))
					continue;

				for (int cellX = targetSector.MinCell.X; cellX <= targetSector.MaxCell.X; cellX++)
				{
					for (int cellY = targetSector.MinCell.Y; cellY <= targetSector.MaxCell.Y; cellY++)
					{
						NavCell targetCell = GetCell(new Vector2I(cellX, cellY));
						if (targetCell == null || !targetCell.Walkable)
							continue;

						targetCell.IntegrationCost = 0;
						queue.Enqueue(targetCell);
					}
				}
			}
		}

		if (queue.Count == 0)
			return false;

		while (queue.Count > 0)
		{
			NavCell currentCell = queue.Dequeue();
			NavCell[] neighbors = GetNeighbors(currentCell.Position);

			foreach (NavCell neighborCell in neighbors)
			{
				NavSector neighborSector = GetSectorForCell(neighborCell.Position);
				if (neighborSector == null || !allowedSectors.Contains(neighborSector.Position))
					continue;

				int moveCost = GetMoveCost(currentCell.Position, neighborCell.Position);
				int newIntegrationCost = currentCell.IntegrationCost + moveCost * neighborCell.Cost;

				if (newIntegrationCost < neighborCell.IntegrationCost)
				{
					neighborCell.IntegrationCost = newIntegrationCost;
					queue.Enqueue(neighborCell);
				}
			}
		}

		return true;
	}

	private bool AreStartCellsReachable(List<Vector2I> startPositions)
	{
		foreach (Vector2I startPos in startPositions)
		{
			NavCell startCell = GetCell(startPos);

			if (startCell == null)
				return false;

			if (startCell.IntegrationCost >= NavCell.MaxIntegrationCost)
				return false;
		}

		return true;
	}

	private HashSet<Vector2I> ExpandAllowedSectors(HashSet<Vector2I> baseSectors, int radius)
	{
		var expandedSectors = new HashSet<Vector2I>();

		foreach (Vector2I sectorPos in baseSectors)
		{
			for (int offsetX = -radius; offsetX <= radius; offsetX++)
			{
				for (int offsetY = -radius; offsetY <= radius; offsetY++)
				{
					Vector2I expandedSectorPos = sectorPos + new Vector2I(offsetX, offsetY);

					if (GetSector(expandedSectorPos) != null)
						expandedSectors.Add(expandedSectorPos);
				}
			}
		}

		return expandedSectors;
	}

	private HashSet<Vector2I> BuildAllSectorSet()
	{
		var sectors = new HashSet<Vector2I>();

		foreach (NavSector sector in _sectors)
			sectors.Add(sector.Position);

		return sectors;
	}

	private void LoadObstacles()
	{
		foreach (Node node in GetTree().GetNodesInGroup("nav_obstacles"))
		{
			var meshes = new List<MeshInstance3D>();
			GetMeshes(node, meshes);

			foreach (var mesh in meshes)
				MarkMeshUnwalkable(mesh);
		}
	}

	private void MarkMeshUnwalkable(MeshInstance3D mesh)
	{
		Aabb aabb = mesh.GetAabb();

		Vector3 min = aabb.Position;
		Vector3 max = aabb.End;

		Vector3[] corners =
		[
			new Vector3(min.X, min.Y, min.Z),
			new Vector3(max.X, min.Y, min.Z),
			new Vector3(min.X, max.Y, min.Z),
			new Vector3(max.X, max.Y, min.Z),

			new Vector3(min.X, min.Y, max.Z),
			new Vector3(max.X, min.Y, max.Z),
			new Vector3(min.X, max.Y, max.Z),
			new Vector3(max.X, max.Y, max.Z),
		];

		Vector2[] footprint = GetMeshWorldFootprint(mesh, corners);
		if (footprint.Length < 3)
			return;

		float minWorldX = float.PositiveInfinity;
		float maxWorldX = float.NegativeInfinity;
		float minWorldZ = float.PositiveInfinity;
		float maxWorldZ = float.NegativeInfinity;

		foreach (Vector2 point in footprint)
		{
			minWorldX = Mathf.Min(minWorldX, point.X);
			maxWorldX = Mathf.Max(maxWorldX, point.X);
			minWorldZ = Mathf.Min(minWorldZ, point.Y);
			maxWorldZ = Mathf.Max(maxWorldZ, point.Y);
		}

		const float maxCellEpsilon = 0.001f;
		const int obstacleBorderInCells = 1;
		Vector2I minCell = WorldToCell(new Vector3(minWorldX, 0, minWorldZ));
		Vector2I maxCell = WorldToCell(new Vector3(maxWorldX - maxCellEpsilon, 0, maxWorldZ - maxCellEpsilon));

		for (int x = minCell.X; x <= maxCell.X; x++)
		{
			for (int y = minCell.Y; y <= maxCell.Y; y++)
			{
				Vector2I cellPos = new Vector2I(x, y);
				if (!CellOverlapsFootprint(cellPos, footprint))
					continue;

				for (int offsetX = -obstacleBorderInCells; offsetX <= obstacleBorderInCells; offsetX++)
				{
					for (int offsetY = -obstacleBorderInCells; offsetY <= obstacleBorderInCells; offsetY++)
						SetWalkable(cellPos + new Vector2I(offsetX, offsetY), false);
				}
			}
		}
	}

	private Vector2[] GetMeshWorldFootprint(MeshInstance3D mesh, Vector3[] localCorners)
	{
		var points = new List<Vector2>();

		foreach (Vector3 corner in localCorners)
		{
			Vector3 worldCorner = mesh.GlobalTransform * corner;
			AddUniquePoint(points, new Vector2(worldCorner.X, worldCorner.Z));
		}

		if (points.Count < 3)
			return points.ToArray();

		Vector2 center = Vector2.Zero;
		foreach (Vector2 point in points)
			center += point;

		center /= points.Count;
		points.Sort((a, b) => MathF.Atan2(a.Y - center.Y, a.X - center.X).CompareTo(MathF.Atan2(b.Y - center.Y, b.X - center.X)));

		return points.ToArray();
	}

	private static void AddUniquePoint(List<Vector2> points, Vector2 point)
	{
		const float pointEpsilon = 0.001f;
		float epsilonSquared = pointEpsilon * pointEpsilon;

		foreach (Vector2 existingPoint in points)
		{
			if ((existingPoint - point).LengthSquared() <= epsilonSquared)
				return;
		}

		points.Add(point);
	}

	private bool CellOverlapsFootprint(Vector2I cellPos, Vector2[] footprint)
	{
		float left = _gridOrigin.X + cellPos.X * CellSize;
		float right = left + CellSize;
		float top = _gridOrigin.Y + cellPos.Y * CellSize;
		float bottom = top + CellSize;

		Vector2[] cellCorners =
		[
			new Vector2(left, top),
			new Vector2(right, top),
			new Vector2(right, bottom),
			new Vector2(left, bottom),
		];

		Vector2[] first = cellCorners;
		Vector2[] second = footprint;

		return !HasSeparatingAxis(first, first, second) && !HasSeparatingAxis(second, first, second);
	}

	private static bool HasSeparatingAxis(Vector2[] axisSource, Vector2[] first, Vector2[] second)
	{
		for (int i = 0; i < axisSource.Length; i++)
		{
			Vector2 start = axisSource[i];
			Vector2 end = axisSource[(i + 1) % axisSource.Length];
			Vector2 edge = end - start;

			if (edge.LengthSquared() <= 0.000001f)
				continue;

			Vector2 axis = new Vector2(-edge.Y, edge.X).Normalized();

			ProjectPolygon(first, axis, out float firstMin, out float firstMax);
			ProjectPolygon(second, axis, out float secondMin, out float secondMax);

			if (firstMax < secondMin || secondMax < firstMin)
				return true;
		}

		return false;
	}

	private static void ProjectPolygon(Vector2[] polygon, Vector2 axis, out float min, out float max)
	{
		float firstProjection = polygon[0].Dot(axis);
		min = firstProjection;
		max = firstProjection;

		for (int i = 1; i < polygon.Length; i++)
		{
			float projection = polygon[i].Dot(axis);
			min = Mathf.Min(min, projection);
			max = Mathf.Max(max, projection);
		}
	}

	private void GetMeshes(Node node, List<MeshInstance3D> meshes)
	{
		if (node is MeshInstance3D mesh)
			meshes.Add(mesh);

		foreach (Node child in node.GetChildren())
			GetMeshes(child, meshes);
	}

	private NavCell[] GetNeighbors(Vector2I cellPos)
	{
		bool isInBounds = IsInBounds(cellPos);
		if (!isInBounds)
			return Array.Empty<NavCell>();

		var neighbors = new List<NavCell>();

		foreach (Vector2I offset in _offsets)
		{
			NavCell cell = GetCell(cellPos + offset);
			if (cell != null && cell.Walkable)
				neighbors.Add(cell);
		}

		return neighbors.ToArray();
	}

	private int GetMoveCost(Vector2I from, Vector2I to)
	{
		Vector2I delta = to - from;

		bool isDiagonal = delta.X != 0 && delta.Y != 0;
		return isDiagonal ? _diagonalCost : _straightCost;
	}

	private bool TryGetTerrainPoint(Vector2 worldPos, out Vector3 terrainPoint)
	{
		return TryGetTerrainPoint(worldPos, out terrainPoint, out _);
	}

	private bool TryGetTerrainPoint(Vector2 worldPos, out Vector3 terrainPoint, out Vector3 terrainNormal)
	{
		float fallbackHeight = GetFallbackGroundHeight();

		Vector3 from = new(
			worldPos.X,
			fallbackHeight + _terrainRaycastHeight,
			worldPos.Y
		);

		Vector3 to = new(
			worldPos.X,
			fallbackHeight - _terrainRaycastHeight,
			worldPos.Y
		);

		return TryRaycastTerrain(from, to, out terrainPoint, out terrainNormal);
	}

	private bool TryRaycastTerrain(
		Vector3 from,
		Vector3 to,
		out Vector3 terrainPoint)
	{
		return TryRaycastTerrain(from, to, out terrainPoint, out _);
	}

	private bool TryRaycastTerrain(Vector3 from, Vector3 to, out Vector3 terrainPoint, out Vector3 terrainNormal)
	{
		terrainPoint = default;
		terrainNormal = Vector3.Up;

		World3D world = GetViewport()?.World3D;
		if (world == null)
			return false;

		PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(from, to);
		query.CollideWithAreas = false;
		query.CollideWithBodies = true;
		query.CollisionMask = _terrainCollisionMask;

		Godot.Collections.Dictionary result = world.DirectSpaceState.IntersectRay(query);
		if (result.Count == 0)
			return false;

		terrainPoint = (Vector3)result["position"];
		terrainNormal = ((Vector3)result["normal"]).Normalized();

		return true;
	}

	private float GetFallbackGroundHeight()
	{
		Node currentScene = GetTree()?.CurrentScene;
		return currentScene?.GetNodeOrNull<Node3D>("%Ground")?.GlobalPosition.Y ?? 0.0f;
	}

	private bool TryGetCellRegion(Rect2I sectorRegion, out Rect2I cellRegion)
	{
		cellRegion = default;
		if (sectorRegion.Size.X <= 0 || sectorRegion.Size.Y <= 0)
			return false;

		Vector2I sectorRegionEnd = sectorRegion.Position + sectorRegion.Size;
		int minSectorX = Math.Max(0, sectorRegion.Position.X);
		int minSectorY = Math.Max(0, sectorRegion.Position.Y);
		int maxSectorX = Math.Min(_sectorWidth, sectorRegionEnd.X) - 1;
		int maxSectorY = Math.Min(_sectorHeight, sectorRegionEnd.Y) - 1;

		if (minSectorX > maxSectorX || minSectorY > maxSectorY)
			return false;

		NavSector minSector = _sectors[minSectorX, minSectorY];
		NavSector maxSector = _sectors[maxSectorX, maxSectorY];
		cellRegion = new Rect2I(
			minSector.MinCell,
			maxSector.MaxCell - minSector.MinCell + Vector2I.One
		);
		return true;
	}

	private void ResetWalkability(Rect2I cellRegion)
	{
		Vector2I cellRegionEnd = cellRegion.Position + cellRegion.Size;

		for (int x = cellRegion.Position.X; x < cellRegionEnd.X; x++)
		{
			for (int y = cellRegion.Position.Y; y < cellRegionEnd.Y; y++)
				_cells[x, y].Walkable = true;
		}
	}

	private void DrawNavigationState()
	{
		if (!_drawNavGrid)
			return;

		_debugRenderer.DrawBlockedCells(this, _gridOrigin, CellSize, _cells);
		_debugRenderer.DrawPortalCells(this, _gridOrigin, _width, _height, CellSize, _sectors);
	}

	private void ResetIntegrationCosts()
	{
		foreach (NavCell cell in _cells)
			cell.ResetIntegrationCost();
	}

	private NavSector CreateSector(Vector2I sectorPos)
	{
		int minCellX = sectorPos.X * _sectorSize;
		int minCellY = sectorPos.Y * _sectorSize;

		int maxCellX = minCellX + _sectorSize - 1;
		int maxCellY = minCellY + _sectorSize - 1;
		maxCellX = Math.Min(maxCellX, _width - 1);
		maxCellY = Math.Min(maxCellY, _height - 1);

		return new NavSector(
			sectorPos,
			new Vector2I(minCellX, minCellY),
			new Vector2I(maxCellX, maxCellY)
		);
	}

	private bool IsInBounds(Vector2I cellPos)
	{
		int x = cellPos.X;
		int y = cellPos.Y;

		if (x >= _width || y >= _height || x < 0 || y < 0)
			return false;

		return true;
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer)]
	private void RequestNavData()
	{
	}
}
