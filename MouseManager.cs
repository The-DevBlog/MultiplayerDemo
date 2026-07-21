using Godot;
using System.Collections.Generic;

public partial class MouseManager : Node
{
	private const float DragThreshold = 8.0f;
	private const float UnitPickRadius = 24.0f;

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
		Unit unit = GetUnitAtScreenPosition(screenPosition);
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

	private Unit GetUnitAtScreenPosition(Vector2 screenPosition)
	{
		Camera3D camera = GetViewport().GetCamera3D();
		if (camera == null)
		{
			return null;
		}

		Unit closestUnit = null;
		float closestDistance = UnitPickRadius;
		foreach (Unit unit in GetUnits())
		{
			if (camera.IsPositionBehind(unit.GlobalPosition))
			{
				continue;
			}

			float distance = screenPosition.DistanceTo(camera.UnprojectPosition(unit.GlobalPosition));
			if (distance <= closestDistance)
			{
				closestDistance = distance;
				closestUnit = unit;
			}
		}

		return closestUnit;
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
		if (_selectedUnits.Count == 0 || !TryGetNavigationTarget(screenPosition, out Vector3 targetPosition))
		{
			return;
		}

		MoveSelectedUnitsTo(targetPosition);
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

	private bool TryGetNavigationTarget(Vector2 screenPosition, out Vector3 targetPosition)
	{
		targetPosition = default;

		if (!TryProjectGroundPosition(screenPosition, out Vector3 groundPosition))
		{
			return false;
		}

		targetPosition = GetClosestNavigationPoint(groundPosition);
		return true;
	}

	private bool TryProjectGroundPosition(Vector2 screenPosition, out Vector3 groundPosition)
	{
		groundPosition = default;

		Camera3D camera = GetViewport().GetCamera3D();
		if (camera == null)
		{
			return false;
		}

		Vector3 rayOrigin = camera.ProjectRayOrigin(screenPosition);
		Vector3 rayDirection = camera.ProjectRayNormal(screenPosition);
		if (Mathf.IsZeroApprox(rayDirection.Y))
		{
			return false;
		}

		float distance = (GetGroundHeight() - rayOrigin.Y) / rayDirection.Y;
		if (distance < 0.0f)
		{
			return false;
		}

		groundPosition = rayOrigin + rayDirection * distance;
		return true;
	}

	private Vector3 GetClosestNavigationPoint(Vector3 worldPosition)
	{
		Camera3D camera = GetViewport().GetCamera3D();
		if (camera == null)
		{
			return worldPosition;
		}

		Rid navigationMap = camera.GetWorld3D().NavigationMap;
		if (!NavigationServer3D.MapGetClosestPointOwner(navigationMap, worldPosition).IsValid)
		{
			return worldPosition;
		}

		return NavigationServer3D.MapGetClosestPoint(navigationMap, worldPosition);
	}

	private float GetGroundHeight()
	{
		return GetGround() is Node3D ground ? ground.GlobalPosition.Y : 0.0f;
	}

	private Node GetGround()
	{
		Node currentScene = GetTree().CurrentScene;
		return currentScene?.GetNodeOrNull("%Ground") ?? currentScene?.GetNodeOrNull("Ground");
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

}
