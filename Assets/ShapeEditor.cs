using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

// NB: there should only be one EdgeEditor in the scene
// EditableShapes should register hovers to EdgeEditor in OnMouseOver,
// but actions can only be taken in the Update() of EdgeEditor.
public class ShapeEditor : MonoBehaviour {

	public static ShapeEditor instance;

	// The state of the mouse
	public string overInfo;
	public string hoverInfo;
	public string heldInfo;
	public string selectionsInfo;
	public IInstructiblePoint over; // over is the first hit
	public IInstructiblePoint hover;// while hover is the first hit that's not held
	public IInstructiblePoint held;
	public IInstructiblePoint dragOrigin;
	public List<IInstructibleElement> selections = new List<IInstructibleElement>();
	public bool overing { get => over != null; }
	public bool hovering { get => hover != null; }
	public bool holding { get => held != null; }
	public bool holdingReal { get => holding && Despecify(held).type != ElementType.TRANSIENT; }
	public bool dragging;
	public Vector2 mousePosition { get => Camera.main.ScreenToWorldPoint(Input.mousePosition); }
	public Vector2 snappedMousePos { get => hover?.position ?? mousePosition; }

	bool shifted { get => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift); }

	public float lastClick = 0;
	const float dragThresh = 0.2f;
	const float dblClickMaxGap = 1; // max seconds between clicks to count as dbl click

	// This dictionary serves two purposes:
	//  1) Its keys are a list of all registered (i.e. persistent) IInstructibleElements.
	//  2) The values store each key's dependents. When something is registered, its will be added to its dependencies' dependents list.
	public Dictionary<IInstructibleElement, List<IInstructibleElement>> registry = new Dictionary<IInstructibleElement, List<IInstructibleElement>>();

	void Awake() {
		instance = this;
		gameObject.AddComponent<ShapeEditorHints>();
	}

	// Update is called once per frame
	void Update() {

		CastHover();

		overInfo = Utility.Identify(over);
		hoverInfo = Utility.Identify(hover);
		heldInfo = Utility.Identify(held);
		selectionsInfo = selections.Count > 0 ? selections.Aggregate("", (str, elm) => str + ", " + Utility.Identify(elm)).Substring(2) : "";

		if (Input.GetMouseButtonDown(0)) {
			held = hover ?? new Point(mousePosition);
			if (Time.time - lastClick <= dblClickMaxGap)
				DblClick();
		}

		if (!dragging && holding && (mousePosition - held.position).magnitude > dragThresh) {
			hover = held; // temporary so snappedMousePos will work for this frame
			StartDrag();
			dragging = true;
		}

		if (Input.GetMouseButtonUp(0)) {
			if (dragging) {
				StopDrag();
				dragging = false;
			} else
				Click();
		}

		if (Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.D))
			Delete();

		if (Input.GetKeyDown(KeyCode.Escape))
			Escape();

		if (Input.GetAxis("Mouse ScrollWheel") != 0)
			if (hovering)
				Rotate(hover);

		if (dragQueue.Count > 0) {
			List<DragQueueEntry> shapes = dragQueue.FindAll((e) => e.element is Shape);
			List<DragQueueEntry> loneVertices = dragQueue.FindAll(delegate (DragQueueEntry e) {
				if (Utility.TryCast(e.element, out Point p) && !shapes.Any((s) => s.element == p.parent)) {
					p.parent.monoBehaviour.changed = true;
					return true;
				}
				return false;
			});
			IEnumerable<DragQueueEntry> filtered = shapes.Concat(loneVertices);
			foreach (DragQueueEntry e in filtered)
				e.element.position = e.selfStartPos + ShapeEditor.instance.snappedMousePos - e.mouseStartWorldPos;
		}

	}

	const float pointRadius = 0.2f;
	const float lineRadius = 0.1f;
	public List<IInstructibleElement> OverlapPointAll (Vector2 position) {
		List<IInstructibleElement> hits = new List<IInstructibleElement>();
		foreach(IInstructibleElement element in registry.Keys) {
			if(Utility.TryCast(element, out IInstructiblePoint point)) {
				if ((point.position - position).sqrMagnitude <= pointRadius * pointRadius)
					hits.Add(point);
			}else if(Utility.TryCast(element, out IInstructibleLine line)) {
				Vector2 offset = position - line.origin;
				float perpOffset = Vector2.Dot(offset, line.perp);
				float paraOffset = Vector2.Dot(offset, line.dir);
				if (Mathf.Abs(perpOffset) <= lineRadius && paraOffset <= line.bounds.y && paraOffset >= line.bounds.x)
					hits.Add(line);
			}else if(Utility.TryCast(element, out Shape shape)) {
				Collider2D[] cols = shape.monoBehaviour.GetComponents<Collider2D>();
				if (cols.Any((col) => col.OverlapPoint(position)))
					hits.Add(shape);
			}
		}
		return hits;
	}

	public void CastHover() {
		hover = null;
		over = null;
		List<IInstructibleElement> hits = OverlapPointAll(mousePosition);
		IOrderedEnumerable<IInstructibleElement> ordered = hits.OrderByDescending((hit) => hit.priority);
		foreach(IInstructibleElement hit in ordered) {
			// Set over regardless
			if (over == null) over = Specify(hit, mousePosition);
			// Filters
			if (hit == Despecify(held))
				continue;
			// Set hover if filters pass
			hover = Specify(hit, mousePosition);
			break;
		}
	}

	public void Register(params IInstructibleElement[] elements) {
		foreach (IInstructibleElement element in elements) {
			foreach (IInstructibleElement dependency in element.dependencies) {
				if (registry.TryGetValue(dependency, out List<IInstructibleElement> dependents))
					dependents.Add(dependency);
			}
			registry.Add(element, new List<IInstructibleElement>());
		}
	}
	public void Deregister(params IInstructibleElement[] elements) {
		foreach (IInstructibleElement element in elements) {
			// Inform parents I'm dead
			foreach (IInstructibleElement dependency in element.dependencies) {
				if (registry.TryGetValue(dependency, out List<IInstructibleElement> dependents))
					dependents.Remove(dependency);
			}
			// Inform children they're orphaned
			foreach (IInstructibleElement dependent in registry[element]) {
				dependent.Orphan(element);
			}
			registry.Remove(element);
		}
	}

	void Click() {
		if (shifted) {
			IInstructibleElement despeccOver = Despecify(over);
			IInstructibleElement despeccHeld = Despecify(held);
			if (overing && despeccOver == despeccHeld) {
				if (selections.Any((selection) => selection == despeccHeld))
					selections.Remove(despeccHeld);
				else
					selections.Add(despeccHeld);
			}
			held = null;
		} else {
			// Click behaviour for held
			Escape();
		}
		lastClick = Time.time;
	}

	void DblClick() { // held is guaranteed to be real
		if (Utility.TryCast(held, out PointOnLine pol)) {
			if (pol.line.type == ElementType.SHAPE)
				pol.line.parent.monoBehaviour.BreakEdge(pol.line, true);
		}
	}

	void Escape() {
		selections.Clear();
		held = null;
	}

	void StartDrag() { // held is guaranteed to be non-null, but not necessarily real
		List<IInstructibleElement> targets = selections.Count > 0 ? selections : new List<IInstructibleElement> { Despecify(held) };
		dragOrigin = new Point(held.position);
		foreach (IInstructibleElement target in targets) {
			switch (target.type) {
				case ElementType.SHAPE:
					if (Utility.TryCast(target, out Point vertex)) {
						PushToDragQueue(vertex);
					} else if (Utility.TryCast(target, out Line line)) {
						PushToDragQueue((Point)line.a);
						PushToDragQueue((Point)line.b);
					} else if (Utility.TryCast(target, out Shape shape)) {
						PushToDragQueue(shape);
					}
					break;
				case ElementType.CONSTRUCTION:
					break;
			}
		}
	}

	void StopDrag() { // held is guaranteed to be non-null, but not necessarily real
		dragQueue.Clear();
		held = null;
		dragOrigin = null;
	}

	void Delete() {
		List<IInstructibleElement> targets = selections.Count > 0 ? selections : (over != null ? new List<IInstructibleElement> { Despecify(over) } : new List<IInstructibleElement>());
		IOrderedEnumerable<IInstructibleElement> ordered = targets.OrderByDescending((target) => Utility.CalculateDependencyDepth(target));
		foreach (IInstructibleElement target in ordered) {
			switch (target.type) {
				case ElementType.SHAPE:
					if (Utility.TryCast(target, out Point vertex) && registry.ContainsKey(vertex)) // a vertex may be deleted by line
						vertex.parent.monoBehaviour.DeleteVertex(vertex);
					else if (Utility.TryCast(target, out Line line))
						line.parent.monoBehaviour.DeleteLine(line);
					else if (Utility.TryCast(target, out Shape shape))
						shape.monoBehaviour.Reset();
					break;
				case ElementType.CONSTRUCTION:
					break;
			}
		}
		selections.Clear();
	}

	public IInstructiblePoint Specify(IInstructibleElement element, Vector2 position) {
		if (Utility.TryCast(element, out Line line)) {
			PointOnLine point = line.PointOnNear(position);
			point.type = ElementType.TRANSIENT;
			return point;
		} else if (Utility.TryCast(element, out Shape shape))
			return new Point(shape.monoBehaviour, position, ElementType.TRANSIENT, true);
		return Utility.TryCast(element, out Point p) ? p : new Point(position);
	}
	public IInstructibleElement Despecify(IInstructibleElement element) {
		if (element == null || element.type != ElementType.TRANSIENT)
			return element;
		if (Utility.TryCast(element, out Point point)) {
			return registry.ContainsKey(point) ? point : Despecify(point.parent);
		} else if (Utility.TryCast(element, out PointOnLine pol))
			return pol.line;
		return element;
	}

	const float rotateMultiplier = 10;
	public void Rotate(IInstructibleElement element)
		=> transform.RotateAround(ShapeEditor.instance.snappedMousePos, Vector3.forward, Input.GetAxis("Mouse ScrollWheel") * rotateMultiplier);



	// Helper functions for instructions
	List<DragQueueEntry> dragQueue = new List<DragQueueEntry>();
	struct DragQueueEntry {
		public IPositionableElement element;
		public Vector2 mouseStartWorldPos;
		public Vector2 selfStartPos;
	}
	void PushToDragQueue(IPositionableElement element) {
		dragQueue.Add(new DragQueueEntry {
			element = element,
			mouseStartWorldPos = snappedMousePos,
			selfStartPos = element.position
		});
	}
}

public static class Utility {
	static string[] elementTypeSymbols = new string[] { "S", "C", "T" };
	public static bool TryCast<T>(object obj, out T result)
		=> TryCast(obj, out result, out bool success);
	public static bool TryCast<T>(object obj, out T result, out bool success) {
		if (obj is T) {
			result = (T)obj;
			success = true;
		} else {
			result = default(T);
			success = false;
		}
		return success;
	}
	public static int Hash(params object[] objs) {
		unchecked {
			int hash = 17;
			foreach (object obj in objs)
				hash = hash * 23 + (obj?.GetHashCode() ?? 0);
			return hash;
		}
	}
	public static int CalculateDependencyDepth(IInstructibleElement element) {
		return element.dependencies.Count() > 0 ? element.dependencies.Select((dep) => CalculateDependencyDepth(dep)).Max() + 1 : 0;
	}
	public static string Identify(IInstructibleElement element) {
		if (element == null)
			return "";
		IInstructibleElement despecc = ShapeEditor.instance.Despecify(element);
		return elementTypeSymbols[(int)element.type] + ":" + element.GetType() + (element.type != ElementType.TRANSIENT ? ":" + element.GetHashCode() : " (" + Identify(despecc) + ")");
	}
}