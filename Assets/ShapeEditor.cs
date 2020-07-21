using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public enum EditMode {
	INACTIVE,
	EDIT,
	CONSTRUCTION
}

// NB: there should only be one EdgeEditor in the scene
// EditableShapes should register hovers to EdgeEditor in OnMouseOver,
// but actions can only be taken in the Update() of EdgeEditor.
public class ShapeEditor : MonoBehaviour {

	public static ShapeEditor instance;

	// The state of the mouse
	public EditMode editMode;
	public int activeMouseButton;
	public string overInfo;
	public string hoverInfo;
	public string heldInfo;
	public string selectionsInfo;
	public IInstructibleElement over; // over is the first hit
	public IInstructibleElement hover;// while hover is the first hit that's not held
	public IInstructibleElement held;
	public Vector2 dragOrigin;
	public bool overing { get => over != null; }
	public bool hovering { get => hover != null; }
	public bool holding { get => held != null; }
	public List<IInstructibleElement> multiselected = new List<IInstructibleElement>();
	public List<IInstructibleElement> dragging { get => dragQueue.ConvertAll((e) => (IInstructibleElement)e.element); }
	public bool isDragging;
	public bool isMultiselecting { get => multiselected.Count > 0; }
	public IInstructibleElement reassignVictim;
	public Vector2 mousePosition { get => Camera.main.ScreenToWorldPoint(Input.mousePosition); }
	public Vector2 snappedMousePos { get => Specify(hover, mousePosition)?.position ?? mousePosition; }

