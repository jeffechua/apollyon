using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class Utility {
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
	public static string Identify(object obj) {
		return obj == null ? "" : obj.GetType() + ":" + obj.GetHashCode();
	}
}

public class EditableShape : MonoBehaviour, IInstructionExecutor {

	// Only edge is guaranteed to exist of edge, poly, lrs and mf.
	public EdgeCollider2D edge;
	public PolygonCollider2D poly;
	public LineRenderer[] lrs;
	public MeshFilter mf;
	const float pointRadius = 0.2f;
	const float lineRadius = 0.1f;
	const float rotateMultiplier = 10;

	public Vector2[] points {
		get => edge.points.Take(edge.pointCount - 1).ToArray();
		set {
			if (value.Length <= 1) return;
			edge.points = value.Concat(new Vector2[] { value[0] }).ToArray();
			if (poly) poly.points = value;
			foreach (LineRenderer lr in lrs) {
				lr.positionCount = value.Length;
				lr.SetPositions(Array.ConvertAll(value, (p) => (Vector3)p));
			}
			if (mf) {
				mf.mesh = mesh;
				// As of 8/7/2020, Unity 2019.3.15, the arguments to PolygonCollider2D.CreateMesh literally do nothing
				// So I'm doing this.
				mf.mesh.vertices = mf.mesh.vertices.Select((p) => transform.InverseTransformPoint(p)).ToArray();
				mf.mesh.RecalculateBounds();
			}
		}
	}

	Mesh mesh { get => poly ? poly.CreateMesh(true, true) : edge.CreateMesh(true, true); }

	List<DragQueueEntry> dragQueue = new List<DragQueueEntry>();
	List<int> deleteQueue = new List<int>();

	// Start is called before the first frame update
	void Start() {
		if (!edge) edge = GetComponent<EdgeCollider2D>(); // ?? operator doesn't work on Monobehaviours
		if (!edge) edge = gameObject.AddComponent<EdgeCollider2D>();
		edge.edgeRadius = pointRadius;
		if (!poly) poly = GetComponent<PolygonCollider2D>();
		if (lrs == null || lrs.Length == 0) lrs = gameObject.GetComponentsInChildren<LineRenderer>();
		if (!mf) mf = gameObject.GetComponent<MeshFilter>();
		points = points;
	}

	// Update is called once per frame
	void Update() {

		if (dragQueue.Count > 0) {
			List<DragQueueEntry> selfDrag = dragQueue.FindAll((e) => e.i == -1);
			if (selfDrag.Count > 0) {
				transform.position = selfDrag[0].selfStartLocalPos + ShapeEditor.instance.snappedMousePos - selfDrag[0].mouseStartWorldPos; // selfStartLocalPos is a lie; it is world
			} else {
				Vector2[] pts = points;
				foreach (DragQueueEntry e in dragQueue.Distinct()) {
					pts[e.i] = e.selfStartLocalPos + (Vector2)
						transform.InverseTransformVector(ShapeEditor.instance.snappedMousePos - e.mouseStartWorldPos);
				}
				points = pts;
			}
		}

		if (deleteQueue.Count > 0) {
			if (deleteQueue.Contains(-1)) {
				Reset();
			} else {
				deleteQueue.Distinct().OrderByDescending((i) => i);
				List<Vector2> pts = points.ToList();
				for (int i = deleteQueue.Count - 1; i >= 0; i--) {
					pts.RemoveAt(i);
				}
				points = pts.ToArray();
			}
		}

		if (edge.pointCount <= 2)
			Reset();

	}

	public IInstructiblePoint GetHovered() {

		Vector2[] pts = points;
		Vector2 mouse = transform.InverseTransformPoint(ShapeEditor.instance.mousePosition);

		// Identify point clicks
		int i = Array.FindIndex(pts, (point) => (point - mouse).sqrMagnitude <= pointRadius * pointRadius);
		if (i != -1)
			return new Vertex(this, i);

		// Identify line clicks
		for (i = 0; i < pts.Length; i++) {
			Vector2 a = pts[i];
			Vector2 b = pts[(i + 1) % pts.Length];
			Vector2 dir = b - a;
			Vector2 disp = mouse - a;
			float parParam = Vector2.Dot(dir, disp) / dir.sqrMagnitude; // 0-1 between a-b
			if (parParam < 0 || parParam > 1)
				continue;
			float perpOffset = Vector2.Dot(Vector2.Perpendicular(dir.normalized), disp);
			if (Math.Abs(perpOffset) < lineRadius)
				return new PointOnLine(new Vertex(this, i), new Vertex(this, (i + 1) % pts.Length), parParam, true);
		}

		// If nothing else, identify as object itself
		if (poly && poly.OverlapPoint(ShapeEditor.instance.mousePosition))
			return new PointOnTransform(this, mouse, false, true);

		// This only happens if we hit the EdgeCollider2D outside the line radius and not on a point
		return null;

	}

