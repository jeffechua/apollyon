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
	IInstructibleElement Despecify(IInstructibleElement element); // meant to reduce a transient element to the thing it represents
}

public interface IInstructibleElement {
	IInstructionExecutor executor { get; }
	void Draw(float radius); // true if replace cursor, false otherwise
}

public interface IInstructiblePoint : IInstructibleElement {
	Vector2 position { get; }
}

[Serializable]
public class Point : IInstructiblePoint {
	public IInstructionExecutor executor { get; set; }
	public Transform parent;
	public Vector2 localPosition;
	public Vector2 position {
		get => parent?.TransformPoint(localPosition) ?? localPosition;
		set => localPosition = parent?.InverseTransformPoint(value) ?? value;
	}
	public Point(Transform parent, Vector2 position, bool inWorldSpace = true, IInstructionExecutor executor = null) {
		this.executor = executor;
		this.parent = parent;
		if (inWorldSpace)
			this.localPosition = position;
		else
			this.position = position;
	}
	public Point(EditableShape shape, Vector2 position, bool inWorldSpace = false) : this(shape.transform, position, inWorldSpace, shape) { }
	public void Draw(float radius) => ShapeEditorHints.SquareAt(position, radius, 270);
	public override bool Equals(object obj) => obj != null && obj is Point && obj.GetHashCode() == GetHashCode();
	public override int GetHashCode() => Utility.Hash(executor, parent, localPosition);
}

[Serializable]
public class Line : IInstructibleElement {
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
	public void Draw(float radius) => ShapeEditorHints.Segments(a.position, b.position);
	public override bool Equals(object obj) => obj != null && obj is Line && obj.GetHashCode() == GetHashCode();
	public override int GetHashCode() => Utility.Hash(executor, a, b);
}

[Serializable]
public class Shape : IInstructibleElement {
	public IInstructionExecutor executor { get => shape; }
	public EditableShape shape;
	public Shape(EditableShape shape) {
		this.shape = shape;
	}
	public void Draw(float radius) {
		EditableShape shape = this.shape; // because the lambda can't access instance members of "this"
		ShapeEditorHints.Path(shape.positions);
	}
	public override bool Equals(object obj) => obj != null && obj is Shape && ((Shape)obj).shape == shape;
	public override int GetHashCode() => shape.GetHashCode();
}

[Serializable]
public struct PointOnLine : IInstructiblePoint {
	public IInstructionExecutor executor { get; set; }
	public Line line;
	public float parameter;
	public Vector2 position { get => line.a.position + (line.b.position - line.a.position) * parameter; }
	public PointOnLine(Line line, float parameter) {
		this.line = line;
		this.parameter = parameter;
		this.executor = line.executor;
	}
	public PointOnLine(IInstructiblePoint a, IInstructiblePoint b, float parameter, IInstructionExecutor executor = null) {
		this.line = new Line(a, b, executor);
		this.parameter = parameter;
		this.executor = line.executor;
	}
	public void Draw(float radius) => ShapeEditorHints.Segments(position + line.perp * radius, position - line.perp * radius);
	public override bool Equals(object obj) => obj != null && obj is PointOnLine && obj.GetHashCode() == GetHashCode();
	public override int GetHashCode() => Utility.Hash(executor, line); // note parameter is not hashed
}