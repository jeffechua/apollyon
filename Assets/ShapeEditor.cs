using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

// NB: there should only be one EdgeEditor in the scene
// EditableShapes should register hovers to EdgeEditor in OnMouseOver,
// but actions can only be taken in the Update() of EdgeEditor.
public class ShapeEditor : MonoBehaviour {

	public static ShapeEditor instance;

	public string overInfo;
	public string hoverInfo;
	public string heldInfo;
	public string selectionsInfo;

	public IInstructiblePoint over; // over is the first hit
	public IInstructiblePoint hover;// while hover is the first hit that's not held
	public IInstructiblePoint held;
	public IInstructiblePoint dragOrigin;
	public bool overing { get => over != null; }
	public bool hovering { get => hover != null; }
	public bool holding { get => held != null; }
	public bool holdingReal { get => holding && !(held is WorldPoint && held.executor == null); }
	public bool dragging;
	public List<IInstructibleElement> selections = new List<IInstructibleElement>();
	public Vector2 mousePosition { get => Camera.main.ScreenToWorldPoint(Input.mousePosition); }
	public Vector2 snappedMousePos { get => hover?.position ?? mousePosition; }

	bool shifted { get => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift); }
	public float lastClick = 0;

	const float dragThresh = 0.2f;
	const float dblClickMaxGap = 1; // max seconds between clicks to count as dbl click

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
		selectionsInfo = selections.Count > 0 ? selections.Aggregate("", (str, elm) => str + ", " + Utility.Identify(elm)).Substring(2) : "";

		if (Input.GetMouseButtonDown(0)) {
			held = hover ?? new WorldPoint(null, mousePosition);
			if (Time.time - lastClick <= dblClickMaxGap)
				DblClick();
		}

		if (!dragging && holding && (mousePosition - held.position).magnitude > dragThresh) {
			hover = held; // temporary so snappedMousePos will work for this frame
			StartDrag();
			dragging = true;
		}

		if (Input.GetMouseButtonUp(0)) {
			if (dragging) {
				StopDrag();
				dragging = false;
			} else
				Click();
		}

		if (Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.D))
			Delete();

		if (Input.GetKeyDown(KeyCode.Escape))
			Escape();

		if (Input.GetAxis("Mouse ScrollWheel") != 0)
			if (hovering)
				hover?.executor?.Rotate(hover);

	}

	public void CastHover() {
		hover = null;
		over = null;
		Physics2D.OverlapPointAll(mousePosition).FirstOrDefault(delegate (Collider2D col) {
			EditableShape shape = col.GetComponent<EditableShape>();
			IInstructiblePoint returned = shape.GetHovered();
			if (over == null) over = returned;
			if (returned == null || returned.Equals(held))
				return false;
			else {
				hover = returned;
				return true;
			}
		});
	}

	void Click() {
		if (shifted) {
			if (overing && over.Equals(held)) {
				IInstructibleElement element = held?.executor?.Select(held);
				if (selections.Any((selection) => selection.Equals(element)))
					selections.Remove(element);
				else
					selections.Add(element);
			}
			held = null;
		} else {
			held?.executor?.Click(held);
			Escape();
		}
		lastClick = Time.time;
	}

	void DblClick() { // held is guaranteed to be real
		if (shifted) {
			foreach (IInstructibleElement selection in selections)
				selection?.executor?.DblClick(selection);
		} else {
			held.executor.DblClick(held);
		}
	}

	void Escape() {
		selections.Clear();
		held = null;
	}

	void StartDrag() { // held is guaranteed to be non-null, but not necessarily real
		dragOrigin = new WorldPoint(null, held);
		foreach (IInstructibleElement selection in selections)
			selection?.executor?.StartDrag(selection);
		if (selections.Count == 0)
			held.executor?.StartDrag(held);
	}

	void StopDrag() { // held is guaranteed to be non-null, but not necessarily real
		foreach (IInstructibleElement selection in selections)
			selection?.executor?.StopDrag(selection);
		held.executor?.StopDrag(held);
		held = null;
		dragOrigin = null;
	}

	void Delete() {
		if (selections.Count > 0) {
			foreach (IInstructibleElement selection in selections)
				selection?.executor?.Delete(selection);
			selections.Clear();
		} else {
			over?.executor?.Delete(over);
		}
	}
}
