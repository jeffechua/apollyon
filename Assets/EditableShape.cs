using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
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
	public EdgeCollider2D[] edges;
	public PolygonCollider2D[] polys;
	public LineRenderer[] lrs;
	public MeshFilter[] mfs;
	const float pointRadius = 0.2f;
	const float lineRadius = 0.1f;
	const float rotateMultiplier = 10;

	public Shape instructibleElement { get; set; }
	public List<Point> vertices;
	public Vector2[] localPositions { get => vertices.ConvertAll((point) => point.localPosition).ToArray(); }
	public Vector2[] positions { get => vertices.ConvertAll((point) => point.position).ToArray(); }

	public int NextIndex(int i) => (i + 1) % (vertices.Count);

	public void Apply() {
		if (vertices.Count <= 1) return;
		Vector2[] ps = localPositions;
		foreach (EdgeCollider2D edge in edges) edge.points = ps.Concat(new Vector2[] { ps[0] }).ToArray();
		foreach (PolygonCollider2D poly in polys) poly.points = ps;
		foreach (LineRenderer lr in lrs) {
			lr.positionCount = vertices.Count;
			lr.SetPositions(vertices.ConvertAll((p) => (Vector3)p.localPosition).ToArray());
		}
		foreach (MeshFilter mf in mfs) {
			mf.mesh = mesh;
			// As of 8/7/2020, Unity 2019.3.15, the arguments to PolygonCollider2D.CreateMesh literally do nothing
			// So I'm doing this.
			mf.mesh.vertices = mf.mesh.vertices.Select((p) => transform.InverseTransformPoint(p)).ToArray();
			mf.mesh.RecalculateBounds();
		}
	}

	Mesh mesh { get => polys.Length > 0 ? polys[0].CreateMesh(true, true) : throw new Exception("Attempt to access shape mesh when no polygon colliders registered."); }

	bool changed;

	List<DragQueueEntry> dragQueue = new List<DragQueueEntry>();

	void Start() => instructibleElement = new Shape(this);

	// Update is called once per frame
	void Update() {

		if (dragQueue.Count > 0) {
			List<DragQueueEntry> selfDrag = dragQueue.FindAll((e) => e.vertex == null);
			if (selfDrag.Count > 0) {
				transform.position = selfDrag[0].selfStartLocalPos + ShapeEditor.instance.snappedMousePos - selfDrag[0].mouseStartWorldPos; // selfStartLocalPos is a lie; it is world
			} else {
				foreach (DragQueueEntry e in dragQueue.Distinct())
					e.vertex.localPosition = e.selfStartLocalPos + (Vector2)
						transform.InverseTransformVector(ShapeEditor.instance.snappedMousePos - e.mouseStartWorldPos);
				changed = true;
			}
		}

		if (vertices.Count <= 1) {
			Reset();
			changed = true;
		}

		if (changed) {
			changed = false;
			Apply();
		}

	}

	public IInstructiblePoint GetHovered() {

		Vector2 mouse = transform.InverseTransformPoint(ShapeEditor.instance.mousePosition);

		// Identify point clicks
		Point vertex = vertices.Find((point) => (point.position - mouse).sqrMagnitude <= pointRadius * pointRadius);
		if (vertex != null) return vertex;

		// Identify line clicks
		for (int i = 0; i < vertices.Count; i++) {
			Vector2 a = vertices[i].position;
			Vector2 b = vertices[NextIndex(i)].position;
			Vector2 dir = b - a;
			Vector2 disp = mouse - a;
			float parParam = Vector2.Dot(dir, disp) / dir.sqrMagnitude; // 0-1 between a-b
			if (parParam < 0 || parParam > 1)
				continue;
			float perpOffset = Vector2.Dot(Vector2.Perpendicular(dir.normalized), disp);
			if (Math.Abs(perpOffset) < lineRadius)
				return new PointOnLine(vertices[i], vertices[NextIndex(i)], parParam);
		}

		// If nothing else, identify as object itself
		if (polys.Length > 0 && polys[0].OverlapPoint(ShapeEditor.instance.mousePosition))
			return new Point(this, mouse);

		// This only happens if we hit the EdgeCollider2D outside the line radius and not on a point
		return null;

	}

	public void Click(IInstructibleElement element) { }
	public void DblClick(IInstructibleElement element) {
		if (Utility.TryCast(element, out PointOnLine edge))
			BreakEdge(edge, true);
	}
	public void BreakEdge(PointOnLine pt, bool thenGrab) {
		vertices.Insert(vertices.IndexOf((Point)pt.line.a) + 1, new Point(this, pt.position, true));
		if (thenGrab) {
			ShapeEditor.instance.CastHover();
			ShapeEditor.instance.held = ShapeEditor.instance.hover;
		}
	}

	public IInstructibleElement Despecify(IInstructibleElement element) {
		if (Utility.TryCast(element, out Point vertex)) {
			if (vertices.Contains(vertex))
				return vertex;
		} else if (Utility.TryCast(element, out PointOnLine pol) | Utility.TryCast(element, out Line line, out bool isLine))
			return isLine ? line : pol.line;
		return instructibleElement;
	}

	public void StartDrag(IInstructibleElement element) { // input should be despecified!
		if (Utility.TryCast(element, out Point vertex)) {
			PushVertexToDragQueue(vertex);
		} else if (Utility.TryCast(element, out Line line)) {
			PushVertexToDragQueue((Point)line.a);
			PushVertexToDragQueue((Point)line.b);
		} else if (Utility.TryCast(element, out Shape shape)) {
			PushSelfToDragQueue();
		}
	}
	public void StopDrag(IInstructibleElement element) { // input should be despecified!
		if (Utility.TryCast(element, out Point vertex)) {
			if (vertices.Contains(vertex))
				dragQueue.RemoveAll((e) => e.vertex == vertex);
			else
				dragQueue.RemoveAll((e) => e.vertex == null);
		} else if (Utility.TryCast(element, out PointOnLine pol) | Utility.TryCast(element, out Line line, out bool isLine)) {
			Line l = isLine ? line : pol.line;
			dragQueue.RemoveAll((e) => e.vertex == l.a || e.vertex == l.b);
		} else if (Utility.TryCast(element, out Shape shape)) {
			dragQueue.RemoveAll((e) => e.vertex == null);
		}
	}
	void PushVertexToDragQueue(Point vertex) {
		dragQueue.Add(new DragQueueEntry {
			vertex = vertex,
			mouseStartWorldPos = ShapeEditor.instance.snappedMousePos,
			selfStartLocalPos = vertex.position
		});
	}
	void PushSelfToDragQueue() {
		dragQueue.Add(new DragQueueEntry {
			vertex = null,
			mouseStartWorldPos = ShapeEditor.instance.snappedMousePos,
			selfStartLocalPos = transform.position // actually worldpos
		});
	}
	struct DragQueueEntry {
		public Point vertex;
		public Vector2 mouseStartWorldPos;
		public Vector2 selfStartLocalPos;
	}

	public void Delete(IInstructibleElement element) {
		if (Utility.TryCast(element, out Point vertex)) {
			if (vertices.Contains(vertex))
				vertices.Remove(vertex);
			else
				Reset();
		} else if (Utility.TryCast(element, out PointOnLine pol) | Utility.TryCast(element, out Line line, out bool isLine)) {
			Line l = isLine ? line : pol.line;
			vertices.Remove((Point)l.a);
			vertices.Remove((Point)l.b);
		} else if (Utility.TryCast(element, out Shape shape)) {
			Reset();
		}
		changed = true;
	}

	public void Rotate(IInstructibleElement element) {
		transform.RotateAround(ShapeEditor.instance.snappedMousePos, Vector3.forward, Input.GetAxis("Mouse ScrollWheel") * rotateMultiplier);
	}

	public void Reset() {
		vertices = new List<Point> { new Point(this, Vector2.left), new Point(this, Vector2.right), new Point(this, Vector2.up) };
	}

}
