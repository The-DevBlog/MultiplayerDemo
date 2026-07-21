using Godot;
using System.Collections.Generic;

public partial class MouseManager : Node
{
	private const float DragThreshold = 8.0f;
	private const float RayLength = 1000.0f;

	private readonly List<Unit> _selectedUnits = new();
	private CanvasLayer _selectionOverlay;
	private ColorRect _selectionBox;
	private Vector2 _dragStart;
	private Vector2 _dragCurrent;
	private bool _isDragging;

	public override void _Ready()
	{
		_selectionOverlay = new CanvasLayer
		{
			Layer = 100,
		};
		AddChild(_selectionOverlay);

		_selectionBox = new ColorRect
		{
			Color = new Color(0.2f, 0.55f, 1.0f, 0.22f),
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Visible = false,
		};
		_selectionOverlay.AddChild(_selectionBox);
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseButton)
		{
			HandleMouseButton(mouseButton);
		}
		else if (@event is InputEventMouseMotion mouseMotion && _isDragging)
		{
			_dragCurrent = mouseMotion.Position;
			UpdateSelectionBox(IsBoxSelection());
		}
	}

	private void HandleMouseButton(InputEventMouseButton mouseButton)
	{
		if (mouseButton.ButtonIndex == MouseButton.Left)
		{
			HandleLeftMouseButton(mouseButton);
			return;
		}

		if (mouseButton.ButtonIndex == MouseButton.Right && mouseButton.Pressed)
		{
			TryMoveSelectedUnits(mouseButton.Position);
			GetViewport().SetInputAsHandled();
		}
	}

	private void HandleLeftMouseButton(InputEventMouseButton mouseButton)
	{
		if (mouseButton.Pressed)
		{
			_isDragging = true;
			_dragStart = mouseButton.Position;
			_dragCurrent = mouseButton.Position;
			UpdateSelectionBox(false);
			GetViewport().SetInputAsHandled();
			return;
		}

		if (!_isDragging)
		{
			return;
		}

		_dragCurrent = mouseButton.Position;

		if (IsBoxSelection())
		{
			SelectUnitsInRect(GetSelectionRect());
		}
		else
		{
			HandleSingleClick(mouseButton.Position);
		}

		_isDragging = false;
		UpdateSelectionBox(false);
		GetViewport().SetInputAsHandled();
	}

	private void HandleSingleClick(Vector2 screenPosition)
	{
		if (!TryRaycast(screenPosition, out RaycastHit hit))
		{
			ClearSelection();
			return;
		}

		Unit unit = GetUnitFromCollider(hit.Collider);
		if (unit != null)
		{
			if (IsOnlySelectedUnit(unit))
			{
				ClearSelection();
				return;
			}

			SelectUnits(new List<Unit> { unit });
			return;
		}

		ClearSelection();
	}

	private bool IsOnlySelectedUnit(Unit unit)
	{
		return _selectedUnits.Count == 1 && _selectedUnits[0] == unit;
	}

	private void SelectUnitsInRect(Rect2 selectionRect)
	{
		Camera3D camera = GetViewport().GetCamera3D();
		if (camera == null)
		{
			return;
		}

		List<Unit> unitsInRect = new();
		foreach (Unit unit in GetUnits())
		{
			if (camera.IsPositionBehind(unit.GlobalPosition))
			{
				continue;
			}

			Vector2 screenPosition = camera.UnprojectPosition(unit.GlobalPosition);
			if (selectionRect.HasPoint(screenPosition))
			{
				unitsInRect.Add(unit);
			}
		}

		SelectUnits(unitsInRect);
	}

	private void SelectUnits(List<Unit> units)
	{
		ClearSelection();

		foreach (Unit unit in units)
		{
			unit.SetSelected(true);
			_selectedUnits.Add(unit);
		}
	}

	private void ClearSelection()
	{
		foreach (Unit unit in _selectedUnits)
		{
			if (IsInstanceValid(unit))
			{
				unit.SetSelected(false);
			}
		}

		_selectedUnits.Clear();
	}

	private void TryMoveSelectedUnits(Vector2 screenPosition)
	{
		if (_selectedUnits.Count == 0 || !TryRaycast(screenPosition, out RaycastHit hit, GetUnitExcludes()))
		{
			return;
		}

		if (IsGround(hit.Collider))
		{
			MoveSelectedUnitsTo(hit.Position);
		}
	}

	private void MoveSelectedUnitsTo(Vector3 targetPosition)
	{
		if (_selectedUnits.Count == 0)
		{
			return;
		}

		Vector3 center = Vector3.Zero;
		foreach (Unit unit in _selectedUnits)
		{
			center += unit.GlobalPosition;
		}
		center /= _selectedUnits.Count;

		foreach (Unit unit in _selectedUnits)
		{
			Vector3 offset = unit.GlobalPosition - center;
			offset.Y = 0.0f;
			unit.MoveTo(targetPosition + offset);
		}
	}

	private IEnumerable<Unit> GetUnits()
	{
		foreach (Node node in GetTree().GetNodesInGroup("units"))
		{
			if (node is Unit unit && IsInstanceValid(unit))
			{
				yield return unit;
			}
		}
	}

	private Godot.Collections.Array<Rid> GetUnitExcludes()
	{
		Godot.Collections.Array<Rid> excludes = new();
		foreach (Unit unit in GetUnits())
		{
			excludes.Add(unit.GetRid());
		}

		return excludes;
	}

	private bool TryRaycast(Vector2 screenPosition, out RaycastHit hit)
	{
		return TryRaycast(screenPosition, out hit, null);
	}

	private bool TryRaycast(Vector2 screenPosition, out RaycastHit hit, Godot.Collections.Array<Rid> excludes)
	{
		hit = default;

		Camera3D camera = GetViewport().GetCamera3D();
		if (camera == null)
		{
			return false;
		}

		Vector3 rayOrigin = camera.ProjectRayOrigin(screenPosition);
		Vector3 rayEnd = rayOrigin + camera.ProjectRayNormal(screenPosition) * RayLength;
		PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(rayOrigin, rayEnd);
		query.CollideWithBodies = true;
		query.CollideWithAreas = false;

		if (excludes != null)
		{
			query.Exclude = excludes;
		}

		Godot.Collections.Dictionary result = camera.GetWorld3D().DirectSpaceState.IntersectRay(query);
		if (result.Count == 0)
		{
			return false;
		}

		Node collider = result["collider"].AsGodotObject() as Node;
		if (collider == null)
		{
			return false;
		}

		hit = new RaycastHit(collider, result["position"].AsVector3());
		return true;
	}

	private Unit GetUnitFromCollider(Node collider)
	{
		Node current = collider;
		while (current != null)
		{
			if (current is Unit unit)
			{
				return unit;
			}

			current = current.GetParent();
		}

		return null;
	}

	private bool IsGround(Node node)
	{
		Node ground = GetGround();
		return ground != null && IsNodeOrChildOf(node, ground);
	}

	private Node GetGround()
	{
		Node currentScene = GetTree().CurrentScene;
		return currentScene?.GetNodeOrNull("%Ground") ?? currentScene?.GetNodeOrNull("Ground");
	}

	private static bool IsNodeOrChildOf(Node node, Node parent)
	{
		Node current = node;
		while (current != null)
		{
			if (current == parent)
			{
				return true;
			}

			current = current.GetParent();
		}

		return false;
	}

	private bool IsBoxSelection()
	{
		return _dragStart.DistanceTo(_dragCurrent) >= DragThreshold;
	}

	private Rect2 GetSelectionRect()
	{
		Vector2 position = new(
			Mathf.Min(_dragStart.X, _dragCurrent.X),
			Mathf.Min(_dragStart.Y, _dragCurrent.Y));
		Vector2 size = new(
			Mathf.Abs(_dragStart.X - _dragCurrent.X),
			Mathf.Abs(_dragStart.Y - _dragCurrent.Y));

		return new Rect2(position, size);
	}

	private void UpdateSelectionBox(bool visible)
	{
		_selectionBox.Visible = visible;
		if (!visible)
		{
			return;
		}

		Rect2 rect = GetSelectionRect();
		_selectionBox.Position = rect.Position;
		_selectionBox.Size = rect.Size;
	}

	private readonly struct RaycastHit
	{
		public RaycastHit(Node collider, Vector3 position)
		{
			Collider = collider;
			Position = position;
		}

		public Node Collider { get; }
		public Vector3 Position { get; }
	}
}