	public void Click(IInstructibleElement element) { }
	public void DblClick(IInstructibleElement element) {
		if (Utility.TryCast(element, out PointOnLine edge))
			BreakEdge(edge, true);
	}
	public void BreakEdge(PointOnLine pt, bool thenGrab) {
		List<Vector2> newPoints = points.ToList();
		newPoints.Insert(((Vertex)pt.line.a).i + 1, transform.InverseTransformPoint(pt.position));
		points = newPoints.ToArray();
		if (thenGrab) {
			ShapeEditor.instance.CastHover();
			ShapeEditor.instance.held = ShapeEditor.instance.hover;
		}
	}

	public void StartDrag(IInstructibleElement element) {
		if (Utility.TryCast(element, out Vertex vert)) {
			PushVertexToDragQueue(vert.i);
		} else if (Utility.TryCast(element, out PointOnLine pol) | Utility.TryCast(element, out Line line, out bool isLine)) {
			int i = ((Vertex)(isLine ? line : pol.line).a).i;
			PushVertexToDragQueue(i);
			PushVertexToDragQueue((i + 1) % (edge.pointCount - 1));
		} else if (Utility.TryCast(element, out PointOnTransform pot) || Utility.TryCast(element, out Shape shape)) {
			dragQueue.Add(new DragQueueEntry {
				i = -1,
				mouseStartWorldPos = ShapeEditor.instance.snappedMousePos,
				selfStartLocalPos = transform.position // actually worldpos
			});
		}
	}
	public void StopDrag(IInstructibleElement element) {
		if (Utility.TryCast(element, out Vertex vert)) {
			dragQueue.RemoveAll((e) => e.i == vert.i);
		} else if (Utility.TryCast(element, out PointOnLine pol) | Utility.TryCast(element, out Line line, out bool isLine)) {
			int i = ((Vertex)(isLine ? line : pol.line).a).i;
			dragQueue.RemoveAll((e) => e.i == i);
			dragQueue.RemoveAll((e) => e.i == (i + 1) % (edge.pointCount - 1));
		} else if (Utility.TryCast(element, out PointOnTransform pot) || Utility.TryCast(element, out Shape shape)) {
			dragQueue.RemoveAll((e) => e.i == -1);
		}
	}
	void PushVertexToDragQueue(int i) {
		dragQueue.Add(new DragQueueEntry {
			i = i,
			mouseStartWorldPos = ShapeEditor.instance.snappedMousePos,
			selfStartLocalPos = points[i]
		});
	}
	struct DragQueueEntry {
		public int i;
		public Vector2 mouseStartWorldPos;
		public Vector2 selfStartLocalPos;
	}

	public void Delete(IInstructibleElement element) {
		if (Utility.TryCast(element, out Vertex vert)) {
			deleteQueue.Add(vert.i);
		} else if (Utility.TryCast(element, out PointOnLine pol) | Utility.TryCast(element, out Line line, out bool isLine)) {
			int i = ((Vertex)(isLine ? line : pol.line).a).i;
			deleteQueue.Add(i);
			deleteQueue.Add((i + 1) % (edge.pointCount - 1));
		} else if (Utility.TryCast(element, out PointOnTransform pot) || Utility.TryCast(element, out Shape shape)) {
			deleteQueue.Add(-1);
		}
	}

	public void Rotate(IInstructibleElement element) {
		transform.RotateAround(ShapeEditor.instance.snappedMousePos, Vector3.forward, Input.GetAxis("Mouse ScrollWheel") * rotateMultiplier);
	}

	public IInstructibleElement Select(IInstructibleElement element) {
		if (Utility.TryCast(element, out PointOnLine pol)) {
			return pol.line;
		} else if (Utility.TryCast(element, out PointOnTransform pot)) {
			return new Shape(this);
		}
		return element;
	}

	public void Reset() {
		points = new Vector2[] { Vector2.left, Vector2.right, Vector2.up };
	}

}
