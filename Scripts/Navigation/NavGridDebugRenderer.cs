using System;
using Godot;

public class NavGridDebugRenderer
{
    private const string ShaderPath = "res://Shaders/NavGridDebug.gdshader";
    private const float OverlayY = 0.08f;
    private const float GridLineWidthWorld = 0.06f;
    private const float MarkerLineWidthWorld = 0.125f;
    private const string GroundNodePath = "%Ground";

    private static readonly Color NeutralDataColor = new(0.0f, 0.5f, 0.5f, 0.0f);

    private Color _gridColor = new(1.0f, 1.0f, 1.0f, 0.35f);
    private Color _blockedColor = new(1.0f, 0.05f, 0.05f, 0.9f);
    private Color _flowColor = new(0.1f, 0.75f, 1.0f, 0.9f);
    private Color _targetColor = new(1.0f, 0.9f, 0.1f, 1.0f);

    private MeshInstance3D _debugMesh;
    private ShaderMaterial _debugMaterial;
    private ImageTexture _navDataTexture;
    private Vector2I _navDataTextureSize;
    private Vector2 _meshSize;
    private Mesh _overlayMeshSource;
    private Node _parent;
    private Vector2I _gridOrigin;
    private int _width;
    private int _height;
    private int _cellSize;
    private NavCell[,] _cells;
    private Vector2I? _targetCell;
    private bool _gridLinesVisible;
    private bool _blockedCellsVisible;
    private bool _flowArrowsVisible;
    private bool _targetVisible;

    public void SetColors(Color gridColor, Color blockedColor, Color flowColor, Color targetColor)
    {
        if (_gridColor == gridColor && _blockedColor == blockedColor && _flowColor == flowColor && _targetColor == targetColor)
            return;

        _gridColor = gridColor;
        _blockedColor = blockedColor;
        _flowColor = flowColor;
        _targetColor = targetColor;

        UpdateShaderColors();
    }

    public void DrawGrid(Node parent, Vector2I gridOrigin, int width, int height, int cellSize, NavCell[,] cells, Vector2I? targetCell = null)
    {
        SetGridMetrics(gridOrigin, width, height, cellSize);
        _cells = cells;
        _targetCell = targetCell;
        _gridLinesVisible = true;
        _blockedCellsVisible = true;
        _flowArrowsVisible = true;
        _targetVisible = targetCell.HasValue;

        RebuildOverlay(parent);
    }

    public void DrawGridLines(Node parent, Vector2I gridOrigin, int width, int height, int cellSize)
    {
        SetGridMetrics(gridOrigin, width, height, cellSize);
        _gridLinesVisible = true;

        RebuildOverlay(parent);
    }

    public void DrawBlockedCells(Node parent, Vector2I gridOrigin, int cellSize, NavCell[,] cells)
    {
        SetCellGridMetrics(gridOrigin, cellSize, cells);
        _cells = cells;
        _blockedCellsVisible = true;

        RebuildOverlay(parent);
    }

    public void DrawFlowArrows(Node parent, Vector2I gridOrigin, int cellSize, NavCell[,] cells)
    {
        SetCellGridMetrics(gridOrigin, cellSize, cells);
        _cells = cells;
        _flowArrowsVisible = true;

        RebuildOverlay(parent);
    }

    public void DrawTarget(Node parent, Vector2I gridOrigin, int cellSize, Vector2I? targetCell)
    {
        _gridOrigin = gridOrigin;
        _cellSize = Math.Max(1, cellSize);
        _targetCell = targetCell;
        _targetVisible = targetCell.HasValue;

        RebuildOverlay(parent);
    }

    public void ClearGrid()
    {
        _gridLinesVisible = false;
        _blockedCellsVisible = false;
        _flowArrowsVisible = false;
        _targetVisible = false;
        _targetCell = null;

        ClearOverlay();
    }

    public void ClearGridLines()
    {
        _gridLinesVisible = false;
        RebuildOverlay();
    }

    public void ClearBlockedCells()
    {
        _blockedCellsVisible = false;
        RebuildOverlay();
    }

    public void ClearFlowArrows()
    {
        _flowArrowsVisible = false;
        RebuildOverlay();
    }

