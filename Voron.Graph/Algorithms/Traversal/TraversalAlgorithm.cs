﻿using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Voron.Graph.Algorithms.Traversal
{
    public enum TraversalType
    {
        BFS,
        DFS
    }

    public class TraversalAlgorithm : BaseAlgorithm
    {
        private readonly INodeTraversalStore<TraversalNodeInfo> _processingQueue;
        private readonly HashSet<EdgeTreeKey> TraversedEdges;
        private readonly Transaction _tx;
        private readonly GraphStorage _graphStorage;
        private CancellationToken _cancelToken;
        private readonly Node _rootNode;

        public ushort? EdgeTypeFilter { get; set; }

        public uint? TraverseDepthLimit { get; set; }

        public IVisitor Visitor { get; set; }

        public TraversalAlgorithm(Transaction tx, 
            GraphStorage graphStorage, 
            Node rootNode, 
            TraversalType traversalType,
            CancellationToken cancelToken)
            : this(tx,
            graphStorage,
            rootNode,
            (traversalType == TraversalType.BFS) ?
                (INodeTraversalStore<TraversalNodeInfo>)(new BfsTraversalStore<TraversalNodeInfo>()) : new DfsTraversalStore<TraversalNodeInfo>(),
            cancelToken)
        {
           
        }       

        public TraversalAlgorithm(Transaction tx, 
            GraphStorage graphStorage, 
            Node rootNode, 
            INodeTraversalStore<TraversalNodeInfo> processingQueue,
            CancellationToken cancelToken)
        {
            _processingQueue = processingQueue;

            _cancelToken = cancelToken;
            TraversedEdges = new HashSet<EdgeTreeKey>();
            _graphStorage = graphStorage;
            _tx = tx;
            _rootNode = rootNode;

            _processingQueue.Put(new TraversalNodeInfo
            {
                CurrentNode = rootNode,
                LastEdgeWeight = 0,
                ParentNode = null,
                TotalEdgeWeightUpToNow = 0,
                TraversalDepth = 1
            });
        }
                    

        public void Traverse()
        {
            if (State == AlgorithmState.Running)
                throw new InvalidOperationException("The algorithm is already running");

            OnStateChange(AlgorithmState.Running);

            while (_processingQueue.Count > 0)
            {
                if (_cancelToken != null)
                    _cancelToken.ThrowIfCancellationRequested();

                var traversalInfo = _processingQueue.GetNext();
                
                if (Visitor != null)
                {
                    Visitor.ExamineTraversalInfo(traversalInfo);
                    if(Visitor.ShouldStopTraversal)
                    {
                        OnStateChange(AlgorithmState.Aborted);
                        break;
                    }

                    if (Visitor.ShouldSkipCurrentNode(traversalInfo))
                        continue;
                }

                foreach (var childNodeWithEdge in
                    _graphStorage.Queries.GetAdjacentOf(_tx, traversalInfo.CurrentNode, EdgeTypeFilter ?? 0)
                                         .Where(nodeWithEdge => !TraversedEdges.Contains(nodeWithEdge.EdgeTo.Key)))
                {
                    if (_cancelToken != null)
                        _cancelToken.ThrowIfCancellationRequested();

                    if (TraverseDepthLimit.HasValue && traversalInfo.TraversalDepth == TraverseDepthLimit)
                        continue;

                    TraversedEdges.Add(childNodeWithEdge.EdgeTo.Key);
                    if (Visitor != null)
                    {
                        if (Visitor.ShouldSkipAdjacentNode(childNodeWithEdge))
                            continue;

                        Visitor.DiscoverAdjacent(childNodeWithEdge);
                    }

                    _processingQueue.Put(new TraversalNodeInfo
                    {
                        CurrentNode = childNodeWithEdge.Node,
                        LastEdgeWeight = childNodeWithEdge.EdgeTo.Weight,
                        ParentNode = traversalInfo.CurrentNode,
                        TraversalDepth = traversalInfo.TraversalDepth + 1,
                        TotalEdgeWeightUpToNow = traversalInfo.TotalEdgeWeightUpToNow + childNodeWithEdge.EdgeTo.Weight                        
                    });
                }
            }

            OnStateChange(AlgorithmState.Finished);
        }

        public Task TraverseAsync()
        {
            return Task.Run(() => Traverse(), _cancelToken);
        }       

        #region Traversal Storage Implementations

        private class BfsTraversalStore<T> : INodeTraversalStore<T>
        {
            private readonly Queue<T> _traversalStorage;

            public BfsTraversalStore()
            {
                _traversalStorage = new Queue<T>();
            }

            public T GetNext()
            {
                return _traversalStorage.Dequeue();
            }

            public void Put(T item)
            {
                _traversalStorage.Enqueue(item);
            }


            public int Count
            {
                get { return _traversalStorage.Count; }
            }

            public IEnumerator<T> GetEnumerator()
            {
                return _traversalStorage.GetEnumerator();
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return _traversalStorage.GetEnumerator();
            }
        }

        private class DfsTraversalStore<T> : INodeTraversalStore<T>
        {
            private readonly Stack<T> _traversalStorage;

            public DfsTraversalStore()
            {
                _traversalStorage = new Stack<T>();
            }

            public T GetNext()
            {
                return _traversalStorage.Pop();
            }

            public void Put(T item)
            {
                _traversalStorage.Push(item);
            }

            public int Count
            {
                get { return _traversalStorage.Count; }
            }

            public IEnumerator<T> GetEnumerator()
            {
                return _traversalStorage.GetEnumerator();
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return _traversalStorage.GetEnumerator();
            }
        }

        #endregion
    }
}
