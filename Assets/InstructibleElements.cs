using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

public enum ElementType {
	SHAPE = 0,
	CONSTRUCTION = 1,
	TRANSIENT = 2
}

public interface IInstructibleElement {
	ElementType type { get; set; }
	float priority { get; }
	IInstructibleElement[] dependencies { get; }
	// should handle being passed any element of dependencies, and *remove all references to the dependency*
	// so they can can be disposed of by GC if necessary. Should accept inheritor = null
	// handoverable is not responsible for checking that original is a dependency
	bool TypecheckHandover(IInstructibleElement original, IInstructibleElement inheritor);
	void Handover(IInstructibleElement original, IInstructibleElement inheritor);
	void Draw();
	void Inspect();
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
public interface IPositionableElement : IInstructibleElement { // IPositionableElements do not have to be IInstructiblePoints
	Vector2 position { get; set; }                            // IInstructiblePoints are meant to be *points*, while IPositionableElements
}                                                              // are merely meant to be able to set a position, with the getter only for
															   // the purpose of drag tracking (i.e. position=position should do nothing)
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
	public IInstructibleElement[] dependencies { get => parent == null ? new IInstructibleElement[0] : new IInstructibleElement[] { parent }; }
	public bool TypecheckHandover(IInstructibleElement original, IInstructibleElement inheritor) => inheritor == null || inheritor is Shape;
	public void Handover(IInstructibleElement original, IInstructibleElement inheritor) {
		Vector2 oldPosition = position;
		parent = (Shape)inheritor;
		position = oldPosition;
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
	public void Draw() {
		ShapeEditorHints.DiamondAt(position, 1);
	}
	public void Inspect() {
		if (parent == null)
			return;
		if (type == ElementType.TRANSIENT || type == ElementType.CONSTRUCTION)
			parent.Draw();
		if (type == ElementType.CONSTRUCTION)
			ShapeEditorHints.DashedLine(position, parent.position, 1);
	}
}

[Serializable]
public class Line : IInstructibleElement, IInstructibleLine {
	public ElementType type { get; set; }
	public float priority { get => 1; }
	public IInstructibleElement[] dependencies { get => new IInstructibleElement[] { a, b }; }
	public bool TypecheckHandover(IInstructibleElement original, IInstructibleElement inheritor) => inheritor == null || inheritor is IPositionableElement;
	public void Handover(IInstructibleElement original, IInstructibleElement inheritor) {
		if (original == a) {
			a = (IInstructiblePoint)inheritor ?? new Point(a.position);
			if (inheritor == null) ShapeEditor.instance.Register(a);
		} else {
			b = (IInstructiblePoint)inheritor ?? new Point(b.position);
			if (inheritor == null) ShapeEditor.instance.Register(b);
		}
	}
	public Shape parent { get; set; } // only used if ElementType.SHAPE
	public IInstructiblePoint a;
	public IInstructiblePoint b;
	public Vector2 origin { get => a.position; }
	public Vector2 dir { get => (b.position - a.position).normalized; }
	public Vector2 perp { get => Vector2.Perpendicular(dir); }
	public Vector2 bounds { get => extrapolate ? LineUtils.boundless : new Vector2(0, (b.position - a.position).magnitude); }
	public bool extrapolate;
	public Line(IInstructiblePoint a, IInstructiblePoint b, ElementType type = ElementType.CONSTRUCTION) {
		this.type = type;
		this.a = a;
		this.b = b;
	}
	public void Draw() => ShapeEditorHints.Line(this);
	public void Inspect() { }
}

[Serializable]
public class Shape : IInstructibleElement, IPositionableElement {
	public ElementType type { get => ElementType.SHAPE; set { } }
	public float priority { get => 0; }
	public IInstructibleElement[] dependencies { get => new IInstructibleElement[0]; }
	public void Orphan(IInstructibleElement dependent) { }
	public EditableShape monoBehaviour;
	public Vector2 position { get => monoBehaviour.transform.position; set => monoBehaviour.transform.position = value; }
	public bool TypecheckHandover(IInstructibleElement original, IInstructibleElement inheritor) => false;
	public void Handover(IInstructibleElement original, IInstructibleElement inheritor) { }
	public Shape(EditableShape shape) {
		monoBehaviour = shape;
	}
	public void Draw() {
		EditableShape shape = monoBehaviour; // because the lambda can't access instance members of "this"
		ShapeEditorHints.ClosedPath(shape.positions);
	}
	public void Inspect() { }
}

[Serializable]
public class PointOnLine : IInstructibleElement, IInstructiblePoint, IPositionableElement {
	public ElementType type { get; set; }
	public float priority { get => 2; }
	public IInstructibleElement[] dependencies { get => new IInstructibleElement[] { line }; }
	public bool TypecheckHandover(IInstructibleElement original, IInstructibleElement inheritor) => inheritor == null || inheritor is IInstructibleLine;
	public void Handover(IInstructibleElement original, IInstructibleElement inheritor) {
		if (inheritor == null) {
			ShapeEditor.instance.TransformElement(this, new Point(Utility.GetParent(line), position, ElementType.CONSTRUCTION));
			line = null;
		} else {
			Vector2 oldPosition = position;
			line = (IInstructibleLine)inheritor;
			position = oldPosition;
		}
	}
	public IInstructibleLine line;
	public float parameter;
	bool _parameterIsRatio;
	public bool parameterIsRatio {
		get => _parameterIsRatio; set {
			Vector2 oldPosition = position;
			_parameterIsRatio = value;
			position = oldPosition;
		}
	}
	public Vector2 position {
		get => line.origin + line.dir * parameter * (parameterIsRatio ? LineUtils.GetLength(line) : 1);
		set {
			Vector2 snappedPos = LineUtils.PostionOnLineNear(line, value);
			float offset = Vector2.Dot(snappedPos - line.origin, line.dir);
			parameter = parameterIsRatio ? (offset / LineUtils.GetLength(line)) : offset;
		}
	}
	public PointOnLine(IInstructibleLine line, float parameter, bool isRatio = true, ElementType type = ElementType.CONSTRUCTION) {
		this.type = type;
		this.line = line;
		this.parameter = parameter;
		this._parameterIsRatio = isRatio;
	}
	public PointOnLine(IInstructibleLine line, Vector2 position, bool isRatio = true, ElementType type = ElementType.CONSTRUCTION) {
		this.type = type;
		this.line = line;
		this._parameterIsRatio = isRatio;
		this.position = position; // 0-1 between a-b
	}
	public void Draw() {
		float radius = ShapeEditorHints.stdLen * 1.2f;
		ShapeEditorHints.Segments(position + line.perp * radius, position - line.perp * radius);
		if (!parameterIsRatio)
			ShapeEditorHints.SquareAt(position, 0.8f, Vector2.SignedAngle(Vector2.up, line.dir));
	}
	public void Inspect() {
		if (type == ElementType.TRANSIENT)
			line.Draw();
		if (LineUtils.IsFinite(line)) {
			float ratio = parameter / (parameterIsRatio ? 1 : LineUtils.GetLength(line));
			if (ratio < 0)
				ShapeEditorHints.DashedLine(position, line.origin, 1);
			else if (ratio > 1)
				ShapeEditorHints.DashedLine(position, LineUtils.GetEnd(line), 1);
		}
	}
}

public class ParallelLine : IInstructibleElement, IInstructibleLine, IPositionableElement {
	public ElementType type { get; set; }
	public float priority { get => 1; }
	public IInstructibleElement[] dependencies { get => new IInstructibleElement[] { referenceLine }; }
	public bool TypecheckHandover(IInstructibleElement original, IInstructibleElement inheritor) => inheritor == null || inheritor is IInstructibleLine;
	public void Handover(IInstructibleElement original, IInstructibleElement inheritor) {
		if (inheritor == null) {
			ShapeEditor.instance.TransformElement(this, new Line(
					new Point(origin),
					new Point(LineUtils.IsFinite(this) ? LineUtils.GetEnd(this) : origin + dir)
			));
			referenceLine = null;
		} else {
			Vector2 oldPosition = position;
			referenceLine = (IInstructibleLine) inheritor;
			position = oldPosition;
		}
	}
	public IInstructibleLine referenceLine;
	public float offset;
	public Vector2 position {
		get => origin;
		set => offset = Vector2.Dot(perp, value - referenceLine.origin);
	}
	public Vector2 origin { get => referenceLine.origin + referenceLine.perp * offset; }
	public Vector2 dir { get => referenceLine.dir; }
	public Vector2 perp { get => referenceLine.perp; }
	public Vector2 bounds { get => extrapolate ? LineUtils.boundless : referenceLine.bounds; }
	public bool extrapolate;
	public void Draw() => ShapeEditorHints.Line(this);
	public void Inspect() { ShapeEditorHints.DashedLine(origin, referenceLine.origin, 0.5f); }
	public ParallelLine(IInstructibleLine line, Vector2 position, bool extrapolate = false, ElementType type = ElementType.CONSTRUCTION) {
		this.referenceLine = line;
		this.type = type;
		this.position = position;
	}
	public ParallelLine(IInstructibleLine line, float offset, bool extrapolate = false, ElementType type = ElementType.CONSTRUCTION) {
		this.referenceLine = line;
		this.type = type;
		this.offset = offset;
	}
}

public static class LineUtils {
	public static Vector2 boundless = new Vector2(Mathf.NegativeInfinity, Mathf.Infinity);
	public static Vector2 PostionOnLineNear(IInstructibleLine line, Vector2 position) {
		Vector2 disp = position - line.origin;
		float dist = Vector2.Dot(line.dir, disp);
		return line.origin + dist * line.dir;
	}
	public static float GetLength(IInstructibleLine line) {
		if (line.bounds == boundless)
			return Mathf.Infinity;
		else
			return line.bounds.y - line.bounds.x;
	}
	public static Vector2 GetEnd(IInstructibleLine line) {
		return line.origin + line.dir * LineUtils.GetLength(line);
	}
	public static bool IsFinite(IInstructibleLine line) {
		return GetLength(line) != Mathf.Infinity;
	}
}