    public void ClearTarget()
    {
        _targetVisible = false;
        _targetCell = null;
        RebuildOverlay();
    }

    private void SetGridMetrics(Vector2I gridOrigin, int width, int height, int cellSize)
    {
        _gridOrigin = gridOrigin;
        _width = Math.Max(0, width);
        _height = Math.Max(0, height);
        _cellSize = Math.Max(1, cellSize);
    }

    private void SetCellGridMetrics(Vector2I gridOrigin, int cellSize, NavCell[,] cells)
    {
        int width = cells?.GetLength(0) ?? _width;
        int height = cells?.GetLength(1) ?? _height;
        SetGridMetrics(gridOrigin, width, height, cellSize);
    }

    private void RebuildOverlay(Node parent = null)
    {
        if (parent != null)
            _parent = parent;

        if (!HasVisibleLayer())
        {
            ClearOverlay();
            return;
        }

        if (_width <= 0 || _height <= 0 || _cellSize <= 0)
            return;

        if (_parent == null || !GodotObject.IsInstanceValid(_parent))
            return;

        EnsureOverlay(_parent);
        if (_debugMesh == null || _debugMaterial == null)
            return;

        UpdateOverlayMesh();
        UpdateDataTexture();
        UpdateShaderParameters();
    }

    private bool HasVisibleLayer()
    {
        return _gridLinesVisible || _blockedCellsVisible || _flowArrowsVisible || _targetVisible;
    }

    private void EnsureOverlay(Node parent)
    {
        if (_debugMesh != null && GodotObject.IsInstanceValid(_debugMesh) && _debugMaterial != null)
            return;

        Shader shader = GD.Load<Shader>(ShaderPath);
        if (shader == null)
        {
            GD.PushError($"NavGridDebugRenderer: Could not load shader at {ShaderPath}");
            return;
        }

        _debugMaterial = new ShaderMaterial
        {
            Shader = shader,
            RenderPriority = 100
        };

        _debugMesh = new MeshInstance3D
        {
            Name = "NavGridDebugShaderOverlay",
            TopLevel = true,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = _debugMaterial
        };

        parent.AddChild(_debugMesh);
    }

    private void UpdateOverlayMesh()
    {
        MeshInstance3D groundMesh = ResolveGroundMesh();
        if (groundMesh?.Mesh != null)
        {
            UpdateGroundShapedOverlayMesh(groundMesh);
            return;
        }

        UpdateFlatOverlayMesh();
    }

    private void UpdateGroundShapedOverlayMesh(MeshInstance3D groundMesh)
    {
        if (_overlayMeshSource != groundMesh.Mesh)
        {
            _debugMesh.Mesh = groundMesh.Mesh;
            _overlayMeshSource = groundMesh.Mesh;
            _meshSize = Vector2.Zero;
        }

        Transform3D groundTransform = groundMesh.GlobalTransform;
        groundTransform.Origin += Vector3.Up * OverlayY;
        _debugMesh.GlobalTransform = groundTransform;
        _debugMesh.Visible = true;
    }

    private void UpdateFlatOverlayMesh()
    {
        float worldWidth = _width * _cellSize;
        float worldHeight = _height * _cellSize;
        float centerX = _gridOrigin.X + worldWidth * 0.5f;
        float centerZ = _gridOrigin.Y + worldHeight * 0.5f;
        Vector2 meshSize = new(worldWidth, worldHeight);

        if (_debugMesh.Mesh is not PlaneMesh || _meshSize != meshSize)
        {
            _debugMesh.Mesh = new PlaneMesh
            {
                Size = meshSize
            };

            _meshSize = meshSize;
            _overlayMeshSource = _debugMesh.Mesh;
        }

        _debugMesh.GlobalTransform = new Transform3D(Basis.Identity, new Vector3(centerX, OverlayY, centerZ));
        _debugMesh.Visible = true;
    }

    private MeshInstance3D ResolveGroundMesh()
    {
        if (_parent == null || !GodotObject.IsInstanceValid(_parent))
            return null;

        Node currentScene = _parent.GetTree()?.CurrentScene;
        return currentScene?.GetNodeOrNull<MeshInstance3D>(GroundNodePath);
    }

