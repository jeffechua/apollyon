using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using UnityEngine;

public static class Utility {
	static string[] elementTypeSymbols = new string[]{ "S", "C", "T"};
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
	public static string Identify(IInstructibleElement element) {
		if (element == null)
			return "";
		IInstructibleElement despecc = ShapeEditor.instance.Despecify(element);
		return elementTypeSymbols[(int)element.type] + ":" + element.GetType() + (element.type != ElementType.TRANSIENT ? ":" + element.GetHashCode() : " (" + Identify(despecc) + ")");
	}
}

public class EditableShape : MonoBehaviour {

	// Only edge is guaranteed to exist of edge, poly, lrs and mf.
	public EdgeCollider2D[] edges;
	public PolygonCollider2D[] polys;
	public LineRenderer[] lrs;
	public MeshFilter[] mfs;
	const float pointRadius = 0.2f;
	const float lineRadius = 0.1f;

	public Shape shape { get; set; }
	public List<Point> vertices; // ordered
	public List<Line> lines;     // not
	public Vector2[] localPositions { get => vertices.ConvertAll((point) => point.localPosition).ToArray(); }
	public Vector2[] positions { get => vertices.ConvertAll((point) => point.position).ToArray(); }
	Mesh mesh { get => polys.Length > 0 ? polys[0].CreateMesh(true, true) : throw new Exception("Attempt to access shape mesh when no polygon colliders registered."); }

	public bool changed;

	public int NextIndex(int i) => (i + 1) % (vertices.Count);
	public int PrevIndex(int i) => (i + vertices.Count - 1) % (vertices.Count);

	void Start() => shape = new Shape(this);

	// Update is called once per frame
	void Update() {

		if (vertices.Count <= 1) {
			Reset();
			changed = true;
		}

		if (changed) {
			changed = false;
			Apply();
		}

	}

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

	public IInstructiblePoint GetHovered() {

		// Identify point clicks
		Point vertex = vertices.Find((point) => (point.position - ShapeEditor.instance.mousePosition).sqrMagnitude <= pointRadius * pointRadius);
		if (vertex != null) return vertex;

		// Identify line clicks
		for (int i = 0; i < vertices.Count; i++) {
			Vector2 a = vertices[i].position;
			Vector2 b = vertices[NextIndex(i)].position;
			Vector2 dir = b - a;
			Vector2 disp = ShapeEditor.instance.mousePosition - a;
			float parParam = Vector2.Dot(dir, disp) / dir.sqrMagnitude; // 0-1 between a-b
			if (parParam < 0 || parParam > 1)
				continue;
			float perpOffset = Vector2.Dot(Vector2.Perpendicular(dir.normalized), disp);
			if (Math.Abs(perpOffset) < lineRadius)
				return ShapeEditor.instance.Specify(lines.Find((line) => line.a == vertices[i] && line.b == vertices[NextIndex(i)]), ShapeEditor.instance.mousePosition);
		}

		// If nothing else, identify as object itself
		if (polys.Length > 0 && polys[0].OverlapPoint(ShapeEditor.instance.mousePosition))
			return ShapeEditor.instance.Specify(shape, ShapeEditor.instance.mousePosition);

		// This only happens if we hit the EdgeCollider2D outside the line radius and not on a point
		return null;

	}

	public void BreakEdge(Line line, bool thenGrab) {
		int i = vertices.IndexOf((Point)line.a);
		InsertVertex(i + 1, ShapeEditor.instance.snappedMousePos);
		RemoveLine(line);
		AddLine(vertices[i], vertices[i + 1]);
		AddLine(vertices[i+1], vertices[NextIndex(i + 1)]);
		if (thenGrab) {
			ShapeEditor.instance.CastHover();
			ShapeEditor.instance.held = ShapeEditor.instance.hover;
		}
		changed = true;
	}

	public void DeleteVertex(Point vertex) {
		int i = vertices.IndexOf(vertex);
		if (i != -1) {
			List<Line> ls = lines.FindAll((l) => l.dependencies.Contains(vertices[i]));
			RemoveLine(ls[0]); RemoveLine(ls[1]); RemoveVertex(vertices[i]);
			AddLine(vertices[PrevIndex(i)], vertices[i]);
		} else
			Reset();
		changed = true;
	}

	public void DeleteLine(Line line) {
		int i = vertices.IndexOf((Point)line.a);
		RemoveVertex((Point)line.a);
		RemoveVertex((Point)line.b);
		List<Line> ls = lines.FindAll((l) => l.dependencies.Contains(line.a) || l.dependencies.Contains(line.b));
		RemoveLine(ls[0]); RemoveLine(ls[1]); RemoveLine(ls[2]); RemoveVertex((Point)line.a); RemoveVertex((Point)line.b);
		AddLine(vertices[PrevIndex(i)], vertices[1]);
		changed = true;
	}

	void InsertVertex(int index, Vector2 position, bool isWorldSpace = true) {
		Point point = new Point(this, position, ElementType.SHAPE, isWorldSpace);
		vertices.Insert(index, point);
		ShapeEditor.instance.Register(point);
	}
	void RemoveVertex(Point vertex) {
		vertices.Remove(vertex);
		ShapeEditor.instance.Deregister(vertex);
	}
	void AddLine(Point p1, Point p2) {
		Line line = new Line(p1, p2, ElementType.SHAPE);
		line.parent = shape;
		lines.Add(line);
		ShapeEditor.instance.Register(line);
	}
	void RemoveLine(Line line) {
		lines.Remove(line);
		ShapeEditor.instance.Deregister(line);
	}

	public void Reset() {
		ShapeEditor.instance.Deregister(lines.ToArray());
		ShapeEditor.instance.Deregister(vertices.ToArray());
		vertices = new List<Point> { new Point(this, Vector2.left, ElementType.SHAPE, false), new Point(this, Vector2.right, ElementType.SHAPE, false), new Point(this, Vector2.up, ElementType.SHAPE, false) };
		lines = new List<Line> { new Line(vertices[0], vertices[1], ElementType.SHAPE), new Line(vertices[1], vertices[2], ElementType.SHAPE), new Line(vertices[2], vertices[0], ElementType.SHAPE) };
		lines[0].parent = shape; lines[1].parent = shape; lines[2].parent = shape;
		ShapeEditor.instance.Register(vertices.ToArray());
		ShapeEditor.instance.Register(lines.ToArray());
	}

}
