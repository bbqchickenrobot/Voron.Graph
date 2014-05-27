﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json.Linq;
using Voron.Graph.Extensions;
using Voron.Trees;

namespace Voron.Graph.Impl
{
	public class GraphQueries
	{		

		public T GetFromSystemMetadata<T>(Transaction tx, string key)
		{
			ReadResult metadataReadResult = tx.SystemTree.Read(tx.GraphMetadataKey);
			Debug.Assert(metadataReadResult.Version > 0);

			using (Stream metadataStream = metadataReadResult.Reader.AsStream())
			{
				if (metadataStream == null)
					return default(T);

				JObject metadata = metadataStream.ToJObject();
				return metadata.Value<T>(key);
			}
		}

		public IEnumerable<Edge> GetEdgesOf(Transaction tx, Node node)
		{
			if (tx == null) throw new ArgumentNullException("tx");
			if (node == null) throw new ArgumentNullException("node");

			using (TreeIterator edgeIterator = tx.EdgeTree.Iterate())
			{
				Slice nodeKey = node.Key.ToSlice();
				edgeIterator.RequiredPrefix = nodeKey;
				if (!edgeIterator.Seek(nodeKey))
					yield break;

				do
				{
					EdgeTreeKey edgeKey = edgeIterator.CurrentKey.ToEdgeTreeKey();
					ValueReader edgeValueReader = edgeIterator.CreateReaderForCurrent();

					using (Stream edgeEtagAndValueAsStream = edgeValueReader.AsStream())
					{
						Etag etag;
						JObject value;

						Util.EtagAndValueFromStream(edgeEtagAndValueAsStream, out etag, out value);
						var edge = new Edge(edgeKey, value, etag);

						yield return edge;
					}
				} while (edgeIterator.MoveNext());
			}
		}

		public IEnumerable<Node> GetAdjacentOf(Transaction tx, Node node, ushort type = 0)
		{
			if (tx == null) throw new ArgumentNullException("tx");
			if (node == null) throw new ArgumentNullException("node");

			var alreadyRetrievedKeys = new HashSet<long>();
			using (TreeIterator edgeIterator = tx.EdgeTree.Iterate())
			{
				Slice nodeKey = node.Key.ToSlice();
				edgeIterator.RequiredPrefix = nodeKey;
				if (!edgeIterator.Seek(nodeKey))
					yield break;

				do
				{
					EdgeTreeKey edgeKey = edgeIterator.CurrentKey.ToEdgeTreeKey();
					if (edgeKey.Type != type)
						continue;

					if (!alreadyRetrievedKeys.Contains(edgeKey.NodeKeyTo))
					{
						alreadyRetrievedKeys.Add(edgeKey.NodeKeyTo);
						Node adjacentNode = LoadNode(tx, edgeKey.NodeKeyTo);
						yield return adjacentNode;
					}
				} while (edgeIterator.MoveNext());
			}
		}

		public bool IsIsolated(Transaction tx, Node node)
		{
			if (tx == null) throw new ArgumentNullException("tx");
			if (node == null) throw new ArgumentNullException("node");

			using (TreeIterator edgeIterator = tx.EdgeTree.Iterate())
			{
				edgeIterator.RequiredPrefix = node.Key.ToSlice();
				return edgeIterator.Seek(Slice.BeforeAllKeys);
			}
		}

		public bool ContainsEdge(Transaction tx, Edge edge)
		{
			if (tx == null) throw new ArgumentNullException("tx");
			if (edge == null) throw new ArgumentNullException("edge");

			return tx.EdgeTree.ReadVersion(edge.Key.ToSlice()) > 0;
		}

		public bool ContainsNode(Transaction tx, Node node)
		{
			if (tx == null) throw new ArgumentNullException("tx");
			if (node == null) throw new ArgumentNullException("node");

			return ContainsNode(tx, node.Key);
		}

		public bool ContainsNode(Transaction tx, long nodeKey)
		{
			if (tx == null) throw new ArgumentNullException("tx");

			return tx.NodeTree.ReadVersion(nodeKey.ToSlice()) > 0;
		}

		public Node LoadNode(Transaction tx, long nodeKey)
		{
			if (tx == null) throw new ArgumentNullException("tx");

			ReadResult readResult = tx.NodeTree.Read(nodeKey.ToSlice());
			if (readResult == null)
				return null;

			using (Stream etagAndValueAsStream = readResult.Reader.AsStream())
			{
				Etag etag;
				JObject value;

				Util.EtagAndValueFromStream(etagAndValueAsStream, out etag, out value);
				return new Node(nodeKey, value, etag);
			}
		}


		public IEnumerable<Edge> GetEdgesBetween(Transaction tx, Node nodeFrom, Node nodeTo, ushort? type = null)
		{
			if (tx == null) throw new ArgumentNullException("tx");
			if (nodeFrom == null)
				throw new ArgumentNullException("nodeFrom");
			if (nodeTo == null)
				throw new ArgumentNullException("nodeTo");

			using (TreeIterator edgeIterator = tx.EdgeTree.Iterate())
			{
				edgeIterator.RequiredPrefix = Util.EdgeKeyPrefix(nodeFrom, nodeTo);
				if (!edgeIterator.Seek(edgeIterator.RequiredPrefix))
					yield break;

				do
				{
					EdgeTreeKey edgeTreeKey = edgeIterator.CurrentKey.ToEdgeTreeKey();
					if (type.HasValue && edgeTreeKey.Type != type)
						continue;

					ValueReader valueReader = edgeIterator.CreateReaderForCurrent();
					using (Stream etagAndValueAsStream = valueReader.AsStream())
					{
						Etag etag;
						JObject value;

						Util.EtagAndValueFromStream(etagAndValueAsStream, out etag, out value);
						yield return new Edge(edgeTreeKey, value, etag);
					}
				} while (edgeIterator.MoveNext());
			}
		}
	}
}