    private void UpdateDataTexture()
    {
        Vector2I textureSize = new(Math.Max(1, _width), Math.Max(1, _height));
        Image dataImage = Image.CreateEmpty(textureSize.X, textureSize.Y, false, Image.Format.Rgba8);
        dataImage.Fill(NeutralDataColor);

        if (_cells != null)
        {
            foreach (NavCell cell in _cells)
            {
                if (!IsCellInTexture(cell.Position))
                    continue;

                Color data = EncodeCellData(cell);
                dataImage.SetPixel(cell.Position.X, cell.Position.Y, data);
            }
        }

        if (_targetVisible && _targetCell.HasValue && IsCellInTexture(_targetCell.Value))
        {
            Color data = dataImage.GetPixel(_targetCell.Value.X, _targetCell.Value.Y);
            data.A = 1.0f;
            dataImage.SetPixel(_targetCell.Value.X, _targetCell.Value.Y, data);
        }

        if (_navDataTexture == null || _navDataTextureSize != textureSize)
        {
            _navDataTexture = ImageTexture.CreateFromImage(dataImage);
            _navDataTextureSize = textureSize;
            _debugMaterial.SetShaderParameter("nav_data", _navDataTexture);
            return;
        }

        _navDataTexture.Update(dataImage);
    }

    private Color EncodeCellData(NavCell cell)
    {
        float blocked = cell.Walkable ? 0.0f : 1.0f;
        Vector2I direction = ClampDirection(cell.Direction);

        return new Color(
            blocked,
            (direction.X + 1) * 0.5f,
            (direction.Y + 1) * 0.5f,
            0.0f
        );
    }

    private static Vector2I ClampDirection(Vector2I direction)
    {
        return new Vector2I(
            direction.X < 0 ? -1 : direction.X > 0 ? 1 : 0,
            direction.Y < 0 ? -1 : direction.Y > 0 ? 1 : 0
        );
    }

    private bool IsCellInTexture(Vector2I cell)
    {
        return cell.X >= 0 && cell.Y >= 0 && cell.X < _width && cell.Y < _height;
    }

    private void UpdateShaderParameters()
    {
        float worldWidth = Math.Max(1, _width * _cellSize);
        float worldHeight = Math.Max(1, _height * _cellSize);

        _debugMaterial.SetShaderParameter("cell_count", new Vector2(Math.Max(1, _width), Math.Max(1, _height)));
        _debugMaterial.SetShaderParameter("grid_origin", new Vector2(_gridOrigin.X, _gridOrigin.Y));
        _debugMaterial.SetShaderParameter("grid_world_size", new Vector2(worldWidth, worldHeight));
        _debugMaterial.SetShaderParameter("show_grid", _gridLinesVisible);
        _debugMaterial.SetShaderParameter("show_blocked", _blockedCellsVisible);
        _debugMaterial.SetShaderParameter("show_flow", _flowArrowsVisible);
        _debugMaterial.SetShaderParameter("show_target", _targetVisible);
        UpdateShaderColors();
        _debugMaterial.SetShaderParameter("grid_line_width", GridLineWidthWorld / _cellSize);
        _debugMaterial.SetShaderParameter("marker_line_width", MarkerLineWidthWorld / _cellSize);
    }

    private void UpdateShaderColors()
    {
        if (_debugMaterial == null)
            return;

        _debugMaterial.SetShaderParameter("grid_color", _gridColor);
        _debugMaterial.SetShaderParameter("blocked_color", _blockedColor);
        _debugMaterial.SetShaderParameter("flow_color", _flowColor);
        _debugMaterial.SetShaderParameter("target_color", _targetColor);
    }

    private void ClearOverlay()
    {
        _debugMaterial = null;
        _navDataTexture = null;
        _navDataTextureSize = Vector2I.Zero;
        _meshSize = Vector2.Zero;
        _overlayMeshSource = null;

        if (_debugMesh == null || !GodotObject.IsInstanceValid(_debugMesh))
        {
            _debugMesh = null;
            return;
        }

        _debugMesh.QueueFree();
        _debugMesh = null;
    }
}