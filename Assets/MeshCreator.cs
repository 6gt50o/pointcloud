﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class MeshCreator {
	private readonly List<Plane> planes;
	private readonly PointCloud pointCloud;
	private Vector2[] shape;

	public Mesh Mesh {
		get;
		private set;
	}

	public MeshCreator(PointCloud pointCloud) {
		this.planes = pointCloud.Planes.ToList();
		this.pointCloud = pointCloud;
		this.shape = this.pointCloud.GetShape();
		if (this.shape.First() == this.shape.Last()) {
			this.shape = this.shape.Skip(1).ToArray();
		}
	}

	public void CreateLayoutMesh() {
		var triangles = new List<Triangle>();

		for (int i = 0; i < this.shape.Length; i++) {
			var v1 = this.shape[i];
			var v2 = this.shape[(i + 1) % this.shape.Length];

			triangles.Add(new Triangle(new Vector3(v1.x, 0, v1.y), new Vector3(v2.x, 0, v2.y), new Vector3(v2.x, 5, v2.y)));
			triangles.Add(new Triangle(new Vector3(v1.x, 0, v1.y), new Vector3(v2.x, 5, v2.y), new Vector3(v1.x, 5, v1.y)));
		}

		this.Mesh = Triangle.CreateMesh(triangles, true);
	}

	private Vector3[] projectToPlane(Vector2[] vertices, Plane plane) {
		Vector3[] result = new Vector3[vertices.Length];
		for (int i = 0; i < vertices.Length; i++) {
			var ray = new Ray(new Vector3(vertices[i].x, -1000, vertices[i].y), Vector3.up);
			float hit;
			if (!plane.Raycast(ray, out hit)) {
				Debug.LogError("Ray didn't hit plane. " + ray.origin + " -> " + ray.direction);
			}
			result[i] = ray.GetPoint(hit);
		}
		return result;
	}

	private IEnumerable<Triangle> createMeshFromPolygon(Plane plane, Vector2[] shape) {
		var vertices = projectToPlane(shape, plane);
		var triangles = HullMesher.TriangulateHull(shape);

		return Triangle.GetTriangles(vertices, triangles);
	}

	public void CreateMesh() {
		var plane1 = this.planes.First();
		var plane2 = this.planes.ElementAt(1);
		var triangles1 = this.createMeshFromPolygon(plane1, this.shape);
		triangles1 = Triangle.CutMesh(triangles1, plane2, false);
		var triangles2 = this.createMeshFromPolygon(plane2, this.shape);
		triangles2 = Triangle.CutMesh(triangles2, plane1, false);

		this.Mesh = Triangle.CreateMesh(triangles1.Concat(triangles2), true);
	}

	public void DisplayMesh() {
		var material = Resources.Load("MeshMaterial", typeof(Material)) as Material;
		var gameObject = new GameObject();
		gameObject.transform.parent = this.pointCloud.transform;
		gameObject.tag = "RoofMesh";
		gameObject.AddComponent<MeshFilter>().sharedMesh = this.Mesh;
		gameObject.AddComponent<MeshRenderer>().material = material;
		gameObject.transform.localPosition = Vector3.zero;
		gameObject.name = "Shape";
	}
}
