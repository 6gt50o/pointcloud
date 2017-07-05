﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Catfood.Shapefile;
using System.IO;
using System.Collections.Concurrent;
using System.Threading;
using System.Globalization;

namespace XYZSeparator {
	public class PointSeparator {
		private ShapeHashSet shapes;
		private string outputFolder;
		private const int QUEUE_LENGTH = 400;
		public int HitCount { get; private set; }

		private ConcurrentDictionary<Polygon, List<Vector3>> currentPoints;

		private ConcurrentQueue<Polygon> polygonQueue;
		private ConcurrentQueue<FileInfo> fileQueue;

		private long hits = 0;
		private long points = 0;
		private int files = 0;
		private int buildings = 0;
		private long dataProcessed = 0;
		private long totalSize;
		private DateTime lastUpdate = DateTime.Now;
		private DateTime startTime;

		public PointSeparator(ShapeHashSet shapes, string outputFolder) {
			this.shapes = shapes;
			this.outputFolder = outputFolder;
			this.currentPoints = new ConcurrentDictionary<Polygon, List<Vector3>>();
			this.polygonQueue = new ConcurrentQueue<Polygon>();
			this.fileQueue = new ConcurrentQueue<FileInfo>();
			this.HitCount = 0;
		}

		public void AddFile(FileInfo file) {
			this.fileQueue.Enqueue(file);
		}

		private void dequeueAndSave() {
			Polygon polygon = null;
			if (!this.polygonQueue.TryDequeue(out polygon)) {
				return;
			}
			List<Vector3> points = null;
			if (!this.currentPoints.TryRemove(polygon, out points)) {
				throw new Exception("Failed to remove points from dictionary.");
			}
			File.AppendAllLines(this.outputFolder + polygon.GetXYZFilename(), points.Select(p => p.ToXYZLine()));
			polygon.SavePolygon(this.outputFolder);
			polygon.SaveMetadata(this.outputFolder);
			this.HitCount += points.Count;
		}

		private void addPoint(Polygon polygon, Vector3 point) {
			if (!this.currentPoints.ContainsKey(polygon)) {
				this.currentPoints[polygon] = new List<Vector3>();
				this.polygonQueue.Enqueue(polygon);				
				buildings++;
			}
			this.currentPoints[polygon].Add(point);
		}

		private void processXYZFile(string filename) {
			foreach (var point in XYZLoader.LoadContinuous(filename)) {
				foreach (var polygon in this.shapes.GetByPoint(point)) {
					if (polygon.Contains(point)) {
						this.addPoint(polygon, point);
						hits++;
					}
				}
				points++;
				if (points % 100 == 0 && (DateTime.Now - lastUpdate).TotalSeconds > 1) {
					this.printStatus();
				}
			}
			this.files++;
		}

		private void printStatus() {
			double progress = (double) this.dataProcessed / (double) this.totalSize;
			var timeElapsed = DateTime.Now - this.startTime;
			Console.WriteLine(formatNumber(this.dataProcessed) + "B, "
				+ this.files.ToString().PadLeft(3) + " files, "
				+ formatNumber(this.points) + " points, "
				+ formatNumber(this.hits) + " hits, "
				+ formatNumber(this.buildings) + " b, "
				+ string.Format(CultureInfo.InvariantCulture, "{0:0.0}", progress * 100.0).PadLeft(4) + "%,"
				+ timeElapsed.ToString(@"h\:mm") + " / -"
				+ TimeSpan.FromTicks((long)(timeElapsed.Ticks * ((1.0 - progress) / progress))).ToString(@"h\:mm"));
			lastUpdate = DateTime.Now;
		}

		public void ClearQueue() {
			while (this.polygonQueue.Any()) {
				this.dequeueAndSave();
			}
		}

		private void xyzFileWorker() {
			while (!this.fileQueue.IsEmpty) {
				FileInfo file = null;
				if (!this.fileQueue.TryDequeue(out file)) {
					continue;
				}
				this.processXYZFile(file.FullName);
				this.dataProcessed += file.Length;
			}
		}

		private void outputWorker() {
			while (!this.fileQueue.IsEmpty) {
				if (this.polygonQueue.Count > PointSeparator.QUEUE_LENGTH) {
					for (int i = 0; i < 100; i++) {
						this.dequeueAndSave();
					}
				}
				Thread.Sleep(200);
			}
		}

		public void Run() {
			const int workerThreadCount = 8;

			this.startTime = DateTime.Now;
			this.totalSize = this.fileQueue.Sum(file => file.Length);

			var threads = new List<Thread>();
			for (int i = 0; i < workerThreadCount; i++) {
				threads.Add(new System.Threading.Thread(new System.Threading.ThreadStart(this.xyzFileWorker)));
			}
			var outputThread = new System.Threading.Thread(new System.Threading.ThreadStart(this.outputWorker));

			foreach (var thread in threads) {
				thread.Start();
			}
			outputThread.Start();

			foreach (var thread in threads) {
				thread.Join();
			}
			outputThread.Join();
			Console.WriteLine("Processed all files, writing remaining output files...");

			this.ClearQueue();
			Console.WriteLine("Found " + this.HitCount + " points in " + (int)System.Math.Floor((DateTime.Now - startTime).TotalMinutes) + "m " + (DateTime.Now - startTime).Seconds + "s.");
		}

		private static string formatNumber(long number) {
			const string letters = "XKMGTE";

			int step = (int)Math.Floor((Math.Log10((double)number) + 0.5) / 3.0);
			double outBase = (double)number / Math.Pow(10, step * 3);

			if (step == 0) {
				return number.ToString();
			} else {
				string s;
				if (outBase < 10) {
					s = "{0:0.00}{1}";
				} else if (outBase < 100) {
					s = "{0:0.0}{1}";
				} else {
					s = " {0:0}{1}";
				}

				return string.Format(CultureInfo.InvariantCulture, s, outBase, letters[step]);
			}
		}
	}
}