	bool shifted { get => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift); }

	public float lastMouseEventTime = 0;
	const float dragThresh = 0.2f;
	const float maxClickGap = 0.5f; // max seconds between clicks to count as dbl click

	// This dictionary serves two purposes:
	//  1) Its keys are a list of all registered (i.e. persistent) IInstructibleElements.
	//  2) The values store each key's dependents. When something is registered, its will be added to its dependencies' dependents list.
	// Elements are responsible for keeping track of what they're dependent on. The registry keeps track of what things each element is a dependency of
	Dictionary<IInstructibleElement, List<IInstructibleElement>> registry = new Dictionary<IInstructibleElement, List<IInstructibleElement>>();
	public List<IInstructibleElement> registered { get => registry.Keys.ToList(); }
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
		selectionsInfo = multiselected.Count > 0 ? multiselected.Aggregate("", (str, elm) => str + ", " + Utility.Identify(elm)).Substring(2) : "";

		if (editMode == EditMode.INACTIVE) {
			if (Input.GetMouseButton(0)) {
				editMode = EditMode.EDIT;
				activeMouseButton = 0;
			} else if (Input.GetMouseButton(1)) {
				editMode = EditMode.CONSTRUCTION;
				activeMouseButton = 1;
			}
		}

		if (Input.GetMouseButtonDown(activeMouseButton)) {
			held = hover;
			dragOrigin = snappedMousePos;
			if (Time.time - lastMouseEventTime <= maxClickGap) {
				DblClick();
				lastMouseEventTime = -1;
			} else {
				lastMouseEventTime = Time.time;
			}
		}

		if (Input.GetMouseButtonUp(activeMouseButton)) {
			if (isDragging) {
				StopDrag();
			} else {
				if (over == held && Time.time - lastMouseEventTime <= maxClickGap) {
					Click();
				}
				held = null;
				if (lastMouseEventTime != -1) // end of double click
					lastMouseEventTime = Time.time;
			}
		}

		if (!isDragging && Input.GetMouseButton(activeMouseButton) && (mousePosition - dragOrigin).magnitude > dragThresh) {
			hover = held; // temporary so snappedMousePos will work for this frame
			StartDrag();
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
			if (editMode == EditMode.EDIT) {
				filtered = dragQueue.FindAll((a) => !dragQueue.Any((b) => b.element == Utility.GetParent(a.element)));
			} else {
				filtered = dragQueue;
			}
			foreach (DragQueueEntry e in filtered) {
				e.element.position = e.selfStartPos + snappedMousePos - dragOrigin;
				if (Utility.GetParent(e.element) != null)
					Utility.GetParent(e.element).monoBehaviour.changed = true;
			}
		}

		if (!Input.GetMouseButton(0) && !Input.GetMouseButton(1))
			editMode = EditMode.INACTIVE;

	}

	const float pointRadius = 0.2f;
	const float lineRadius = 0.1f;
	public List<IInstructibleElement> OverlapPointAll(Vector2 position) {
		List<IInstructibleElement> hits = new List<IInstructibleElement>();
		foreach (IInstructibleElement element in registered) {
			if (Utility.TryCast(element, out Shape shape)) {
				Collider2D[] cols = shape.monoBehaviour.GetComponents<Collider2D>();
				if (cols.Any((col) => col.OverlapPoint(position)))
					hits.Add(shape);
			} else if (Utility.TryCast(element, out IInstructibleLine line)) {
				Vector2 offset = position - line.origin;
				float perpOffset = Vector2.Dot(offset, line.perp);
				float paraOffset = Vector2.Dot(offset, line.dir);
				if (Mathf.Abs(perpOffset) <= lineRadius && (line.infinite || (paraOffset <= line.bounds.y && paraOffset >= line.bounds.x)))
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
		IInstructibleLine[] lines = hits.FindAll((hit) => hit is IInstructibleLine && ValidateHit(hit)).Cast<IInstructibleLine>().ToArray();
		if (lines.Length > 1) {
			hits.Add(new Intersection(lines[0], lines[1], ElementType.TRANSIENT));
		}
		IOrderedEnumerable<IInstructibleElement> ordered = hits.OrderByDescending((hit) => hit.priority);
		foreach (IInstructibleElement hit in ordered) {
			// Set over regardless
			if (over == null) over = hit;
			// Filter
			if (!ValidateHit(hit)) continue;
			// Set hover if filters pass
			hover = hit;
			break;
		}
	}

	public bool ValidateHit(IInstructibleElement hit) {
		if (holding)
			if (hit == held || hit.dependencies.Contains(held)
				|| held.dependencies.Contains(hit) || hit == Utility.GetParent(held))
				return false;
		return true;
	}

	public void Register(params IInstructibleElement[] elements) {
		foreach (IInstructibleElement element in elements) {
			if (element.type == ElementType.TRANSIENT)
				element.type = ElementType.CONSTRUCTION;
			foreach (IInstructibleElement dependency in element.dependencies) {
				if (!registry.ContainsKey(dependency))
					Register(dependency);
				registry[dependency].Add(element);
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
			// Inform dependents they're orphaned
			if (registry.ContainsKey(element)) {
				// If only one dependent and that dependent has no dependents, deregister it. e.g.: point to a line
				if (registry[element].Count == 1 && registry[registry[element][0]].Count == 0)
					Deregister(registry[element][0]);
				// Perform handovers
				List<IInstructibleElement> dependents = new List<IInstructibleElement>(registry[element]); // because registry[elements] will change
				foreach (IInstructibleElement dependent in dependents)
					dependent.Handover(element, null);
				registry.Remove(element);
			}
		}
	}
	public void TransformElement(IInstructibleElement resignee, IInstructibleElement inheritor) {
		Register(inheritor);
		// Inform dependents of the handover
		foreach (IInstructibleElement dependent in new List<IInstructibleElement>(registry[resignee]))
			ReassignLink(dependent, resignee, inheritor);
		Deregister(resignee);
	}
	public void ReassignLink(IInstructibleElement element, IInstructibleElement originalTarget, IInstructibleElement newTarget) {
		element.Handover(originalTarget, newTarget);
		registry[originalTarget].Remove(element);
		if (newTarget != null) {
			if (!registry.ContainsKey(newTarget))
				Register(newTarget);
			registry[newTarget].Add(element);
		}
	}

	void Click() {
		if (shifted) {
			if (multiselected.Contains(held))
				multiselected.Remove(held);
			else
				multiselected.Add(held);
		} else {
			if (editMode == EditMode.EDIT) {
				if (Utility.TryCast(held, out IInstructibleLine line)) {
					line.infinite = !line.infinite;
				} else if (Utility.TryCast(held, out PointOnLine pol)) {
					pol.parameterIsRatio = !pol.parameterIsRatio;
				}
			}
			Release();
		}
	}

	void DblClick() { // held is guaranteed to be real
		if (editMode == EditMode.EDIT) {
			if (Utility.TryCast(held, out Line line) && line.type == ElementType.SHAPE)
				line.parent.monoBehaviour.BreakEdge(line, true);
		} else {
			if (held.type == ElementType.TRANSIENT) {
				held.type = ElementType.CONSTRUCTION;
				Register(held);
			} else if (Utility.TryCast(held, out IInstructibleLine line)) {
				PointOnLine pol = new PointOnLine(line, snappedMousePos, !line.infinite);
				Register(pol);
				held = pol;
				editMode = EditMode.EDIT;
				StartDrag(); // if the user immediately releases, this prevents the ratio/absolute PointOnLine toggle from activating
			} else {
				Utility.TryCast(held, out Point point);
				Utility.TryCast(held, out Shape shape);
				Point newPoint = new Point(shape ?? point?.parent ?? null, snappedMousePos, ElementType.CONSTRUCTION);
				Register(newPoint);
				held = newPoint;
				editMode = EditMode.EDIT;
			}
		}
	}

	void Release() {
		foreach (IInstructibleElement element in cancelQueue) {
			Deregister(element);
		}
		multiselected.Clear();
		held = null;
		isDragging = false;
		dragQueue.Clear();
		cancelQueue.Clear();
	}

	void StartDrag() { // held is guaranteed to be non-null, but not necessarily real
		List<IInstructibleElement> targets = SelectionsOtherwise(held);
		if (editMode == EditMode.EDIT) {
			foreach (IInstructibleElement target in targets) {
				if (Utility.TryCast(target, out IPositionableElement positionable)) {
					PushToDragQueue(positionable);
				}
			}
		} else {
			// Derive lines from other elements
			foreach (IInstructibleElement target in targets) {
				if (Utility.TryCast(target, out IInstructibleLine line)) {
					// Create a parallel line to an existing line
					ParallelLine pLine = new ParallelLine(line, 0);
					Register(pLine);
					cancelQueue.Add(pLine);
					PushToDragQueue(pLine);
					if (!isMultiselecting) {
						PointOnLine displayPoint = new PointOnLine(pLine, snappedMousePos, true, ElementType.TRANSIENT);
						held = displayPoint;
					}
				} else if (Utility.TryCast(target, out IInstructiblePoint point)) {
					// Create a line that starts at an existing point
					Point newPoint = new Point(point.position, ElementType.CONSTRUCTION);
					Line newLine = new Line(point, newPoint);
					Register(newPoint, newLine);
					PushToDragQueue(newPoint);
					cancelQueue.Add(newLine); cancelQueue.Add(newPoint);
					// if single selection, allow the reassignment of the end point to an existing point
					if (!isMultiselecting) {
						held = newPoint;
						reassignVictim = newLine;
					}
				}
			}
			// Free line drawing
			if (!isMultiselecting && dragQueue.Count == 0) {
				Utility.TryCast(held, out Shape shape);
				Point start = new Point(shape, dragOrigin, ElementType.CONSTRUCTION);
				Point end = new Point(dragOrigin, ElementType.CONSTRUCTION);
				Line newLine = new Line(start, end);
				Register(start, end, newLine);
				PushToDragQueue(end);
				cancelQueue.Add(newLine); cancelQueue.Add(start); cancelQueue.Add(end);
				held = end;
				reassignVictim = newLine;
			}
		}
		isDragging = true;
	}

	void StopDrag() {
		// Reassignment
		if (reassignVictim != null && hovering && Utility.CanMakeDependentOn(reassignVictim, hover) && reassignVictim.TypecheckHandover(held, hover)) {
			ReassignLink(reassignVictim, held, hover);
			if (registry[held].Count == 0)
				Deregister(held);
		}
		// Clear caches
		reassignVictim = null;
		dragQueue.Clear();
		cancelQueue.Clear();
		held = null;
		isDragging = false;
	}

	void Delete() {
		List<IInstructibleElement> targets = SelectionsOtherwise(over);
		IOrderedEnumerable<IInstructibleElement> ordered = targets.OrderByDescending((target) => Utility.CalculateDependencyDepth(target));
		foreach (IInstructibleElement target in ordered) {
			switch (target.type) {
				case ElementType.SHAPE:
					if (Utility.TryCast(target, out Point vertex)) // a vertex may be deleted by line
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
		multiselected.Clear();
	}

	public IInstructiblePoint Specify(IInstructibleElement element, Vector2 position) {
		if (Utility.TryCast(element, out IInstructibleLine line)) {
			return new PointOnLine(line, position, !line.infinite);
		} else if (Utility.TryCast(element, out Shape shape))
			return new Point(shape.monoBehaviour, position, ElementType.CONSTRUCTION, true);
		return Utility.TryCast(element, out IInstructiblePoint p) ? p : new Point(position);
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

	// Misc. helper functions
	public List<IInstructibleElement> SelectionsOtherwise(IInstructibleElement alternative) {
		return isMultiselecting ? multiselected :
			(alternative == null ? new List<IInstructibleElement>() : new List<IInstructibleElement> { alternative });
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
		return elementTypeSymbols[(int)element.type] + ":" + element.GetType() + ":" + element.GetHashCode();
	}
	public static Shape GetParent(IInstructibleElement element) {
		if (TryCast(element, out Point point))
			return point.parent;
		else if (TryCast(element, out Line line))
			return line.parent;
		return null;
	}
}