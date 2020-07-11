using System;
using System.Linq;
using UnityEngine;


public interface IInstructionExecutor {
	void Click(IInstructibleElement element);
	void DblClick(IInstructibleElement element);
	void StartDrag(IInstructibleElement element);
	void StopDrag(IInstructibleElement element);
	void Rotate(IInstructibleElement element);
	void Delete(IInstructibleElement element);
	IInstructibleElement Select(IInstructibleElement element); // meant to reduce a transient element to the thing it represents
}

public interface IInstructibleElement {
	IInstructionExecutor executor { get; }
	bool Highlight(float radius); // true if replace cursor, false otherwise
}

public interface IInstructiblePoint : IInstructibleElement {
	Vector2 position { get; }
}

public struct Vertex : IInstructiblePoint {
	public EditableShape shape;
	public IInstructionExecutor executor { get => shape; }
	public int i;
	public Vector2 position { get => shape.transform.TransformPoint(shape.points[i]); }
	public Vertex(EditableShape shape, int i) {
		this.shape = shape;
		this.i = i;
	}
	public bool Highlight(float radius) {
		ShapeEditorHints.SquareAt(position, radius, 270);
		return false;
	}
	public override bool Equals(object obj) => obj != null && obj is Vertex && obj.GetHashCode() == GetHashCode();
	public override int GetHashCode() => Utility.Hash(shape, i);
}

public struct Line : IInstructibleElement {
	public IInstructionExecutor executor { get; set; }
	public IInstructiblePoint a;
	public IInstructiblePoint b;
	public Vector2 dir { get => (a.position - b.position).normalized; }
	public Vector2 perp { get => Vector2.Perpendicular(dir); }
	public Line(IInstructiblePoint a, IInstructiblePoint b, IInstructionExecutor executor = null) {
		this.a = a;
		this.b = b;
		this.executor = executor ?? a.executor;
		if (executor == null && a.executor != b.executor)
			throw new ArgumentException("Constitutent points' executors are not the same, but no override was given.");
	}
	public bool Highlight(float radius) {
		ShapeEditorHints.Segments(a.position, b.position);
		return true;
	}
	public override bool Equals(object obj) => obj != null && obj is Line && obj.GetHashCode() == GetHashCode();
	public override int GetHashCode() => Utility.Hash(executor, a, b);
}

public struct Shape : IInstructibleElement {
	public IInstructionExecutor executor { get => shape; }
	public EditableShape shape;
	public Shape(EditableShape shape) {
		this.shape = shape;
	}
	public bool Highlight(float radius) {
		EditableShape shape = this.shape; // because the lambda can't access instance members of "this"
		ShapeEditorHints.Path(shape.edge.points.Select((p) => (Vector2)shape.transform.TransformPoint(p)).ToArray());
		return false;
	}
	public override bool Equals(object obj) => obj != null && obj is Shape && ((Shape)obj).shape == shape;
	public override int GetHashCode() => shape.GetHashCode();
}

public struct WorldPoint : IInstructiblePoint {
	public IInstructionExecutor executor { get; set; }
	public Vector2 position { get; set; }
	public WorldPoint (IInstructionExecutor executor, Vector2 position) {
		this.executor = executor;
		this.position = position;
	}
	public WorldPoint (IInstructionExecutor executor, IInstructiblePoint point) {
		this.executor = executor;
		this.position = point.position;
	}
	public bool Highlight(float radius) {
		ShapeEditorHints.SquareAt(position, radius, 270);
		return false;
	}
	public override bool Equals(object obj) => obj != null && obj is WorldPoint && obj.GetHashCode() == GetHashCode();
	public override int GetHashCode() => Utility.Hash(executor, position); // note parameter is not hashed
}

public struct PointOnLine : IInstructiblePoint {
	public IInstructionExecutor executor { get; set; }
	public Line line;
	public float parameter;
	public Vector2 position { get => line.a.position + (line.b.position - line.a.position) * parameter; }
	public bool transient;
	public PointOnLine(Line line, float parameter, bool transient = false) {
		this.line = line;
		this.parameter = parameter;
		this.executor = line.executor;
		this.transient = transient;
	}
	public PointOnLine(IInstructiblePoint a, IInstructiblePoint b, float parameter, bool transient = false, IInstructionExecutor executor = null) {
		this.line = new Line(a, b, executor);
		this.parameter = parameter;
		this.executor = line.executor;
		this.transient = transient;
	}
	public bool Highlight(float radius) {
		if (!transient) ShapeEditorHints.SquareAt(position, radius, 270);
		ShapeEditorHints.Segments(position + line.perp * radius, position - line.perp * radius);
		line.Highlight(radius);
		return false;
	}
	public override bool Equals(object obj) => obj != null && obj is PointOnLine && obj.GetHashCode() == GetHashCode();
	public override int GetHashCode() => Utility.Hash(executor, line); // note parameter is not hashed
}

public struct PointOnTransform : IInstructiblePoint {
	public IInstructionExecutor executor { get; set; }
	public Transform transform;
	public Vector2 offset;
	public Vector2 position { get => transform.TransformPoint(offset); }
	public bool transient;
	public PointOnTransform(EditableShape shape, Vector2 position, bool inWorldSpace, bool transient = false) {
		executor = shape;
		transform = shape.transform;
		offset = inWorldSpace ? (Vector2)transform.InverseTransformPoint(position) : position;
		this.transient = transient;
	}
	public bool Highlight(float radius) {
		if(!transient) ShapeEditorHints.SquareAt(position, radius, 270);
		executor.Select(this).Highlight(radius);
		return true;
	}
	public override bool Equals(object obj) => obj != null && obj is PointOnTransform && obj.GetHashCode() == GetHashCode();
	public override int GetHashCode() => Utility.Hash(transform, executor); // note offset is not hashed
}