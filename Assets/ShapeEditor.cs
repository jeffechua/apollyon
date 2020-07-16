using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public enum MouseMode {
	INACTIVE,
	EDIT,
	CONSTRUCTION
}
[Serializable]
public enum DragMode {
	SINGLE,
	MULTI
}

// NB: there should only be one EdgeEditor in the scene
// EditableShapes should register hovers to EdgeEditor in OnMouseOver,
// but actions can only be taken in the Update() of EdgeEditor.
public class ShapeEditor : MonoBehaviour {

	public static ShapeEditor instance;

	// The state of the mouse
	public MouseMode mouseMode;
	public DragMode dragMode;
	public int activeMouseButton { get => mouseMode == MouseMode.CONSTRUCTION ? 1 : 0; }
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
	public bool dragging;
	public IInstructibleElement reassignVictim;
	public Vector2 mousePosition { get => Camera.main.ScreenToWorldPoint(Input.mousePosition); }
	public Vector2 snappedMousePos { get => hover?.position ?? mousePosition; }

	bool shifted { get => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift); }

	public float lastClick = 0;
	const float dragThresh = 0.2f;
	const float dblClickMaxGap = 1; // max seconds between clicks to count as dbl click

	// This dictionary serves two purposes:
	//  1) Its keys are a list of all registered (i.e. persistent) IInstructibleElements.
	//  2) The values store each key's dependents. When something is registered, its will be added to its dependencies' dependents list.
	// Elements are responsible for keeping track of what they're dependent on. The registry keeps track of what things each element is a dependency of
	public Dictionary<IInstructibleElement, List<IInstructibleElement>> registry = new Dictionary<IInstructibleElement, List<IInstructibleElement>>();
	List<IInstructibleElement> cancelQueue = new List<IInstructibleElement>();

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

		if (mouseMode == MouseMode.INACTIVE) {
			if (Input.GetMouseButton(0))
				mouseMode = MouseMode.EDIT;
			else if (Input.GetMouseButton(1))
				mouseMode = MouseMode.CONSTRUCTION;
		}

		if (Input.GetMouseButtonDown(activeMouseButton)) {
			held = hover;
			dragOrigin = new Point(snappedMousePos);
			if (Time.time - lastClick <= dblClickMaxGap)
				DblClick();
		}

		if (Input.GetMouseButtonUp(activeMouseButton)) {
			if (dragging) {
				StopDrag();
				dragging = false;
			} else {
				Click();
			}
		}

		if (!dragging && Input.GetMouseButton(activeMouseButton) && (mousePosition - dragOrigin.position).magnitude > dragThresh) {
			hover = held; // temporary so snappedMousePos will work for this frame
			StartDrag();
			dragging = true;
		}

		if (Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.D))
			Delete();

		if (Input.GetKeyDown(KeyCode.Escape)) {
			foreach (IInstructibleElement cancelee in cancelQueue)
				Deregister(cancelee);
			Release();
		}

		if (Input.GetAxis("Mouse ScrollWheel") != 0)
			if (hovering)
				Rotate(hover);

		if (dragQueue.Count > 0) {
			IEnumerable<DragQueueEntry> filtered;
			if (mouseMode == MouseMode.EDIT) {
				filtered = dragQueue.FindAll((a) => !dragQueue.Any((b) => b.element == Utility.GetParent(a.element)));
			} else {
				filtered = dragQueue;
			}
			foreach (DragQueueEntry e in filtered) {
				e.element.position = e.selfStartPos + snappedMousePos - dragOrigin.position;
				if (Utility.GetParent(e.element) != null)
					Utility.GetParent(e.element).monoBehaviour.changed = true;
			}
		}

		if (!Input.GetMouseButton(0) && !Input.GetMouseButton(1))
			mouseMode = MouseMode.INACTIVE;

	}

	const float pointRadius = 0.2f;
	const float lineRadius = 0.1f;
	public List<IInstructibleElement> OverlapPointAll(Vector2 position) {
		List<IInstructibleElement> hits = new List<IInstructibleElement>();
		foreach (IInstructibleElement element in registry.Keys) {
			if (Utility.TryCast(element, out Shape shape)) {
				Collider2D[] cols = shape.monoBehaviour.GetComponents<Collider2D>();
				if (cols.Any((col) => col.OverlapPoint(position)))
					hits.Add(shape);
			} else if (Utility.TryCast(element, out IInstructibleLine line)) {
				Vector2 offset = position - line.origin;
				float perpOffset = Vector2.Dot(offset, line.perp);
				float paraOffset = Vector2.Dot(offset, line.dir);
				if (Mathf.Abs(perpOffset) <= lineRadius && paraOffset <= line.bounds.y && paraOffset >= line.bounds.x)
					hits.Add(line);
			} else if (Utility.TryCast(element, out IInstructiblePoint point)) {
				if ((point.position - position).sqrMagnitude <= pointRadius * pointRadius)
					hits.Add(point);
			}
		}
		return hits;
	}

	public void CastHover() {
		hover = null;
		over = null;
		List<IInstructibleElement> hits = OverlapPointAll(mousePosition);
		IOrderedEnumerable<IInstructibleElement> ordered = hits.OrderByDescending((hit) => hit.priority);
		foreach (IInstructibleElement hit in ordered) {
			// Set over regardless
			if (over == null) over = Specify(hit, mousePosition);
			// Filters
			if (holding)
				if (hit == Despecify(held) || hit.dependencies.Contains(Despecify(held))
					|| held.dependencies.Contains(hit) || hit == Utility.GetParent(Despecify(held)))
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
					dependents.Add(element);
			}
			registry.Add(element, new List<IInstructibleElement>());
		}
	}
	public void Deregister(params IInstructibleElement[] elements) {
		foreach (IInstructibleElement element in elements) {
			// Update the registry
			foreach (IInstructibleElement dependency in element.dependencies)
				if (registry.ContainsKey(dependency))
					registry[dependency].Remove(element);
			// Inform children they're orphaned
			List<IInstructibleElement> dependents = new List<IInstructibleElement>(registry[element]); // because registry[elements] will change
			foreach (IInstructibleElement dependent in dependents)
				dependent.Handover(element, null);
			registry.Remove(element);
		}
	}
	public void TransformElement(IInstructibleElement resignee, IInstructibleElement inheritor) {
		// Inform dependents of the handover
		foreach (IInstructibleElement dependent in registry[resignee])
			dependent.Handover(resignee, inheritor);
		// Update the registry. Don't use Deregister since that orphans dependents.
		foreach (IInstructibleElement dependency in resignee.dependencies)
			if (registry.ContainsKey(dependency))
				registry[dependency].Remove(dependency);
		registry.Remove(resignee);
		Register(inheritor);
	}

	void Click() {
		IInstructibleElement target = Despecify(held);
		if (overing && Despecify(over) == Despecify(held)) {
			if (shifted) {
				if (selections.Any((selection) => selection == target))
					selections.Remove(target);
				else
					selections.Add(target);
			} else {
				if (mouseMode == MouseMode.EDIT) {
					if (Utility.TryCast(target, out ParallelLine pline, out bool isPline) |
						(Utility.TryCast(target, out Line line) && line.type != ElementType.SHAPE)) {
						if (isPline) pline.extrapolate = !pline.extrapolate;
						else line.extrapolate = !line.extrapolate;
					} else if (Utility.TryCast(target, out PointOnLine pol)) {
						pol.parameterIsRatio = !pol.parameterIsRatio;
					}
				}
				Release();
			}
		}
		held = null;
		lastClick = Time.time;
	}

	void DblClick() { // held is guaranteed to be real
		IInstructibleElement target = Despecify(held);
		if (mouseMode == MouseMode.EDIT) {
			if (Utility.TryCast(target, out Line line) && line.type == ElementType.SHAPE)
				line.parent.monoBehaviour.BreakEdge(line, true);
		} else {
			if (Utility.TryCast(target, out IInstructibleLine line)) {
				PointOnLine pol = new PointOnLine(line, snappedMousePos, LineUtils.IsFinite(line));
				Register(pol);
				held = pol;
			} else {
				Utility.TryCast(target, out Point point);
				Utility.TryCast(target, out Shape shape);
				Point newPoint = new Point(shape ?? point?.parent ?? null, snappedMousePos, ElementType.CONSTRUCTION);
				Register(newPoint);
				held = newPoint;
			}
		}
		lastClick = 0;
	}

	void Release() {
		foreach (IInstructibleElement element in cancelQueue) {
			Deregister(element);
		}
		selections.Clear();
		held = null;
	}

	void StartDrag() { // held is guaranteed to be non-null, but not necessarily real
		List<IInstructibleElement> targets;
		if (selections.Count > 0) {
			dragMode = DragMode.MULTI;
			targets = selections;
		} else {
			dragMode = DragMode.SINGLE;
			targets = new List<IInstructibleElement> { Despecify(held) };
		}
		if (mouseMode == MouseMode.EDIT) {
			foreach (IInstructibleElement target in targets) {
				if (Utility.TryCast(target, out IPositionableElement positionable)) {
					PushToDragQueue(positionable);
				} else if (Utility.TryCast(target, out Line line) && line.type == ElementType.SHAPE) {
					PushToDragQueue((Point)line.a, (Point)line.b);
				}
			}
		} else {
			foreach (IInstructibleElement target in targets) {
				if (Utility.TryCast(target, out IInstructibleLine line)) {
					ParallelLine pLine = new ParallelLine(line, 0);
					Register(pLine);
					cancelQueue.Add(pLine);
					PushToDragQueue(pLine);
					if (dragMode == DragMode.SINGLE) {
						PointOnLine displayPoint = new PointOnLine(pLine, snappedMousePos, true, ElementType.TRANSIENT);
						held = displayPoint;
						PushToDragQueue(displayPoint);
					}
				} else if (Utility.TryCast(target, out IInstructiblePoint point)) {
					Point newPoint = new Point(point.position, ElementType.CONSTRUCTION);
					Line newLine = new Line(point, newPoint);
					Register(newPoint, newLine);
					PushToDragQueue(newPoint);
					cancelQueue.Add(newLine); cancelQueue.Add(newPoint);
					if (dragMode == DragMode.SINGLE) {
						held = newPoint;
						reassignVictim = newLine;
					}
				}
			}
			if(dragMode == DragMode.SINGLE && dragQueue.Count == 0) {
				Point start = new Point(snappedMousePos, ElementType.CONSTRUCTION);
				Point end = new Point(snappedMousePos, ElementType.CONSTRUCTION);
				Line newLine = new Line(start, end);
				Register(start, end, newLine);
				PushToDragQueue(end);
				cancelQueue.Add(start); cancelQueue.Add(end); cancelQueue.Add(newLine);
				held = end;
				reassignVictim = newLine;
			}
		}
	}

	void StopDrag() { // held is guaranteed to be non-null, but not necessarily real
		if (reassignVictim != null && hovering && Utility.CanMakeDependentOn(reassignVictim, hover) && reassignVictim.TypecheckHandover(held, hover)) {
			reassignVictim.Handover(held, hover);
			registry[held].Remove(reassignVictim);
			registry[hover].Add(reassignVictim);
			if (registry[held].Count == 0)
				Deregister(held);
		}
		reassignVictim = null;
		dragMode = DragMode.SINGLE;
		dragQueue.Clear();
		cancelQueue.Clear();
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
					Deregister(target);
					break;
			}
		}
		selections.Clear();
	}

	public IInstructiblePoint Specify(IInstructibleElement element, Vector2 position) {
		if (Utility.TryCast(element, out IInstructibleLine line)) {
			return new PointOnLine(line, position, LineUtils.IsFinite(line), ElementType.TRANSIENT);
		} else if (Utility.TryCast(element, out Shape shape))
			return new Point(shape.monoBehaviour, position, ElementType.TRANSIENT, true);
		return Utility.TryCast(element, out IInstructiblePoint p) ? p : new Point(position);
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

	// Helper members for dragging
	List<DragQueueEntry> dragQueue = new List<DragQueueEntry>();
	struct DragQueueEntry {
		public IPositionableElement element;
		public Vector2 selfStartPos;
	}
	void PushToDragQueue(params IPositionableElement[] elements) {
		foreach (IPositionableElement element in elements) {
			dragQueue.Add(new DragQueueEntry {
				element = element,
				selfStartPos = element.position
			});
		}
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
	public static IInstructibleElement[] GetAllDependencies(IInstructibleElement element) {
		return element.dependencies.Aggregate(element.dependencies.ToArray(),
			(arr, dependency) => arr.Concat(dependency.dependencies).ToArray());
	}
	public static bool CanMakeDependentOn(IInstructibleElement dependent, IInstructibleElement dependency) {
		// return GetAllDependencies(dependency).Contains(dependent);
		Queue<IInstructibleElement> queue = new Queue<IInstructibleElement>(dependency.dependencies);
		while (queue.Count > 0) {
			IInstructibleElement element = queue.Dequeue();
			if (element == dependent)
				return false;
			foreach (IInstructibleElement d in element.dependencies)
				queue.Enqueue(d);
		}
		return true;
	}
	public static string Identify(IInstructibleElement element) {
		if (element == null)
			return "";
		IInstructibleElement despecc = ShapeEditor.instance.Despecify(element);
		return elementTypeSymbols[(int)element.type] + ":" + element.GetType() + (element.type != ElementType.TRANSIENT ? ":" + element.GetHashCode() : " (" + Identify(despecc) + ")");
	}
	public static Shape GetParent(IInstructibleElement element) {
		if (TryCast(element, out Point point))
			return point.parent;
		else if (TryCast(element, out Line line))
			return line.parent;
		return null;
	}
}