using System.Linq;
using System.Collections.Generic;
using UnityEngine;

public class ShapeEditorHints : MonoBehaviour {

	Material mat;
	ShapeEditor editor;
	const bool spin = false;
	static float spinClock { get => spin ? Time.time : 0; }

	public const float stdLen = 0.1f;

	void Start() {
		mat = new Material(Shader.Find("Sprites/Default"));
		editor = ShapeEditor.instance;
		Cursor.visible = false;
	}

	void OnPostRender() {
		GL.PushMatrix();
		mat.SetPass(0);
		GL.LoadOrtho();
		GL.Begin(GL.LINES);

		// Construction objects;
		GL.Color(Color.yellow);
		foreach (IInstructibleElement element in editor.registry.Keys) {
			if (element.type != ElementType.CONSTRUCTION)
				continue;
			element.Draw();
		}

		// Drag origin
		GL.Color(Color.blue);
		editor.dragOrigin?.Draw();
		
		// Held
		if (editor.holding && editor.dragMode != DragMode.MULTI) {
			if(editor.held.type == ElementType.TRANSIENT) {
				GL.Color(Color.blue);
				editor.held.Draw();
				editor.held.Inspect();
				IInstructibleElement possibleReal = editor.Despecify(editor.held);
				if(possibleReal.type != ElementType.TRANSIENT) {
					GL.Color(Color.green);
					possibleReal.Draw();
				}
			} else {
				GL.Color(Color.green);
				editor.held.Draw();
				editor.held.Inspect();
			}
		}

		// Selected
		GL.Color(Color.green);
		foreach (IInstructibleElement element in editor.selections)
			element.Draw();

		// Hover
		GL.Color(Color.red);
		if (editor.hovering) {
			editor.hover.Draw();
			editor.hover.Inspect();
		} else {
			CrossAt(editor.mousePosition, 1.5f);
		}


		GL.End();
		GL.PopMatrix();
	}

	public static void SquareAt(Vector2 position, float radius, float angle = 0) {
		DiamondAt(position, radius*stdLen/Mathf.Sqrt(2), angle + 45);
	}
	public static void DiamondAt(Vector2 position, float radius, float angle = 0) {
		Path(new float[] { 0, 1, 2, 3, 0 }.Select<float, Vector2>((i)
			=> Quaternion.Euler(0, 0, angle + 90 * i) * Vector2.up * radius * stdLen + (Vector3)position).ToArray());
	}
	public static void CrossAt(Vector2 position, float radius, float angle = 0) {
		Segments(new float[] { 0, 2, 1, 3 }.Select<float, Vector2>((i)
			=> Quaternion.Euler(0, 0, angle + 90 * i) * Vector2.up * radius * stdLen + (Vector3)position).ToArray());
	}
	public static void DashedLine(Vector2 p1, Vector2 p2, float pitch = 1) {
		List<Vector2> points = new List<Vector2>();
		float parameter = 0;
		while (parameter < (p1 - p2).magnitude) {
			points.Add(p1 + (p2 - p1).normalized * parameter);
			parameter += pitch * stdLen / 2;
		}
		if (points.Count % 2 != 0) points.Add(p2);
		Segments(points.ToArray());
	}
	public static void Line(IInstructibleLine line) {
		if(LineUtils.IsFinite(line))
			Segments(line.origin + line.dir * line.bounds.x, line.origin + line.dir * line.bounds.y);
		else {
			Vector2 p1 = Camera.main.WorldToViewportPoint(line.origin);
			Vector2 p2 = Camera.main.WorldToViewportPoint(line.origin + line.dir);
			Vector2 dir = (p2 - p1);
			if(dir.x == 0) {
				GL.Vertex(new Vector2(p1.x, 0));
				GL.Vertex(new Vector2(p1.x, 1));
			} else {
				GL.Vertex(p1 - dir * p1.x / dir.x);
				GL.Vertex(p1 + dir * (1-p1.x) / dir.x);
			}
		}
	}

	public static void ClosedPath(params Vector2[] positions) => Path(positions.Concat(new Vector2[] { positions[0] }).ToArray());
	public static void Path(params Vector2[] positions) {
		Vector2[] newPositions = new Vector2[positions.Length * 2 - 2];
		for (int i = 0; i < positions.Length - 1; i++) {
			newPositions[i * 2] = positions[i];
			newPositions[i * 2 + 1] = positions[i + 1];
		}
		Segments(newPositions);
	}

	public static void Segments(params Vector2[] positions) {
		for (int i = 0; i < positions.Length; i++)
			GL.Vertex((Vector2)Camera.main.WorldToViewportPoint(positions[i]));
	}

}
