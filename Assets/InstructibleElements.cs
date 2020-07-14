using System;
using System.Collections.Generic;
using UnityEngine;

public enum ElementType {
	SHAPE = 0,
	CONSTRUCTION = 1,
	TRANSIENT = 2
}

public interface IInstructibleElement {
	ElementType type { get; }
	float priority { get; }
	IEnumerable<IInstructibleElement> dependencies { get; }
	// should handle being passed any element of dependencies, and *remove all references to the dependency*
	// so they can can be disposed of by GC
	void Orphan(IInstructibleElement dependency);
	void Draw(float radius); // true if replace cursor, false otherwise
}

public interface IInstructiblePoint : IInstructibleElement {
	Vector2 position { get; }
}
public interface IInstructibleLine : IInstructibleElement {
	Vector2 origin { get; }
	Vector2 dir { get; } // normalized
	Vector2 perp { get; }
	Vector2 bounds { get; } // the line segment is (a-x*dir) -> (a*y*dir). ±infinity for a line. Length of segment is y-x
}
public interface IPositionableElement : IInstructibleElement {
	Vector2 position { get;  set; }
}

[Serializable]
public class Point : IInstructiblePoint, IPositionableElement {
	public ElementType type { get; set; }
	public float priority { get => 2; }
	public Shape parent; // If ElementType.CONSTRUCTION, the transform reference origin. If ElementType.SHAPE, the shape of which I am a vertex
	public Vector2 localPosition;
	public Vector2 position {
		get => parent?.monoBehaviour.transform.TransformPoint(localPosition) ?? localPosition;
		set => localPosition = parent?.monoBehaviour.transform.InverseTransformPoint(value) ?? value;
	}
	public IEnumerable<IInstructibleElement> dependencies { get => parent == null ? new IInstructibleElement[0] : new IInstructibleElement[] { parent }; }
	public void Orphan(IInstructibleElement dependency) {
		localPosition = position;
		parent = null;
	}
	public Point(Vector2 position, ElementType type = ElementType.CONSTRUCTION) {
		this.type = type;
		this.parent = null;
		this.localPosition = position;
	}
	public Point(Shape parent, Vector2 position, ElementType type, bool inWorldSpace = true) {
		this.type = type;
		this.parent = parent;
		if (inWorldSpace)
			this.position = position;
		else
			this.localPosition = position;
	}
	public Point(EditableShape shape, Vector2 position, ElementType type, bool inWorldSpace = true) : this(shape.shape, position, type, inWorldSpace) { }
	public void Draw(float radius) => ShapeEditorHints.SquareAt(position, radius, 270);
}

[Serializable]
public class Line : IInstructibleElement, IInstructibleLine {
	public ElementType type { get; set; }
	public float priority { get => 1; }
	public IEnumerable<IInstructibleElement> dependencies { get => new IInstructibleElement[] { a, b }; }
	public void Orphan(IInstructibleElement dependency) {
		if (dependency == a) {
			a = new Point(a.position);
			ShapeEditor.instance.Register(a);
		} else {
			b = new Point(b.position);
			ShapeEditor.instance.Register(a);
		}
	}
	public Shape parent { get; set; } // only used if ElementType.SHAPE
	public IInstructiblePoint a;
	public IInstructiblePoint b;
	public Vector2 origin { get => a.position; }
	public Vector2 dir { get => (b.position - a.position).normalized; }
	public Vector2 perp { get => Vector2.Perpendicular(dir); }
	public Vector2 bounds { get => new Vector2(0, (b.position-a.position).magnitude); }
	public Line(IInstructiblePoint a, IInstructiblePoint b, ElementType type = ElementType.CONSTRUCTION) {
		this.type = type;
		this.a = a;
		this.b = b;
	}
	public void Draw(float radius) => ShapeEditorHints.Segments(a.position, b.position);
	public PointOnLine PointOnNear(Vector2 position) {
		Vector2 disp = position - origin;
		float param = Vector2.Dot(dir, disp) / bounds.y; // 0-1 between a-b
		return new PointOnLine(this, param);
	}
}

[Serializable]
public class Shape : IInstructibleElement, IPositionableElement {
	public ElementType type { get => ElementType.SHAPE; }
	public float priority { get => 0; }
	public IEnumerable<IInstructibleElement> dependencies { get => new IInstructibleElement[0]; }
	public void Orphan(IInstructibleElement dependent) { }
	public EditableShape monoBehaviour;
	public Vector2 position { get => monoBehaviour.transform.position;  set => monoBehaviour.transform.position = value; }
	public Shape(EditableShape shape) {
		monoBehaviour = shape;
	}
	public void Draw(float radius) {
		EditableShape shape = monoBehaviour; // because the lambda can't access instance members of "this"
		ShapeEditorHints.ClosedPath(shape.positions);
	}
}

[Serializable]
public struct PointOnLine : IInstructiblePoint {
	public ElementType type { get; set; }
	public float priority { get => 2; }
	public IEnumerable<IInstructibleElement> dependencies { get => new IInstructibleElement[] { line }; }
	public void Orphan(IInstructibleElement dependent) { line = null; ShapeEditor.instance.Deregister(this); }
	public Line line;
	public float parameter;
	public Vector2 position { get => line.a.position + (line.b.position - line.a.position) * parameter; }
	public PointOnLine(Line line, float parameter, ElementType type = ElementType.CONSTRUCTION) {
		this.type = type;
		this.line = line;
		this.parameter = parameter;
	}
	public PointOnLine(IInstructiblePoint a, IInstructiblePoint b, float parameter, ElementType type = ElementType.CONSTRUCTION) {
		this.type = type;
		this.line = new Line(a, b, type);
		this.parameter = parameter;
	}
	public void Draw(float radius) => ShapeEditorHints.Segments(position + line.perp * radius, position - line.perp * radius);
}