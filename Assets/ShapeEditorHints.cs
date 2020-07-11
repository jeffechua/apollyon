using System.Linq;
using UnityEngine;

public class ShapeEditorHints : MonoBehaviour {

	Material mat;
	ShapeEditor editor;
	const bool spin = false;
	static float spinClock { get => spin ? Time.time : 0; }

	void Start() {
		mat = new Material(Shader.Find("Sprites/Default"));
		editor = ShapeEditor.instance;
	}

	void OnPostRender() {
		GL.PushMatrix();
		mat.SetPass(0);
		GL.LoadOrtho();
		GL.Begin(GL.LINES);

		float radius = Input.GetMouseButton(0) ? 0.1f : 0.15f;

		// Hover
		GL.Color(Color.red);
		if (editor.hovering) {
			editor.hover.Highlight(radius);
			editor.hover.executor.Select(editor.hover).Highlight(radius);
		} else {
			CrossAt(editor.mousePosition, radius, 180);
		}

		// Held
		if (editor.holding) {
			GL.Color(editor.holdingReal && editor.selections.Count == 0 ? Color.green : Color.blue);
			editor.held.Highlight(radius);
			editor.held.executor.Select(editor.held).Highlight(radius);
		}

		// Drag origin
		GL.Color(Color.blue);
		editor.dragOrigin?.Highlight(radius);

		// Selected
		GL.Color(Color.green);
		foreach (IInstructibleElement element in editor.selections)
			element.Highlight(radius);

		GL.End();
		GL.PopMatrix();
	}

	public static void SquareAt(Vector2 position, float radius, float spinRate = 0, float angle = 0) {
		Path(new float[] { 0, 1, 2, 3, 0 }.Select<float, Vector2>((i)
			=> Quaternion.Euler(0, 0, angle + spinClock * spinRate + 90 * i) * Vector2.up * radius + (Vector3)position).ToArray());
	}
	public static void CrossAt(Vector2 position, float radius, float spinRate = 0, float angle = 0) {
		Segments(new float[] { 0, 2, 1, 3 }.Select<float, Vector2>((i)
			=> Quaternion.Euler(0, 0, angle + spinClock * spinRate + 90 * i) * Vector2.up * radius + (Vector3)position).ToArray());
	}

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
